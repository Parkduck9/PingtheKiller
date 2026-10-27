using System.Net;
using System.Runtime.InteropServices;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;

int port = 7777;
int bots = 0;
bool autostart = false;
bool sandbox = false;
IPAddress bind = IPAddress.Any;
for (int i = 0; i < args.Length; ++i)
{
    string arg = args[i];
    if (arg == "--autostart") autostart = true;
    else if (arg == "--sandbox") sandbox = true;
    else if (arg == "--bind")
    {
        if (++i >= args.Length || !IPAddress.TryParse(args[i], out var address))
        {
            Console.Error.WriteLine($"Invalid --bind address '{(i < args.Length ? args[i] : "")}': expected an IPv4 or IPv6 address such as 127.0.0.1.");
            return Usage();
        }
        bind = address;
    }
    else if ((arg == "--port" || arg == "--bots") && ++i < args.Length && int.TryParse(args[i], out int value))
    {
        if (arg == "--port" && value >= 0 && value <= 65535) port = value;
        else if (arg == "--bots" && value >= 0 && value <= 8) bots = value;
        else return Usage();
    }
    else return Usage();
}
if (autostart && bots < 2) return Usage();

using var shutdown = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Set(); };
// SIGTERM (systemd / docker stop) takes the same graceful path as Ctrl+C. Cross-platform API, no Windows-only calls.
PosixSignalRegistration? sigterm = null;
try { sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => { context.Cancel = true; shutdown.Set(); }); }
catch (PlatformNotSupportedException) { Console.WriteLine("SIGTERM handler not supported on this platform"); }

using var game = new GameServer(new ServerOptions { BotCount = bots, AutoStart = autostart, Sandbox = sandbox });
using var transport = new TcpTransport(port, bind);
game.Log += Console.WriteLine;
transport.Log += Console.WriteLine;
transport.ConnectionAccepted += game.AddConnection;
int exitCode = 0;
try
{
    transport.Start();
    game.Start();
    Console.WriteLine($"listening address={transport.Address} port={transport.Port} bots={bots}{(sandbox ? " sandbox=U2a-rules-only" : "")}");
    shutdown.Wait();
    Console.WriteLine("shutting down");
}
catch (System.Net.Sockets.SocketException ex)
{
    Console.Error.WriteLine($"Server socket error: {ex.Message}");
    exitCode = 1;
}
finally
{
    try { transport.Stop(); }
    catch (Exception ex) { Console.Error.WriteLine($"transport stop error {ex.GetType().Name}: {ex.Message}"); }
    try { game.Stop(); }
    catch (Exception ex) { Console.Error.WriteLine($"game stop error {ex.GetType().Name}: {ex.Message}"); }
    sigterm?.Dispose();
}
return exitCode;

static int Usage()
{
    Console.Error.WriteLine("Usage: Server [--port 0..65535] [--bots 0..8] [--bind <ip>, default 0.0.0.0] [--autostart (requires >=2 bots)] [--sandbox (network QA: U2a rules only, no match end before 600 s)]");
    return 2;
}
