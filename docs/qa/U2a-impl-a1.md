# U2a implementation report — attempt 1

Implemented `Server.Core/**` (netstandard2.1/UPM), `Server/**`, `Server.Tests/Core/**` (35 cases), and solution registrations: transports, lobby, movement/collision, masking/threat, bots, ping, reconnect. U2b features have TODO seams.

Scope exception: `Server.Tests/Server.Tests.csproj` needs one ProjectReference for Core tests. Shared unchanged; no commits.

Verification:
- `dotnet build PingTheKiller.sln -warnaserror`: PASS, zero warnings/errors.
- `dotnet test PingTheKiller.sln`: PASS, 360 passed, zero failed/skipped.
- `dotnet run --project Server -- --port 7777 --bots 7 --autostart`: 10+ seconds, match-start log, clean Ctrl+C exit.
- Unity-bundled Roslyn, C# 9/netstandard2.1: PASS for Shared/Core. `git diff --check`: PASS.

Sandbox builds used process-local SDK/AppData overrides and cached packages through `artifacts/NuGet.Config`.

Public API: `GameServer(ServerOptions)`, `Start/Stop`, `AddConnection`, deterministic `Tick(float)`, `Map`, `Log`. Subscribe `listener.ConnectionAccepted += server.AddConnection` before starting. Transports: `Start/Stop/Dispose`; TCP: bound `Port`; memory: client `Connect()`. Connections: `Send(PacketCodec.Encode(packet))`, `TryReceive`, `PacketAvailable/Closed`, `RemoteId`, `IsClosed`, `Close/Dispose`. Offline `ServerOptions.CreateTransport()` bypasses TCP, verified using a throwing online factory and socket-free fields.

Unverified: Unity Editor import, Linux/ARM64 execution. Limits: direct-steering bots can stall at walls; skill IDs are role-local GDD indices.
