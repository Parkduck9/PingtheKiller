using System.Runtime.InteropServices;
using PingTheKiller.ServerCore;
using PingTheKiller.ServerCore.Transport;

int port = 7777;
int bots = 0;
bool autostart = false;
for (int i = 0; i < args.Length; ++i)
{
    string arg = args[i];
    if (arg == "--autostart") autostart = true;
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

using var game = new GameServer(new ServerOptions { BotCount = bots, AutoStart = autostart });
using var transport = new TcpTransport(port);
game.Log += Console.WriteLine;
transport.Log += Console.WriteLine;
transport.ConnectionAccepted += game.AddConnection;
int exitCode = 0;
try
{
    transport.Start();
    game.Start();
    Console.WriteLine($"listening port={transport.Port} bots={bots}");
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
    Console.Error.WriteLine("Usage: Server [--port 0..65535] [--bots 0..8] [--autostart (requires >=2 bots)]");
    return 2;
}
