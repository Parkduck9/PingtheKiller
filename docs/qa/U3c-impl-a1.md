# U3c implementation report — attempt 1

Implementer: Claude Opus subagent (quota fallback: Codex out of credits; not a failed attempt). Not committed.

**Files:** `Tools/Harness/` WireClient, KillerView (new), MatchChecks (H3), Measurements, KillerLeakScan, ScenarioCheck, HarnessRunner, Options, MatchSession, README. `Tools/Harness.Tests/` LobbyServer, ScenarioServer, PingServer, LobbyFlowTests, MeasurementTests, ScenarioTests, new AnonymousViewTests and KillerViewTests.

**Verification:** `dotnet build Tools/Harness/Harness.csproj -warnaserror` and `dotnet build PingTheKiller.sln -warnaserror`: 0 warnings. `dotnet test PingTheKiller.sln`: Server.Tests 662/662 and Harness.Tests 323/323 (284 before, 39 added); identical in a second full run.

**QA tests:** none fail. None asserted the old `survivorId=… omitted` text.

**Judgement calls:**
- H3 attribution = nearest survivor at the same Tick within 0.05 m. Ambiguous only when the tied nearest survivors are bit-identical; equidistant distinct positions still count against the stopped survivor. Unexplained entries fail in both phases.
- The killer's own entry = real Id **and** `Role = Killer`; any other entry is attributed.
- `Measurements.Masked` dropped its Id check (raw scan unchanged); `MeasurementTests` updated with an Id-0 case added.
- Known Id = `Enum.IsDefined(PacketId)`. Unknown frames are kept raw (≤32768 per connection) for H3 and H6 byte scans. Skip counts go into evidence only when nonzero, from a per-run tally on `Options`.
- `S2C_SelfState` seam: looked up by name through reflection; its float fields are treated as legitimate.

**Unverified until U2c merges:** the real anonymous server; whether its killer-view positions match the survivor-view positions bit for bit at the same tick (README U3c-3); SelfState decoding through the seam; the conservative raw scan of 0x10 frames (U3c-2).
