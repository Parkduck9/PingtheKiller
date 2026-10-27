# U2a implementation report, attempt 2

Implementer switched Codex->Claude Opus (Codex workspace out of credits); not counted as an attempt.

Tests: `Server.Tests/Core/AttemptTwoFixTests.cs` (new), plus Core tests updated for the hold rule and new spawns.

1. Held direction applies only while the last non-zero input is within `SignalHoldSeconds`, evaluated at the broadcast `_now` (all players, bots included). Tests: `SingleInputMovesOnly…`, `TwentyHertzClient…`, `ThirtyHertzClientContract…`, `RandomInputPatternsNeverMove…`. **Clients must resend `MoveInput` at >= 10 Hz while moving.** Unity contract: fixed 30 Hz while a key is held plus an immediate zero input on release, never above 30/s (server cap stays 60/s).
2. `Run` catches each `Tick` fault, logs once per distinct message per second; `Log` subscribers are isolated; a throwing `IConnection.Send` closes only that connection. Tests: `ThreadedLoopLogsTickFaults…`, `ConnectionWhoseSendThrows…`.
3. `TcpTransport.Log`; accept errors logged, 100 ms back-off; subscriber/connection faults close that client only; `Stop()` never rethrows. Test: `ThrowingAcceptSubscriber…`.
4. Serial-number Seq. Test: `SeqWrapAround…`.
5. Keep-alive, token takeover, 10 s join timeout (`GameServer.JoinTimeoutSeconds`). Tests: `AcceptedTcpConnectionEnablesKeepAlive`, `ValidTokenFromNewConnectionTakesOverBoundSlot`, `ConnectionWithoutJoinIsClosed…`, `RejectedJoinDoesNotCount…`.
6. `public static GameServer.SanitizeNickname`. Tests: `NicknameStripsFormatCharacters`, `NicknameIsCapped…`.
7. SIGTERM via `PosixSignalRegistration`; stop errors logged. Not tested.
8. Killer spawn 0 = (5,5); survivors >= 21 m away. Tests: `KillerSpawnIsAtLeast…`, `EveryKillerIndexStarts…`.

Counts: 435 total, 429 passed, 6 failed (all QA tests written for attempt 1): `Seq_UintWraparound…` (expected), and `InputSanitizing…` x3, `RateCap…`, `Seq_OutOfOrder…`. These expect one input to keep a player moving for 0.15–0.95 s, which fix 1 forbids.

Public API unchanged except `TcpTransport.Log`, `GameServer.SanitizeNickname`, `GameServer.JoinTimeoutSeconds`.
