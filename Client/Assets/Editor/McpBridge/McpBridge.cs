using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace PingTheKiller.McpBridge
{
    /// <summary>
    /// Editor-side half of Tools/UnityMcp. Listens on 127.0.0.1:7801 and runs commands on the main thread.
    ///
    /// Framing (must match Tools/UnityMcp/BridgeFraming.cs): [int32 Length, little-endian][UTF-8 JSON],
    /// Length = payload bytes, 1..8 MiB; anything else closes the connection.
    /// Request {"id":n,"cmd":"name","args":{...}} -> response {"id":n,"ok":true,"result":...} or
    /// {"id":n,"ok":false,"error":"..."}.
    ///
    /// Socket threads only frame and parse; commands are queued and drained on EditorApplication.update.
    /// The listener is stopped before every domain reload and restarted by [InitializeOnLoad] afterwards;
    /// the external MCP server reconnects on its own.
    /// </summary>
    [InitializeOnLoad]
    internal static class McpBridge
    {
        public const int Port = 7801;
        public const string BridgeVersion = "1.0.0";
        private const int MaxPayloadBytes = 8 * 1024 * 1024;
        private const string BatchEnvVar = "UNITY_MCP_BATCH";

        /// <summary>Changes on every domain load; lets the server detect that a reload happened.</summary>
        public static readonly string Epoch = Guid.NewGuid().ToString("N");
        public static readonly DateTime DomainLoadedUtc = DateTime.UtcNow;

        private static readonly ConcurrentQueue<Request> Inbox = new ConcurrentQueue<Request>();
        private static readonly List<ClientConnection> Clients = new List<ClientConnection>();
        private static readonly List<InFlight> Waiting = new List<InFlight>();
        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static volatile bool _running;
        private static double _nextBindAttempt;
        private static bool _bindWarningLogged;

        public static bool Enabled { get; private set; }
        public static bool Listening => _running;
        public static int ClientCount { get { lock (Clients) return Clients.Count; } }

        static McpBridge()
        {
            Enabled = !Application.isBatchMode || Environment.GetEnvironmentVariable(BatchEnvVar) == "1";
            if (!Enabled) return;
            McpLogBuffer.Install();
            McpCompileMessages.Install();
            McpTestRuns.Install();
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting += Stop;
            EditorApplication.update += Update;
            TryStart();
        }

        [MenuItem("Tools/PtK MCP Bridge/Restart Listener")]
        private static void RestartFromMenu()
        {
            Stop();
            _bindWarningLogged = false;
            TryStart();
            LogStatus();
        }

        [MenuItem("Tools/PtK MCP Bridge/Log Status")]
        private static void LogStatus() =>
            Debug.Log($"[McpBridge] enabled={Enabled} listening={Listening} port={Port} clients={ClientCount} epoch={Epoch}");

        private static void OnBeforeAssemblyReload()
        {
            McpLogBuffer.Persist();
            Stop();
        }

        private static void TryStart()
        {
            if (_running) return;
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                _listener = listener;
                _running = true;
                _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "McpBridge.Accept" };
                _acceptThread.Start();
                _bindWarningLogged = false;
            }
            catch (SocketException ex)
            {
                // Usually another editor instance holds the port, or the old socket is still closing.
                _nextBindAttempt = EditorApplication.timeSinceStartup + 2.0;
                if (!_bindWarningLogged)
                {
                    _bindWarningLogged = true;
                    Debug.LogWarning($"[McpBridge] Cannot listen on 127.0.0.1:{Port} ({ex.SocketErrorCode}). Retrying every 2 s.");
                }
            }
        }

        public static void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch (Exception) { /* already closed */ }
            _listener = null;
            ClientConnection[] clients;
            lock (Clients)
            {
                clients = Clients.ToArray();
                Clients.Clear();
            }
            foreach (var client in clients) client.Close(flush: true);
            Waiting.Clear();
            while (Inbox.TryDequeue(out _)) { }
        }

        private static void AcceptLoop()
        {
            var listener = _listener;
            while (_running && listener != null)
            {
                TcpClient tcp;
                try
                {
                    tcp = listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    return; // listener stopped
                }
                var client = new ClientConnection(tcp);
                lock (Clients) Clients.Add(client);
                client.Start();
            }
        }

        private static void Update()
        {
            if (!_running && EditorApplication.timeSinceStartup >= _nextBindAttempt) TryStart();

            // Bound the work per frame so a burst of requests cannot freeze the editor.
            for (int i = 0; i < 32 && Inbox.TryDequeue(out var request); i++) Dispatch(request);

            for (int i = Waiting.Count - 1; i >= 0; i--)
            {
                var item = Waiting[i];
                object result;
                string error;
                bool done;
                try
                {
                    done = item.Operation.Poll(out result, out error);
                }
                catch (Exception ex)
                {
                    done = true;
                    result = null;
                    error = ex.Message;
                }
                if (!done) continue;
                Waiting.RemoveAt(i);
                if (error != null) item.Client.SendError(item.Id, error);
                else item.Client.SendResult(item.Id, result);
            }
        }

        private static void Dispatch(Request request)
        {
            if (request.ParseError != null)
            {
                request.Client.SendError(request.Id, request.ParseError);
                return;
            }
            try
            {
                object result = McpCommands.Execute(request.Command, request.Args);
                if (result is IPendingOperation pending)
                    Waiting.Add(new InFlight { Client = request.Client, Id = request.Id, Operation = pending });
                else
                    request.Client.SendResult(request.Id, result);
            }
            catch (Exception ex)
            {
                request.Client.SendError(request.Id, ex is McpCommandException ? ex.Message : ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void Remove(ClientConnection client)
        {
            lock (Clients) Clients.Remove(client);
        }

        private sealed class Request
        {
            public ClientConnection Client;
            public double Id;
            public string Command;
            public McpArgs Args;
            public string ParseError;
        }

        private sealed class InFlight
        {
            public ClientConnection Client;
            public double Id;
            public IPendingOperation Operation;
        }

        private sealed class ClientConnection
        {
            private readonly TcpClient _tcp;
            private readonly NetworkStream _stream;
            private readonly BlockingCollection<byte[]> _outbox = new BlockingCollection<byte[]>();
            private Thread _reader;
            private Thread _writer;
            private int _closed;

            public ClientConnection(TcpClient tcp)
            {
                _tcp = tcp;
                _tcp.NoDelay = true;
                _stream = tcp.GetStream();
            }

            public void Start()
            {
                _reader = new Thread(ReadLoop) { IsBackground = true, Name = "McpBridge.Read" };
                _writer = new Thread(WriteLoop) { IsBackground = true, Name = "McpBridge.Write" };
                _reader.Start();
                _writer.Start();
            }

            public void SendResult(double id, object result) =>
                Enqueue(new Dictionary<string, object> { { "id", id }, { "ok", true }, { "result", result } });

            public void SendError(double id, string error) =>
                Enqueue(new Dictionary<string, object> { { "id", id }, { "ok", false }, { "error", error } });

            private void Enqueue(Dictionary<string, object> message)
            {
                byte[] payload = Encoding.UTF8.GetBytes(McpJson.Write(message));
                if (payload.Length > MaxPayloadBytes)
                {
                    payload = Encoding.UTF8.GetBytes(McpJson.Write(new Dictionary<string, object>
                    {
                        { "id", message["id"] }, { "ok", false },
                        { "error", $"Response too large ({payload.Length} bytes); narrow the request." },
                    }));
                }
                var frame = new byte[4 + payload.Length];
                frame[0] = (byte)payload.Length;
                frame[1] = (byte)(payload.Length >> 8);
                frame[2] = (byte)(payload.Length >> 16);
                frame[3] = (byte)(payload.Length >> 24);
                Buffer.BlockCopy(payload, 0, frame, 4, payload.Length);
                try { _outbox.Add(frame); } catch (InvalidOperationException) { /* closing */ }
            }

            private void ReadLoop()
            {
                var header = new byte[4];
                try
                {
                    while (true)
                    {
                        if (!ReadExact(header, 4, allowEof: true)) break;
                        int length = header[0] | (header[1] << 8) | (header[2] << 16) | (header[3] << 24);
                        if (length < 1 || length > MaxPayloadBytes) break; // protocol violation
                        var payload = new byte[length];
                        ReadExact(payload, length, allowEof: false);
                        Inbox.Enqueue(ParseRequest(Encoding.UTF8.GetString(payload)));
                    }
                }
                catch (Exception)
                {
                    // Socket closed or reset.
                }
                Close(flush: false);
            }

            private Request ParseRequest(string json)
            {
                var request = new Request { Client = this };
                try
                {
                    if (!(McpJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("Request must be a JSON object.");
                    request.Id = root.TryGetValue("id", out object id) && id is double d ? d : 0;
                    request.Command = root.TryGetValue("cmd", out object cmd) ? cmd as string : null;
                    request.Args = new McpArgs(root.TryGetValue("args", out object a) ? a as Dictionary<string, object> : null);
                    if (string.IsNullOrEmpty(request.Command)) request.ParseError = "Request has no 'cmd'.";
                }
                catch (Exception ex)
                {
                    request.ParseError = "Malformed request: " + ex.Message;
                }
                return request;
            }

            private bool ReadExact(byte[] buffer, int count, bool allowEof)
            {
                int read = 0;
                while (read < count)
                {
                    int n = _stream.Read(buffer, read, count - read);
                    if (n == 0)
                    {
                        if (read == 0 && allowEof) return false;
                        throw new EndOfStreamException();
                    }
                    read += n;
                }
                return true;
            }

            private void WriteLoop()
            {
                try
                {
                    foreach (byte[] frame in _outbox.GetConsumingEnumerable())
                        _stream.Write(frame, 0, frame.Length);
                }
                catch (Exception)
                {
                    // Peer went away.
                }
            }

            public void Close(bool flush)
            {
                if (Interlocked.Exchange(ref _closed, 1) != 0) return;
                _outbox.CompleteAdding();
                // Before a domain reload, give queued replies (e.g. to "refresh") a moment to leave.
                if (flush && _writer != null && Thread.CurrentThread != _writer) _writer.Join(500);
                try { _tcp.Close(); } catch (Exception) { /* ignore */ }
                Remove(this);
            }
        }
    }

    /// <summary>A command that completes over several editor frames (polled on the main thread).</summary>
    internal interface IPendingOperation
    {
        /// <summary>Returns true when finished; set exactly one of result/error.</summary>
        bool Poll(out object result, out string error);
    }

    /// <summary>A failure whose message is shown to the caller verbatim.</summary>
    internal sealed class McpCommandException : Exception
    {
        public McpCommandException(string message) : base(message) { }
    }
}
