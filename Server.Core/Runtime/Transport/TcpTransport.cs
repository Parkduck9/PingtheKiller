using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PingTheKiller.ServerCore.Transport
{
    public sealed class TcpTransport : ITransportListener
    {
        private readonly TcpListener _listener;
        private readonly ConcurrentDictionary<string, TcpConnection> _connections = new ConcurrentDictionary<string, TcpConnection>();
        private Task _acceptLoop = Task.CompletedTask;
        private int _running;
        private CancellationTokenSource _stop = new CancellationTokenSource();
        public TcpTransport(int port = 7777, IPAddress address = null)
        {
            _listener = new TcpListener(address ?? IPAddress.Any, port);
        }
        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
        public event Action<IConnection> ConnectionAccepted;
        public event Action<string> Log;
        public void Start()
        {
            if (Interlocked.Exchange(ref _running, 1) != 0) return;
            if (_stop.IsCancellationRequested)
            {
                _stop.Dispose();
                _stop = new CancellationTokenSource();
            }
            try { _listener.Start(); }
            catch { Volatile.Write(ref _running, 0); throw; }
            _acceptLoop = AcceptLoop();
        }
        private async Task AcceptLoop()
        {
            while (Volatile.Read(ref _running) != 0)
            {
                TcpClient client = null;
                TcpConnection connection = null;
                try
                {
                    client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    connection = new TcpConnection(client);
                    _connections[connection.RemoteId] = connection;
                    var accepted = connection;
                    connection.Closed += () => _connections.TryRemove(accepted.RemoteId, out _);
                    if (Volatile.Read(ref _running) == 0) { CloseClient(connection, client); break; }
                    ConnectionAccepted?.Invoke(connection);
                    connection.Start();
                }
                catch (Exception ex)
                {
                    CloseClient(connection, client);
                    if (Volatile.Read(ref _running) == 0) break;
                    WriteLog("accept error " + ex.GetType().Name + ": " + ex.Message);
                    try { await Task.Delay(100, _stop.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        private void CloseClient(TcpConnection connection, TcpClient client)
        {
            try { connection?.Close(); }
            catch (Exception ex) { WriteLog("close error " + ex.GetType().Name + ": " + ex.Message); }
            finally
            {
                if (connection != null) _connections.TryRemove(connection.RemoteId, out _);
                try { client?.Close(); }
                catch (Exception ex) { WriteLog("close error " + ex.GetType().Name + ": " + ex.Message); }
            }
        }
        private void WriteLog(string message)
        {
            var subscribers = Log;
            if (subscribers == null) return;
            foreach (Action<string> subscriber in subscribers.GetInvocationList())
                try { subscriber(message); } catch (Exception) { /* Logging cannot stop accepting. */ }
        }
        public void Stop()
        {
            if (Interlocked.Exchange(ref _running, 0) == 0) return;
            _stop.Cancel();
            try { _listener.Stop(); }
            catch (Exception ex) { WriteLog("stop error " + ex.GetType().Name + ": " + ex.Message); }
            try { _acceptLoop.GetAwaiter().GetResult(); }
            catch (Exception ex) { WriteLog("accept loop error " + ex.GetType().Name + ": " + ex.Message); }
            foreach (var pair in _connections) CloseClient(pair.Value, null);
        }
        public void Dispose() => Stop();
    }

    public sealed class TcpConnection : FramedConnection
    {
        public const int MaxQueuedBytes = 256 * 1024;
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly ConcurrentQueue<byte[]> _sendQueue = new ConcurrentQueue<byte[]>();
        private readonly SemaphoreSlim _sendReady = new SemaphoreSlim(0);
        private int _queuedBytes;
        private int _started;
        public int QueuedBytes => Volatile.Read(ref _queuedBytes);
        public Task Completion { get; private set; } = Task.CompletedTask;
        public TcpConnection(TcpClient client) : base("tcp:" + Guid.NewGuid().ToString("N"))
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _client.NoDelay = true;
            _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _stream = client.GetStream();
        }
        public void Start()
        {
            if (Interlocked.Exchange(ref _started, 1) == 0)
                Completion = Task.WhenAll(ReceiveLoop(), SendLoop());
        }
        public override void Send(byte[] frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (IsClosed) return;
            if (Interlocked.Add(ref _queuedBytes, frame.Length) > MaxQueuedBytes)
            {
                Interlocked.Add(ref _queuedBytes, -frame.Length);
                Close();
                return;
            }
            _sendQueue.Enqueue(frame);
            _sendReady.Release();
        }
        private async Task ReceiveLoop()
        {
            var bytes = new byte[8192];
            try
            {
                while (!IsClosed)
                {
                    int count = await _stream.ReadAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                    if (count == 0) break;
                    ReceiveBytes(bytes.AsSpan(0, count));
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { Close(); }
        }
        private async Task SendLoop()
        {
            try
            {
                while (!IsClosed)
                {
                    await _sendReady.WaitAsync().ConfigureAwait(false);
                    if (IsClosed) break;
                    while (!IsClosed && _sendQueue.TryDequeue(out var frame))
                    {
                        await _stream.WriteAsync(frame, 0, frame.Length).ConfigureAwait(false);
                        Interlocked.Add(ref _queuedBytes, -frame.Length);
                    }
                }
            }
            catch (IOException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
            finally { Close(); }
        }
        protected override void OnClose()
        {
            _client.Close();
            _sendReady.Release();
        }
    }
}
