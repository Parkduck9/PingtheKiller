# 핑 더 킬러 (Ping the Killer) — 개발 계획서

- 작성: 2026-10-27 (Claude Opus 5.5) · 원안: `ping_the_killer.md`, Notion 「Server로 직접 만들기」
- 상태: **v0.1 초안** — 🔶 표시는 기본값으로 정한 항목(사용자 확인 시 확정)

---

## 0. 원안 대비 변경점

| 항목 | 원안 | 확정 |
|---|---|---|
| 서버 위치 | 무료 클라우드 인스턴스 | **이 PC에서 .NET 서버 실행** (LAN + 공유기 포트포워딩) — 클라우드 단계 삭제. 2026-10-27 무료 호스팅 조사 결과 이후 이전 후보는 **Oracle Cloud Always Free 서울**(영구 무료, 유휴 회수 정책 주의). 서버는 Linux에서도 돌도록 Windows 전용 API 금지 |
| 출시 | 스토어(미정) | **Microsoft Store (Windows)** |
| 엔진 | Unity6 | **Unity 6000.3.25f1 LTS** |
| 시점 | 미정 | **살인마 = 3D 탑다운, 생존자 = 1인칭** |
| 그래픽 | — | 기본 도형(프리미티브)으로 윤곽, 에셋은 사용자가 추후 제공 |
| 이동 입력 | `DirX, DirY, Seq` | `DirX, DirZ, Yaw, Seq` — 1인칭은 카메라 기준 방향을 **클라가 월드 방향으로 변환해 전송**, 서버가 정규화·클램프 |
| 노출 마스킹 | 노출 플래그 `false` | **숨은 생존자는 살인마용 패킷에서 항목 자체를 제외** (좌표 미전송 → 패킷 스니핑 월핵 차단) |
| 패킷 길이 정의 | "Payload Length" / "전체 길이" 혼재 | `Length = PacketId(2) + Payload` 바이트 수, 상한 16 KB |

---

## 1. 게임 규칙 (서버 판정 기준)

**규칙의 단일 기준은 [`docs/GDD.md`](GDD.md)** 이다. 여기에는 구현에 영향을 주는 요약만 둔다.

- 승리 ✅: 생존자 1명이라도 탈출 → 생존자 승 / 생존자 전원 무력화 → 살인마 승
- 임무 ✅: 릴레이 단말 해킹(필요 2~4개) 완료 시 탈출구 개방, 해킹 중 Signal
- 구출 ✅: 모든 생존자가 3 s 접촉으로 구출 가능
- 생존자는 살인마 위치를 **절대 받지 않음** ✅ → 서버가 **위협 단계 0~3**(거리)만 전송, 클라는 비프음·잡음으로 연출
- 오프라인 봇 모드 ✅: 봇 추가/삭제 1v1~1v7, 서버 로직을 Unity 안에서 실행

---

## 2. 기술 아키텍처

```
[Unity Client ×N] ──TCP 7777──▶ [.NET 10 Server (이 PC)]
      │                              ├─ Accept 스레드
      │ Shared (UPM local pkg)       ├─ Session별 async Receive → PacketAssembler
      └──────── 같은 소스 ───────────┤─ ConcurrentQueue<Input>
                                     ├─ GameLoop 스레드 (20 Hz, 상태 단독 소유)
                                     ├─ Bot AI (서버 내부, 소켓 없이 Input 큐에 직접 투입)
                                     └─ Session별 Send 큐
```

- **서버 로직 분리 (오프라인 모드용):** 게임 규칙·봇·세션 관리는 `Server.Core`(netstandard2.1, C# 9)에 두고, `Server`(.NET 10 콘솔)는 실행 껍데기만 담당. Unity는 오프라인 모드에서 `Server.Core`를 **같은 프로세스 안에서 루프백으로 실행** → 외부 서버 없이 봇전 가능, Store 심사 리스크 해소.
- **Shared 단일화:** `Shared/`를 로컬 UPM 패키지(`package.json` + asmdef)로 만들고 Unity는 `Packages/manifest.json`에 `"file:../../Shared"`로 참조, 서버는 같은 `.cs`를 `Shared.csproj`(netstandard2.1, C# 9)로 컴파일. → 서버만 고쳐도 양쪽 반영.
- **봇:** 서버 내부 봇은 큐 직접 투입(가볍고 결정적). 별도로 **Harness가 소켓 클라이언트로 봇 7개를 띄워** 실제 네트워크 경로를 검증.
- **클라이언트:** 네트워크 스레드 → 메인스레드 큐. 원격 엔티티는 100 ms 버퍼 보간. 내 캐릭터는 입력 예측 없이 서버값 보간 🔶(지연 둔감 설계라 1차는 단순하게).
- **재접속:** `JoinResult`에 `ReconnectToken(uint64)` 발급. 끊김 후 10 s 내 같은 토큰으로 `Join` → 같은 PlayerId/상태 복귀. 그 사이 캐릭터는 정지(=생존자는 Silent).
- **치트 방지:** 방향 정규화·NaN 제거·`Seq` 역순 폐기·초당 입력 60개 초과 시 무시·비정상 Length 즉시 끊기.

### 프로토콜 표 (v0.1)

| ID | 이름 | 방향 | Payload |
|---|---|---|---|
| 0x0001 | C2S_Join | C→S | `string Nickname, ulong ReconnectToken(0=신규)` |
| 0x0002 | S2C_JoinResult | S→C | `int PlayerId, ulong ReconnectToken, byte Result` |
| 0x0003 | C2S_MoveInput | C→S | `float DirX, float DirZ, float Yaw, uint Seq` |
| 0x0004 | C2S_UseSkill | C→S | `byte SkillId` |
| 0x0005 | S2C_WorldState | S→C | `uint Tick, float TimeLeft, byte ExitLocked, List<PlayerState>` (수신자별 마스킹) |
| 0x0006 | S2C_SkillEffect | S→C | `int CasterId, byte SkillId, float X, float Z, float Duration` |
| 0x0007 | S2C_GameResult | S→C | `byte WinnerRole, List<int> EscapedIds` |
| 0x0008 | C2S_Ping | C→S | `long Timestamp` |
| 0x0009 | S2C_Pong | S→C | `long Timestamp` |
| 0x000A | S2C_LobbyState | S→C | `List<Slot>(8) {PlayerId, Nickname, IsBot, IsHost}` |
| 0x000B | C2S_StartMatch | C→S | (없음) — 호스트만 |
| 0x000C | S2C_MatchStart | S→C | `byte Role, byte SkillId, int Seed` |

`PlayerState = {int Id, byte Role, byte Status(Normal/Frozen/Escaped), float X, float Z, float Yaw, byte Flags(Signal, Afterimage, Stealthed)}`

**v0.2 변경 (GDD 반영, U1 이후 스펙 변경 브리프로 적용):**
- `S2C_WorldState`에 `byte ThreatLevel`(수신자별, 생존자만 의미) 추가, 생존자용 패킷에서 **살인마 항목 제외**
- `PlayerStatus` = `Normal / Lagging / Escaped / Eliminated` (Frozen 폐기 → 패킷 손실), `PlayerState`에 `float StatusTimer`(타임아웃·구출 진행도), `byte CaptureCount`
- 제한 시간 600 s, Lagging 이속 25%·항상 Signal, 2번째 체포 즉시 처형
- `S2C_WorldState`에 `List<Terminal>{byte Id, float X, float Z, byte Progress(0~100)}`, `byte TerminalsRequired`, `byte ExitOpen`
- `C2S_Interact {byte Active}` (해킹·구출 유지 시작/종료)
- `C2S_SetBots {byte Count}`, `C2S_SetRole {byte Role}` (호스트 전용, 로비)

**v0.5 변경 (U6g, 서버 RTT 측정):**
- `0x0014 S2C_RttProbe {uint Nonce}`, `0x0015 C2S_RttEcho {uint Nonce}` 추가. 온라인 인간에게 로비·매치에서 1 s마다 전송하며 클라이언트 수신 스레드에서 즉시 에코한다(동시 대기 1개, 1 s 시간 초과).
- 서버 시계로 왕복 시간을 측정하고 0~0.25 s 클램프·1/8 평활화 후 싱크 판정에서 전체 RTT를 뺀다. 기존 Ping/Pong은 클라이언트 표시용이며 판정에 사용하지 않는다. 봇·오프라인은 프로브 없이 RTT 0을 유지한다.

---

## 3. 일정 (Notion 일정 기준, 서버 클라우드 단계 제거)

| 날짜 | 목표 | 완료 조건 |
|---|---|---|
| **D1 10/27 (오늘)** | 기획·프로토콜 확정, Shared + 서버 + 프레이밍, Unity 접속, 봇 7개 이동이 Unity 화면에 보임 | 하네스 H1·H2 통과, Unity에서 봇 8개 시각 확인 |
| **D2 10/28** | 게임 규칙 전부(노출·체포·구출·탈출·스킬 9종), 끊김/재접속, 보간, 치트 방지 | 하네스 전체 통과(§5 기준점) |
| **D3 10/29** | UI/UX(슬롭 배제, `docs/ui/HUD_SPEC.md` 기준), **에셋 적용**(캐릭터 모델·셰이더, 프리미티브 교체), 1인칭/탑다운 카메라 완성, 로비(Host/Join/IP), 포트포워딩 테스트, 개인정보 처리방침 | UI 리뷰 ≥ 85점, 에셋 적용 후 스크린샷 리뷰 통과, 외부망 접속 1회 성공 |

**에셋 적용 (10/27 결정):** D3 UI 작업과 한 단위로 진행한다. 에셋(Robot Kyle, Toon Shaders Pro 등 Unity 에셋 스토어 구매분)은 **사용자가 직접** Unity 에디터 Package Manager → My Assets로 가져온다(계정 필요, AI가 내려받지 않음). 선행 조건: U2c·U4c(살인마 화면 위치 기반 추적) 병합 후 — ✅ 10/29 충족. 범위: 생존자·살인마 모델 교체, URP 호환 셰이더 적용, 슬롭 팔레트·조명과 색 맞춤, 1280×720/1920×1080 스크린샷 리뷰. 에셋 원본을 레포에 커밋할지는 레포 공개 여부를 보고 사용자가 결정.
| **D4 10/30** | MSIX 패키징, Microsoft Store 제출 | Partner Center 제출 완료 |

### D1 작업 단위 (루프 단위)

| # | 단위 | 담당 | QA |
|---|---|---|---|
| U1 | 솔루션 골격 + Shared(패킷·직렬화·PacketAssembler) + xUnit | Codex Sol | Flash: 분할/병합 퍼즈 테스트 |
| U2 | 서버 Listener/Session/GameLoop + 서버 봇 7 | Codex Sol | Flash: H2 부하 측정 |
| U3 | Tools/Harness (H1~H6 스코어러) | Codex Sol | Flash가 **하네스 자체**를 리뷰(채점 버그 탐지) |
| U4 | Unity 프로젝트 생성, Shared 참조, 접속·WorldState 렌더(프리미티브) | Codex Sol | Flash: EditMode 테스트 + 스크린샷 |

---

## 4. AI 역할 분담 · 루프 엔지니어링

### 모델 배치 (조사 결과 기반, 2026-10-27)

| 작업 | 1순위 | 실패 2회 후 상향 | 근거 |
|---|---|---|---|
| 서버·프레이밍·스레드 | Codex **GPT-6.1 Sol** (high) | GPT-6 Astra | Sol = OpenAI 공식 "복잡한 작업용 주력", Astra = 최상위 |
| Unity 클라 게임플레이 | Codex **GPT-6.1 Sol** | Claude Opus 서브에이전트 | |
| 단순 수정·보일러플레이트 | Codex GPT-6 Luna | Sol | 저비용 |
| QA 테스트·퍼즈·하네스 실행 | Antigravity **Gemini 3.8 Flash** (medium) | Sonnet 5.5 (agy 경유) | 구현과 다른 모델 계열 → 같은 맹점 회피, 토큰 절약 |
| UI 시각 리뷰(스크린샷) | Antigravity **Gemini 3.8 Flash (high)** | Codex **GPT-6.1 Terra** | Terra는 공식적으로 시각 이해·글쓰기 특화, **코딩용은 아님** |
| 단순 조회 | Claude Haiku | Gemini 3.8 Flash (low) | Flash-Lite는 agy 미제공 |
| 감독·최종 판정 | **Claude Opus 5.5** | Fable 5.1 (설계 막힘 시) | |

> **QA 모델 결정 (2026-10-27):** 토큰 비용 때문에 QA는 Argon 대신 **Gemini 3.8 Flash**로 진행. Flash가 할당량 문제로 실패하면 1회 재시도 후 상향 열(Sonnet 5.5)로 전환하며, 이 전환은 3회 시도 횟수에 포함하지 않음.

### 루프 규칙

```
Opus 브리프(영어) → Codex 구현 → Antigravity QA(테스트·하네스·리포트) → Opus 판정
   └ 실패 시: 1~2회차 = 같은 방식 + 구체적 수정 목록
             3회차   = 다른 방식(설계 변경 또는 모델 상향)을 브리프에 명시
             3회 실패 = 중단, 사용자에게 한국어로 보고(증거·선택지)
```
- Opus는 브리프에 없는 문제(보안·스레드 경합·슬롭·규칙 위반)도 판정에 포함해 수정 지시.
- 커밋은 Opus PASS 이후에만.
- **토큰 소진 전환 (10/27 결정):** Codex 소진 → Claude가 구현 / Claude 소진 → Codex가 구현 / QA는 항상 구현자와 다른 모델. 전환은 시도 횟수에 포함하지 않음.
- AI끼리 대화는 영어, 사용자 보고는 한국어.

---

## 5. 하네스 (자동 채점) — 100점

| # | 항목 | 점수 | 게이트 |
|---|---|---:|:---:|
| H1 | 10,000개 메시지를 무작위 분할/병합 스트림으로 전송 → 유실·파싱 오류 0건 | 25 | ✔ |
| H2 | 플레이어 2 + 봇 6 접속, 60 s 동안 모든 클라 WorldState 수신 간격 p99 ≤ 120 ms, Tick 단조 증가·간격 ≤ 2 (서버 내부 틱 시간 p99 ≤ 60 ms는 TCP로 측정 불가 → 서버 계측 추가 전까지 미채점) | 20 | |
| H3 | 정지한 생존자의 좌표가 살인마 수신 패킷에 **바이트 단위로 0회 등장** | 20 | ✔ |
| H4 | 비정상 입력(초과 길이·음수 길이·미지 ID·NaN·속도 10배 방향·초당 500회 입력) → 서버 크래시 0, 최대속도 초과 이동 0 | 15 | ✔ |
| H5 | 접속 강제 종료 후 5 s 내 재접속 → 같은 PlayerId·위치·상태 복원 | 10 | |
| H6 | 시나리오: 단말 해킹 → 탈출구 개방 → 체포 → 구출 → 탈출 → GameResult 일치, 생존자 패킷에 살인마 좌표 0회 | 10 | |

**통과 기준점 (확정, 2026-10-27):**
- **U2b 이전 (H6 미구현):** 총점 **90/100** = H1~H5 전 항목 통과 + 게이트(H1·H3·H4) 만점. 한 항목이라도 실패하면 불합격. 하네스 기본값 `--threshold 90`.
- **U2b 이후 (H6 활성):** 총점 **100/100**. 모든 항목이 전부 아니면 0점인 이진 판정이고, 실제 서버가 H1~H5를 3회 연속 흔들림 없이 통과했으므로 여유 점수를 두지 않는다. U2b부터 `--threshold 100`으로 실행. U2e(11/02)부터 `--expect-los`도 함께 붙인다.
- **서버 모드 (U2b부터):** H1~H5는 네트워크 계층 점검이라 `--sandbox` 서버(`--port <p> --bots 0 --bind 127.0.0.1 --sandbox`, U2a 규칙만: 해킹·체포·스킬·승패 없음)로, H6는 일반 서버로 채점한다. 일반 서버에서는 봇이 20초 안에 탈출해 매치가 끝나므로 H2~H5가 성립하지 않는다.
- **안정성:** 판정은 새 서버 인스턴스마다 1회씩, **3회 연속 통과**해야 PASS. 1회라도 실패하면 원인 분석 후 재시도(재시도 정책의 1회로 계산).
- **보조 지표 (점수 외 감시):** H2 수신 p99 ≤ 120 ms(측정 63.5 ms), H5 재접속 ≤ 5 s(측정 48 ms). 측정값이 기준의 75%를 넘으면 리스크로 보고.
- **하네스 자체 검증:** 실제 서버 대신 의도적 결함 가짜 서버(마스킹 누출·Pong 누락/중복·속도 10배·재접속 상태 변경 등)로 각 항목이 감점되는지 `Tools/Harness.Tests` 150개 테스트로 확인. 이 중 타이밍 민감 테스트 1개가 `dotnet test PingTheKiller.sln`(Server.Tests와 동시 실행) 중 1회 실패, 단독 재실행 2회는 150/150 통과 → 흔들리는 테스트로 기록. **U3b에서 해결**(`QaH1Tests.BrokenServerFailsH1`, 검사 강도 유지), U3b 이후 하네스 자체 테스트 284개.
- 첫 실행 결과 (2026-10-27, main `01787eb`, 이 PC 루프백, `dotnet Server.dll --port 777x --bots 0` 새 인스턴스 × 3):

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 63.3 ms) | 20 | 15 | 10 (48.2 ms) | 0 (U2b 필요) | 90 | ✔ | 0 |
  | 2 | 25 | 20 (p99 63.5 ms) | 20 | 15 | 10 (48.0 ms) | 0 | 90 | ✔ | 0 |
  | 3 | 25 | 20 (p99 63.5 ms) | 20 | 15 | 10 (47.1 ms) | 0 | 90 | ✔ | 0 |

  리포트: `docs/qa/harness-run1.json` ~ `harness-run3.json`. 20 Hz(50 ms) 대비 수신 p99 63 ms는 Windows 타이머 해상도(약 15.6 ms)로 인한 지터로 판단.
- **H6 포함 첫 100점 실행 (2026-10-27, main `5f83456` = U2b 병합, U3b 최종 하네스, 이 PC 루프백):** 회차마다 새 `--sandbox` 서버(H1~H5)와 새 일반 서버(H6) 한 쌍을 띄우고, 하네스 1회 실행(`--port P --h6-port P+1 --threshold 100`)으로 함께 채점. 모든 서버는 회차 후 종료.

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 63.5 ms) | 20 | 15 | 10 (48.1 ms) | 10 | **100** | ✔ | 0 |
  | 2 | 25 | 20 (p99 63.4 ms) | 20 | 15 | 10 (47.2 ms) | 10 | **100** | ✔ | 0 |
  | 3 | 25 | 20 (p99 63.5 ms) | 20 | 15 | 10 (63.6 ms) | 10 | **100** | ✔ | 0 |

  H6 시나리오: 해킹 → 탈출구 개방 → 체포 → 접촉 3 s 유지(처형 없음) → 후퇴 → 구출 → 탈출 → GameResult 일치, 생존자 패킷 살인마 좌표 0회. 처형 유예가 있든 없든 통과하도록 설계. 상세: `docs/qa/U3b-verdict.md`. **U2b 이후 기준(100/100, 3회 연속) 충족.** U2c(살인마 화면 익명화) 병합 시 H3·H6를 위치 기반으로 바꾸는 U3c가 함께 들어가야 이 기준이 유지된다.
- **U2c·U3c·U4c/U4d 동시 병합 후 실행 (2026-10-29, main `fd52e11`, 이 PC 루프백):** 프로토콜 v0.3(`S2C_SelfState`), 익명 살인마 화면, 가짜 신호(phantom), 비공개 RNG가 들어간 서버를 U3c 하네스(틱+위치로 생존자 대조, 모르는 패킷 건너뜀)로 채점. 방식은 위와 같음(회차마다 새 `--sandbox` 서버 + 새 일반 서버, 회차 후 종료).

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 63.6 ms) | 20 | 15 | 10 (46.0 ms) | 10 | **100** | ✔ | 0 |
  | 2 | 25 | 20 (p99 63.1 ms) | 20 | 15 | 10 (48.0 ms) | 10 | **100** | ✔ | 0 |
  | 3 | 25 | 20 (p99 63.3 ms) | 20 | 15 | 10 (47.0 ms) | 10 | **100** | ✔ | 0 |

  리포트: `docs/qa/harness-u2c-run1.json` ~ `harness-u2c-run3.json`. **U2c 이후에도 기준(100/100, 3회 연속) 유지.** 병합 시 U3c 테스트가 "모르는 패킷"으로 쓰던 Id 0x10이 U2c의 `S2C_SelfState`가 되어 Harness.Tests 27건이 실패했고, 테스트용 Id를 0x11로 옮겨 해결(`fd52e11`). 하네스 본체는 SelfState를 자동으로 인식하도록 이미 준비되어 있었음. 게이트: 빌드 경고 0, Server.Tests 713/713, Harness.Tests 386/386.
- **U2e·U3d·U4e 병합 후 실행 (2026-11-02, main `3a9ab15`, 이 PC 루프백):** 생존자가 시야 안의 살인마를 보는 규칙(GDD §2, 10/30) 적용. **이후 기본 실행 명령에 `--expect-los`를 붙인다** (H6에서 시야 안 살인마 표시와 가려져 숨겨진 틱이 각각 1회 이상 나와야 통과). 방식은 위와 같음.

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 62.5 ms) | 20 | 15 | 10 (47.3 ms) | 10 | **100** | ✔ | 0 |
  | 2 | 25 | 20 (p99 62.6 ms) | 20 | 15 | 10 (46.6 ms) | 10 | **100** | ✔ | 0 |
  | 3 | 25 | 20 (p99 62.7 ms) | 20 | 15 | 10 (46.5 ms) | 10 | **100** | ✔ | 0 |

  시야 집계(정상 표시 / 가려져 숨김 / 경계 0.05 m 이내라 판정 안 함): H3 83~85 / 0 / 0, H6 2092~2094 / 152 / 5. 생존자 패킷의 살인마 좌표 초과 0회. 리포트: `docs/qa/harness-los-run1.json` ~ `harness-los-run3.json`. 게이트: 빌드 경고 0, Server.Tests 793/793, Harness.Tests 616/616. 클라이언트(U4e)는 Unity 1151/1151로 통과했으나 살인마 화면은 촬영용 가짜 데이터로 검증 — 실제 서버 데이터로 보이는지는 플레이 확인 필요.
- **Gemini Flash 재검증 + U2d 병합 후 실행 (2026-11-02, main `5b7e210`):** U2c~U4e를 Gemini Flash가 독립 재검증(제품 결함 0, 예전 오프라인 테스트 1건 수정, `docs/briefs/Reverify-flash.md`)한 뒤 U2d(봇 밸런스: 봇은 자기 역할이 볼 수 있는 정보만 사용, 1v7 생존자 승률 53%·중간 길이 401 s, 상세 `docs/qa/U2d-impl-a1.md`) 병합. 방식은 위와 같음(`--threshold 100 --expect-los`).

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 63.3 ms) | 20 | 15 | 10 (47.3 ms) | 10 | **100** | ✔ | 0 |
  | 2 | 25 | 20 (p99 63.4 ms) | 20 | 15 | 10 (47.1 ms) | 10 | **100** | ✔ | 0 |
  | 3 | 25 | 20 (p99 63.4 ms) | 20 | 15 | 10 (46.8 ms) | 10 | **100** | ✔ | 0 |

  H6 시야 집계: 정상 표시 2092~2098 / 가려져 숨김 152 / 판정 안 함 5. 리포트: `docs/qa/harness-u2d-run1.json` ~ `harness-u2d-run3.json`. 게이트: 빌드 경고 0, Server.Tests 836/836, Harness.Tests 635/635, Unity EditMode 1187/1187(컴파일 오류 0).
- **U6b(맵 v2 + 봇 길찾기) + U4g(캐릭터 에셋 + 근거리 표시 수정) 병합 후 실행 (2026-11-03, main `fbf1c99`):** 80×80 도시 블록 맵(`docs/GDD-v2.md` §5), Rio/Robot Kyle 캐릭터. Codex 크레딧 소진으로 후반은 Claude Sonnet이 이어받음, QA는 Gemini Flash. 방식은 위와 같음.

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 63.2 ms) | 20 | 15 | 10 (47.5 ms) | 10 | **100** | ✔ | 0 |
  | 2 | 25 | 20 (p99 63.4 ms) | 20 | 15 | 10 (62.7 ms) | 10 | **100** | ✔ | 0 |
  | 3 | 25 | 20 (p99 63.4 ms) | 20 | 15 | 10 (47.5 ms) | 10 | **100** | ✔ | 0 |

  H6 시야 집계: 정상 표시 2365~2369 / 가려져 숨김 868~870 (새 맵은 건물이 많아 가려지는 비율이 크게 늘었다). 리포트: `docs/qa/harness-u6b-u4g-run1.json` ~ `run3.json`. 게이트: 빌드 경고 0, Server.Tests 838/838(밸런스 스윕 제외), Harness.Tests 635/635, Unity EditMode 1220/1220(컴파일 오류 0). **알려진 미달:** 밸런스 스윕 1v3 생존자 승률 15~18%(목표 25~75%) — U6d(봇 AI v2)에서 v2 목표로 재조정.
- **U6a·U6e·U4h·U4i·U4j·U6c-ui·U7a 7개 동시 병합 후 실행 (2026-11-04, main 병합 직후 수정 커밋):** 규칙 v2 서버(프로토콜 v0.4, 쓰러진 생존자는 타이머로만 진행), 맵 v2.1, 맵 그래픽, 툰 셰이딩 + 밤 색감, Mixamo 전투·상태 모션, 규칙 v2 HUD 위젯(미연결), 설정 화면. 방식은 위와 같음(회차마다 새 `--sandbox` 서버 + 새 일반 서버, `--threshold 100 --expect-los`).

  | 실행 | H1 | H2 | H3 | H4 | H5 | H6 | 총점 | 게이트 | 종료 코드 |
  |---|---|---|---|---|---|---|---:|:---:|:---:|
  | 1 | 25 | 20 (p99 63.3 ms) | 20 | 15 | 10 (46.7 ms) | 10 | **100** | ✔ | 0 |
  | 2 | 25 | 20 (p99 63.3 ms) | 20 | 15 | 10 (46.9 ms) | 10 | **100** | ✔ | 0 |
  | 3 | 25 | 20 (p99 63.3 ms) | 20 | 15 | 10 (62.7 ms) | 10 | **100** | ✔ | 0 |

  H6 시야 집계: 정상 표시 2793~2794 / 가려져 숨김 4398~4399. H6은 규칙 v2 흐름(스위치 15 s, 붕괴 120 s, 쓰러진 A를 살인마가 3 s 접촉해도 Lagging 유지, 구조 후 둘 다 탈출)을 끝까지 통과. 리포트: `docs/qa/harness-merge1104-run1.json` ~ `run3.json`, 로그 `docs/qa/merge-1104/`. 게이트: 빌드 경고 0, Server.Tests 955/955(밸런스 스윕 제외), Harness.Tests 635/635, Unity EditMode 1320/1320, Windows 빌드 성공.

  병합 후 수정:
  - 클라이언트 `SyncResult` 중복 정의 제거(Shared 것 사용).
  - 주차 차량 바닥 메시가 방향별로 하나만 저장되어 크기가 다른 차의 바닥이 충돌 박스와 어긋나던 U4h 버그 수정(장애물마다 별도 메시). 맵 v2.1로 WorldArt 재생성.
  - 출구별 v0.4 코드에 맞춰 소스 검사 테스트 2개 갱신.
  - 맵 v2.1에서 건물 안이 된 테스트 대기 위치 이동.
  - 화이트 노이즈 단말기 시작점 2~3개로 조정(U2c AUC 0.855 < 0.86, 구분 KS p ≥ 0.01을 동시에 통과하는 유일한 범위).
  - U3d H6 테스트 변형 보강.
  - **생존자 봇 정지 버그 수정:** 안 보이는 살인마가 12 m 안에 가만히 있으면 봇이 끝까지 멈추던 문제. 20 s간 변화가 없으면 캠핑으로 보고 다른 단말기로 가거나, 남은 게 그것뿐이면 작업하되 살인마가 마지막으로 보인 자리 3.6 m 안 단말기는 피함. Opus 구현, Gemini Flash QA 1차 FAIL(주요 2) → 2차 PASS. 보고서 `docs/qa/merge-1104/botfreeze-*.md`.

  **알려진 미달 (U6d에서 복구):**
  - MatchLength 봇 시간 초과 기준을 6 → 7/15로 임시 완화. 봇 해킹 속도가 원인이며, U6d에서 ≤ 4로 복구.
  - U2c AUC 상한 0.86 유지. U6d에서 0.85로 복구.
  - 구분 KS p = 0.0166으로 여유가 적음.
  - 1v7 봇끼리 생존자 승률 0~3%, 시간 초과 28~30%.
  - 안 보이는 살인마가 위협 3단계 안에서 다가오는 경우는 20 s 뒤 무시됨(Flash QA Finding 3).

- **U6c-wire + U6c-hatch 병합 후 실행 (2026-11-04):** 클라이언트만 바뀐 단위라서 서버/하네스 점수 검사는 다시 돌리지 않았습니다(서버 코드 변경 없음).
  - **U6c-wire:** 규칙 v2 HUD를 v0.4 패킷에 연결했습니다. Opus가 구현했고, Gemini Flash QA는 1차 FAIL(blocker 1, major 4) → 2차 PASS였습니다.
    - 생존자: 싱크 체크 링(Space/패드 A, 체크당 1회), 스위치·해치 안내, 출구·붕괴 줄, Regressing 표시.
    - 살인마: 처형 안내 제거, 걷어차기·해치 닫기 막대, 붕괴 시계, 과부하 경고, 출구 마커.
    - 결과 화면 v2.
    - 두 번째 체포 안내 30 s 수정, 1080p 위협 노이즈가 잘린 글자처럼 보이던 문제(B1) 수정.
    - 개발 플래그 `-ptk-dev-hack`, `-ptk-dev-switch`, `-ptk-auto-sync` 추가.
  - **U6c-hatch:** 두 역할 모두에게 해치를 바닥에 그립니다. Sonnet이 구현했고, Flash QA는 1차 PASS였습니다.
  - **게이트:** 빌드 경고 0, Server.Tests 955/955(밸런스 스윕 제외), Harness.Tests 635/635, Unity EditMode 1356/1356, Windows 빌드 성공. 리포트는 `docs/qa/U6c-wire-*`, `docs/qa/U6c-hatch-*`.
  - **남은 것:**
    - 실제 경기의 붕괴·해치·걷어차기 화면은 미리보기로만 확인했습니다(봇끼리는 출구 전원까지 가지 못함).
    - v2 효과음이 없습니다.
    - 살인마는 Regressing을 받지 않습니다(서버 마스킹).
    - 화면 가장자리 거리 표시가 겹칩니다(기존 문제).

- **U7b-skillfx + 살인마 시야 테스트 수정 병합 후 실행 (2026-11-05):** 클라이언트만 바뀐 단위라 서버/하네스 점수 검사는 다시 돌리지 않았습니다(서버·Shared·하네스 코드 변경 없음).
  - **U7b-skillfx:** 스킬 9종의 월드 효과·결과 문구·효과음(`docs/ui/HUD_SPEC.md` §14). Opus가 구현했고, Gemini Flash QA는 1차 FAIL(위치형 릴레이 효과음이 듣는 사람 위치에 붙음) → 2차 PASS였습니다.
  - **테스트 수정:** 오프라인 살인마 시야 재검증 테스트가 가끔 실패하던 문제(제자리 생존자는 Silent라 봇 살인마가 오지 않음). 테스트 생존자가 사각형으로 걷고 한도를 2400 스냅샷으로 늘렸습니다. 단독 5회 연속 통과(첫 살인마 103~599 스냅샷).
  - **게이트:** 빌드 경고 0, Server.Tests 955/955(`Category!=BalanceSweep`), Unity EditMode 1386/1386, Windows 빌드 성공. 로그 `docs/qa/merge-1105/`. Harness.Tests는 코드 변경이 없어 다시 돌리지 않았습니다(직전 635/635).
  - **남은 것:** U7b 효과음은 실제로 들어 보지 않았습니다. 방화벽 막힘 장면은 촬영하지 않았습니다. 서버가 스킬 거절 이유를 보내지 않습니다(프로토콜 부족, §14.3).

- **U6f-sync-timing 병합 후 실행 (2026-11-05, main `fca9ed6`):** 플레이 점검(`docs/qa/playcheck-1105.md`)에서 싱크 체크 Great가 사실상 안 나오던 문제를 고쳤습니다. 서버가 체크를 한 틱 늦게 기록하고 누른 시각을 다음 틱 시작으로 잡아 0~50 ms 이르게 판정하던 것이 원인이었습니다(Great 구간 38.5 ms).
  - 이제 누른 입력은 도착 시각으로, 체크는 실제로 나간 시각으로 기록하고, 늦게 돈 틱도 보정합니다. 지연 보정은 왕복 전체(RTT)를 뺍니다. 판정 구간과 봇 결과 비율은 그대로입니다.
  - Opus 구현. Gemini Flash는 agy 로그인이 필요해 실행하지 못했고, 대체 규칙대로 Sonnet QA 1차 PASS(사소 5건).
  - **게이트:** 빌드 경고 0, Server.Tests 982/982(`Category!=BalanceSweep`), Harness.Tests 635/635, 하네스 100/100 3회 연속(브랜치에서 실행, 병합 결과와 코드 동일, `docs/qa/harness-u6f-run1..3.json`), Unity EditMode 1386/1386, Windows 빌드 성공. 실제 오프라인 경기: Great 조준 7/7 Great, Good 조준 8/8 Good(이전 0/6 Great). 로그 `docs/qa/merge-1105b/`, `docs/qa/U6f-sync-timing-runs/`.
  - **남은 것:** 서버에 RTT 측정이 없어 온라인에서는 실제 왕복만큼 늦게 판정됩니다. 별도 단위 U6g(`claude/sync-rtt-probe`)에서 진행 중입니다.

- **7개 브랜치 통합 병합 (2026-11-10, main `6237bfa`, fast-forward):** 임시 브랜치 `claude/qa-integration`에서 합쳐 통합 QA를 통과한 결과를 그대로 main에 올렸습니다. 병합 순서: U6d-bots(`7bb3963`, 봇 협동·움직이는 킬러·밸런스), U6g-rtt-probe(`ffaa8a6`, 서버 RTT 측정), Q1-playcheck-polish(`f377cae`), Q2-edge-labels(`ddfd199`), Q4-v2-sounds(`cb33aaf`), Q6-survivor-menu(`31342dc`), U4k-buildings(`0d77c85`, 건물 빈틈·스카이라인).
  - 충돌은 `ClientApp.cs` EndMatch 한 곳(Q4 `v2Audio.End()` + Q6 `ResetMenu()`, 둘 다 유지).
  - 2026-11-09부터 구현은 Opus/Sonnet, 검수는 Fable입니다.
  - **게이트:** 빌드 경고 0, Server.Tests 1062/1062, BalanceSweep 18/18, 하네스 100/100 3회, Harness.Tests 638/638, Unity EditMode 1468/1468, Windows 빌드 성공, 오프라인 실제 경기 2판(생존자 285초·킬러 190초, 둘 다 7/7 탈출, 오류 0). Fable 통합 검수 PASS, 막는 문제 0, SLOP 98/100. 보고서 `docs/qa/qa-int-1110.md`, 로그 `docs/qa/qa-int-runs/`. 병합 뒤 main에서 빌드와 Server.Tests 1062/1062를 다시 확인했습니다.
  - **남은 것(사소):** Esc 메뉴 "Captured twice" 문구가 Q1의 포획 횟수 의미와 어긋남(`PlayMenu.cs:35`). 화면 안 거리 라벨이 가장자리 화살표에 붙음. 통합 빌드에서 사람 해킹 장면과 Esc 메뉴 화면은 촬영 못 함. 연결 끊김 때 `v2Audio.End()` 생략(무해). U4k 사소 3건은 사용자 결정으로 보류. 285초 판은 표본 1개라 생존자 쪽 실제 경기를 2~3판 더 보면 좋습니다.
  - **다음:** Q3(스킬 거절 이유, Shared 패킷), Q5(판자·창문 넘기, GDD v2 §7).

- **minors-1110 병합 (2026-11-10, main `5ba712d`):** 통합 QA의 작은 문제 1·2를 고쳤습니다.
  - Esc 메뉴의 `CaptureCount ≥ 2` 줄이 횟수를 세지 않는 `Next capture eliminates you.`로 바뀌었습니다.
  - 킬러 월드 태그는 다른 태그·피해야 할 마커와 4 u(`TagGap`) 간격을 둡니다. 생존자 거리 태그는 가장자리 화살표도 피합니다. 붐빌 때 Overloaded/Exit 태그는 4 → 2 → 0 u로 물러납니다(HUD_SPEC §5·§6, 11/10).
  - Sonnet 구현, Fable QA 1차 PASS(사소 5건, 한 건은 검토 중 문서 수정).
  - **게이트:** Unity EditMode 1475/1475, Windows 빌드 성공, 빌드 경고 0, 킬러 오프라인 경기 200초 오류 0, SLOP 97/100. 보고서 `docs/qa/minors-1110-qa-a1.md`.
  - **남은 것:** 가장자리가 꽉 찼을 때 EdgeSpread의 3/2 u 간격과 TagLayout 4 u가 어긋날 수 있습니다(7명으로는 생기지 않음, 후속 후보). 정확한 "38 m / 39 m" 장면은 재현하지 않았고 테스트로 확인했습니다.

- **Q3-skill-reject 병합 (2026-11-10, main `0ee32a9`):** 서버가 스킬 사용을 거절하면 이제 이유를 보냅니다(HUD_SPEC §14.3의 프로토콜 부족 해소).
  - **프로토콜 v0.6:** 새 패킷 `S2C_SkillRejected` 0x0016 `[SkillId][Reason]`을 누른 사람에게만 보냅니다. 거절 이유 8가지는 NotInMatch, NotYourSkill, NotNormal, Cooldown, NoTarget, NoSwitchInProgress, Passive, Sandbox입니다.
  - 플레이어당 틱마다 응답은 1개까지입니다. 봇에게는 보내지 않습니다. 로그는 플레이어당 1초에 1줄입니다.
  - 킬러에게 새 정보는 없습니다(출구 진행도는 원래 WorldState에 있음, 숨은 생존자 위치와 무관함을 테스트로 확인).
  - 클라이언트는 이유별 문구를 바로 띄우고, 0.5초 대기는 예비로만 남겼습니다. 쿨다운 경계 문구 `Skill still on cooldown.` / `Not used · still on cooldown`.
  - Opus 구현, Fable QA 1차 PASS(사소 3건, 문구 1건은 검토 중 수정).
  - **게이트:** 빌드 경고 0, Server.Tests 1087/1087, BalanceSweep 18/18, 하네스 100/100 3회, Harness.Tests 641/641, Unity EditMode 1486/1486(문구 수정 뒤 재실행), Windows 빌드 성공, 킬러·생존자 오프라인 경기 오류 0. 보고서 `docs/qa/Q3-skill-reject-qa-a1.md`.
  - AGENTS.md의 하네스 명령을 실제 절차로 고쳤습니다(`--bots auto`는 없음: 7777 샌드박스 서버 + 7778 H6 서버, 실행마다 새로).
  - **남은 것:** 서버 이유 문구가 실제 경기 화면에 뜨는 장면은 촬영하지 못했습니다(EditMode와 로그로 확인). `RttProbeTests`의 실측 RTT 테스트가 PC가 바쁠 때 가끔 실패합니다(기존 문제, 후속 후보).
  - **다음:** Q5(판자·창문 넘기, GDD v2 §7).

UI는 별도 기준: `docs/ui/SLOP_DEFINITION.md` §9, **≥ 85점 + 게이트 무위반**.

---

## 6. UI/UX 진행 방식

1. ✅ **슬롭 정의 문서 작성** — `docs/ui/SLOP_DEFINITION.md` (시각 12·레이아웃 8·문구·인터랙션·게임 특화·프로세스 금지 목록 + 이 게임의 대안 방향 + 채점표)
2. D3: 그 문서를 컨텍스트로 넣은 상태에서 "방금 정의한 슬롭을 철저히 배제해 UI 제작" 브리프를 Codex에 전달
3. Flash(high)가 스크린샷으로 §9 채점 → Opus 판정 → 루프
- 역할별 HUD 분리 (10/27 시점 변경 반영, 상세 `docs/ui/HUD_SPEC.md`): 생존자(등 뒤 3인칭) = 하단 중앙 송신 미터 + 스킬 링, 살인마(1인칭) = 시야 속 핑 표식 + 처형 유예 안내
- 확인 필요 시 사용자에게 질문 (폰트·로고·색 취향 등)

---

## 7. Microsoft Store 준비

- 빌드: Unity Windows Standalone(IL2CPP, x64) → **MSIX 패키징** (또는 Store의 Win32 EXE/MSI 제출 경로)
- 매니페스트 기능: `internetClient`, `internetClientServer`, `privateNetworkClientServer`
- 접속 방식: 메인 메뉴에서 호스트 IP 입력 (기본값 `127.0.0.1:7777`). 서버는 이 PC에서 실행. Store 심사는 **오프라인 봇 모드**(✅ 확정)로 진행 가능하도록 안내문에 명시
- 개인정보 처리방침: 닉네임·IP만 처리, 저장 없음 — 공개 URL 필요(Notion 공개 페이지 활용 🔶)
- 연령 등급(IARC) 설문, 스크린샷 4장+, 자유 채팅 없음

---

## 8. 리스크

| 리스크 | 대응 |
|---|---|
| Store 심사자가 이 PC 서버에 접속 못 함 → 리젝 | 오프라인 봇 연습 모드 또는 심사 기간 서버 상시 가동 |
| 가정용 회선 포트포워딩/CGNAT 불가 | D3에 외부망 테스트, 불가 시 사용자 보고 |
| 1인칭 생존자 시야에서 Yaw 위조 | Yaw는 연출용으로만 사용, 판정에 미사용 |
| 토큰 급증 | 동시 에이전트 2개 제한, 루프 3회 상한, 오토컴팩트 450k |
| git이 PATH에 없음(GitHub Desktop 내장 git만 존재) | Git for Windows 설치 권장 (Codex·플러그인 일부가 git/bash 필요) |

---

## 9. 확인 필요

GDD §8 참조 (제한 시간 승패, 빈사 30 s 탈락, 오프라인 역할 선택, 위협 방향 정보).
