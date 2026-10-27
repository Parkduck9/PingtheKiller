# U3 implementation — attempt 1

Implemented `Tools/Harness/**` and `Tools/Harness.Tests/**`: TCP H1–H5, finite network deadlines, CLI/scorer/JSON, and test-only fake servers. H6 reports `skipped (needs U2b)`, zero points, false.

Run `dotnet run --project Tools/Harness -- --host 127.0.0.1 --port 7777 --report docs/qa/harness-latest.json`; select checks with `--only H1,H3`.

- H1: any server instance; `--port 7777 --bots 7 --autostart` is permitted.
- H2/H3/H4/H5: share one fresh `--port 7777 --bots 0` lobby, **without `--autostart`**. Harness becomes first-human host, joins its survivor, sends SetRole(Killer), SetBots(6), StartMatch. H4 throwaway movers and H5 rejoin the reserved survivor slot using its token. Independent invocations need a fresh lobby. H2 defaults to 60 seconds; `--duration` shortens it.
- H6: no server needed.

Verification: `dotnet build Tools/Harness/Harness.csproj --no-restore -warnaserror -p:UseSharedCompilation=false` passed without warnings; `dotnet test Tools/Harness.Tests/Harness.Tests.csproj --no-restore -p:UseSharedCompilation=false` passed **28/28**. CLI skipped/unreachable checks wrote JSON and exited 1. Sandbox profile/restore workarounds and per-check details: [README](../../Tools/Harness/README.md). Generated artifacts are ignored.

Real-server H1–H5 remain unverified until U2a merges. H2 infers tick period from TCP timestamps; coordinate-byte coincidences conservatively fail H3. Shared/Server/Client/solution unchanged. No commits.
