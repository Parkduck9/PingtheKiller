# 핑 더 킬러 (Ping the Killer) — 개발 계획서

- 작성: 2026-10-27 (Claude Opus 5.5) · 원안: `ping_the_killer.md`, Notion 「Server로 직접 만들기」
- 상태: **v0.1 초안** — 🔶 표시는 기본값으로 정한 항목(사용자 확인 시 확정)

---

## 0. 원안 대비 변경점

| 항목 | 원안 | 확정 |
|---|---|---|
| 서버 위치 | 무료 클라우드 인스턴스 | **이 PC에서 .NET 서버 실행** (LAN + 공유기 포트포워딩) — 클라우드 단계 삭제 |
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

---

## 3. 일정 (Notion 일정 기준, 서버 클라우드 단계 제거)

| 날짜 | 목표 | 완료 조건 |
|---|---|---|
| **D1 10/27 (오늘)** | 기획·프로토콜 확정, Shared + 서버 + 프레이밍, Unity 접속, 봇 7개 이동이 Unity 화면에 보임 | 하네스 H1·H2 통과, Unity에서 봇 8개 시각 확인 |
| **D2 10/28** | 게임 규칙 전부(노출·체포·구출·탈출·스킬 9종), 끊김/재접속, 보간, 치트 방지 | 하네스 전체 통과(§5 기준점) |
| **D3 10/29** | UI/UX(슬롭 배제), 1인칭/탑다운 카메라 완성, 로비(Host/Join/IP), 포트포워딩 테스트, 개인정보 처리방침 | UI 리뷰 ≥ 85점, 외부망 접속 1회 성공 |
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
- 커밋은 Opus PASS 이후에만. 동시 에이전트 최대 2개.
- AI끼리 대화는 영어, 사용자 보고는 한국어.

---

## 5. 하네스 (자동 채점) — 100점

| # | 항목 | 점수 | 게이트 |
|---|---|---:|:---:|
| H1 | 10,000개 메시지를 무작위 분할/병합 스트림으로 전송 → 유실·파싱 오류 0건 | 25 | ✔ |
| H2 | 플레이어 2 + 봇 6 접속, 60 s 동안 틱 p99 ≤ 60 ms, 모든 클라 WorldState 수신 간격 p99 ≤ 120 ms | 20 | |
| H3 | 정지한 생존자의 좌표가 살인마 수신 패킷에 **바이트 단위로 0회 등장** | 20 | ✔ |
| H4 | 비정상 입력(초과 길이·음수 길이·미지 ID·NaN·속도 10배 방향·초당 500회 입력) → 서버 크래시 0, 최대속도 초과 이동 0 | 15 | ✔ |
| H5 | 접속 강제 종료 후 5 s 내 재접속 → 같은 PlayerId·위치·상태 복원 | 10 | |
| H6 | 시나리오: 단말 해킹 → 탈출구 개방 → 체포 → 구출 → 탈출 → GameResult 일치, 생존자 패킷에 살인마 좌표 0회 | 10 | |

**통과 기준점 (잠정):** **총점 ≥ 90 그리고 게이트(H1·H3·H4) 만점.**
- 확정 방법: U3 완료 직후 하네스를 처음 실행해 결과를 이 표 아래에 기록하고, Opus가 기준점을 **이 문서에 직접 확정 기입**한다. 첫 실행에서 의도적 결함 버전(마스킹 끈 서버)을 돌려 하네스가 실제로 감점하는지도 확인(하네스 자체 검증).
- 첫 실행 결과: _(U3 후 기입)_

UI는 별도 기준: `docs/ui/SLOP_DEFINITION.md` §9, **≥ 85점 + 게이트 무위반**.

---

## 6. UI/UX 진행 방식

1. ✅ **슬롭 정의 문서 작성** — `docs/ui/SLOP_DEFINITION.md` (시각 12·레이아웃 8·문구·인터랙션·게임 특화·프로세스 금지 목록 + 이 게임의 대안 방향 + 채점표)
2. D3: 그 문서를 컨텍스트로 넣은 상태에서 "방금 정의한 슬롭을 철저히 배제해 UI 제작" 브리프를 Codex에 전달
3. Flash(high)가 스크린샷으로 §9 채점 → Opus 판정 → 루프
- 역할별 HUD 분리: 생존자(1인칭) = 하단 중앙 송신 미터 + 스킬 링, 살인마(탑다운) = 맵 자체가 HUD
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
