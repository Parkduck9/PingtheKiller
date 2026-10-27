# Store listing — Ping the Killer (핑 더 킬러)

Copy follows `docs/ui/SLOP_DEFINITION.md` §4: plain, factual, no hype words, no exclamation marks. Terms follow §8: Killer, Survivor, Signal, Silent, Rescue, Exit, Locked, Skill, Cooldown. Rules come from `docs/GDD.md` v0.2; if a number changes there, change it here.

Field limits and sizes marked *(verify)* come from general Partner Center experience, not from a page I fetched. Check them in the live form.

---

## 1. App name

| | |
|---|---|
| English | Ping the Killer |
| Korean | 핑 더 킬러 |
| Reserve | `Ping the Killer` in Partner Center first. Add the Korean name as the listing title for the `ko-KR` language. Names can be up to 256 characters, no emoji, no version number at the end |

## 2. Short description

**EN (one sentence):** A 1-vs-7 chase game where Survivors are visible to the Killer only while they move or hack. Play online or offline with bots.

**KO:** 생존자는 움직이거나 해킹하는 동안에만 살인마에게 보이는 1대7 추격 게임. 온라인 또는 봇과 오프라인으로 플레이합니다.

## 3. Full description

### English

Ping the Killer is a 1-vs-7 chase game. One player is the Killer. Up to seven players are Survivors.

**The rule:** A Survivor is visible to the Killer only while sending a Signal. Moving or hacking sends a Signal. Standing still makes the Survivor Silent, and the server stops sending that Survivor's position to the Killer.

**Survivors**
- Hack relay terminals to unlock the Exit. The number of terminals needed depends on how many Survivors are in the match (2, 3 or 4).
- One Survivor reaching the open Exit wins the match for the Survivors.
- Survivors never see where the Killer is. They get a threat level from 0 to 3 based on distance only, shown as beeps and noise. There is no direction.
- If the Killer catches a Survivor, that Survivor enters a packet-loss state (Lagging): slower, always visible to the Killer, unable to hack or use skills. A teammate who stays in contact for 3 seconds performs a Rescue. If nobody does within 60 seconds, the Survivor is disconnected from the match.

**Killer**
- Plays from a top-down view of the whole map. Survivors appear only while they send a Signal.
- Wins when every Survivor is Lagging or disconnected, or when the 10-minute timer ends.

**Skills:** Each match gives each player one random Skill with a Cooldown. There are 4 Killer skills and 5 Survivor skills, for example Ping Scanner (the Killer briefly reveals the nearest Survivor) and Dummy Packet (a Survivor leaves a fake Signal).

**Modes**
- **Offline Bots:** Play alone. Pick the Killer or Survivor role, then add or remove bots from 1v1 up to 1v7. No server and no internet connection needed.
- **Online:** One player hosts a match on their PC and others join by IP address, up to 8 players. Empty slots are filled by bots. Hosting listens on TCP port 7777; playing over the internet needs port forwarding on the host's router.

**Privacy and safety:** No accounts, no purchases, no ads, no free-text chat. The game processes only a nickname and the connecting IP address while a match runs, and stores neither.

### 한국어

핑 더 킬러는 1대7 추격 게임입니다. 한 명이 살인마, 최대 일곱 명이 생존자입니다.

**핵심 규칙:** 생존자는 Signal(신호)을 보내는 동안에만 살인마에게 보입니다. 움직이거나 해킹하면 Signal이 나갑니다. 가만히 있으면 Silent(정지) 상태가 되고, 서버는 그 생존자의 위치를 살인마에게 보내지 않습니다.

**생존자**
- 릴레이 단말을 해킹해 탈출구를 엽니다. 필요한 단말 수는 생존자 수에 따라 2, 3, 4개입니다.
- 한 명이라도 열린 탈출구에 도달하면 생존자의 승리입니다.
- 살인마의 위치는 보이지 않습니다. 거리만으로 정해지는 위협 단계(0~3)가 비프음과 잡음으로 전달되며 방향은 알려주지 않습니다.
- 살인마에게 잡히면 패킷 손실(Lagging) 상태가 됩니다. 이동 속도가 느려지고, 살인마에게 계속 보이며, 해킹과 스킬을 쓸 수 없습니다. 아군이 3초 동안 접촉을 유지하면 구출됩니다. 60초 안에 구출되지 못하면 매치에서 연결이 끊깁니다.

**살인마**
- 맵 전체를 위에서 내려다보는 시점으로 플레이합니다. 생존자는 Signal을 보내는 동안에만 보입니다.
- 모든 생존자가 Lagging이거나 연결이 끊기면, 또는 10분 제한 시간이 끝나면 승리합니다.

**스킬:** 판마다 플레이어마다 쿨다운이 있는 스킬이 무작위로 하나 주어집니다. 살인마 스킬 4종, 생존자 스킬 5종이 있습니다. 예: 핑 스캐너(가장 가까운 생존자를 잠깐 노출), 더미 패킷(가짜 Signal 남기기).

**모드**
- **오프라인 봇:** 혼자 플레이합니다. 살인마·생존자 역할을 고르고 봇을 1대1부터 1대7까지 추가하거나 삭제합니다. 서버와 인터넷 연결이 필요 없습니다.
- **온라인:** 한 명이 자기 PC에서 매치를 열고 다른 플레이어가 IP 주소로 접속합니다. 최대 8명이며 빈자리는 봇이 채웁니다. 호스트는 TCP 7777 포트를 사용하고, 인터넷을 통한 플레이에는 호스트 공유기의 포트 포워딩이 필요합니다.

**개인정보와 안전:** 계정, 결제, 광고, 자유 채팅이 없습니다. 매치 동안 닉네임과 접속 IP 주소만 처리하고 저장하지 않습니다.

## 4. Feature bullets

EN
- 1 Killer vs 1–7 Survivors, 10-minute matches
- Visible only while sending a Signal: Survivors who stand still are not sent to the Killer at all
- Killer top-down view, Survivor first-person view, each with its own HUD
- Hack 2–4 relay terminals, open the Exit, one escape wins
- Packet-loss state with Rescue by teammates instead of instant elimination
- 9 Skills, one random per player per match
- Offline Bot mode from 1v1 to 1v7, no internet needed
- Online play for up to 8 players by IP address
- No accounts, purchases, ads or chat

KO
- 살인마 1 대 생존자 1~7, 10분 매치
- Signal을 보내는 동안에만 보임: 가만히 있는 생존자의 위치는 살인마에게 전송되지 않음
- 살인마는 탑다운, 생존자는 1인칭 시점이며 HUD도 역할별로 다름
- 릴레이 단말 2~4개 해킹 후 탈출구 개방, 한 명만 탈출해도 승리
- 즉시 탈락 대신 패킷 손실 상태와 아군 구출
- 스킬 9종, 판마다 플레이어당 무작위 1개
- 1대1~1대7 오프라인 봇 모드, 인터넷 불필요
- IP 주소로 접속하는 최대 8인 온라인 플레이
- 계정·결제·광고·채팅 없음

## 5. Search terms *(verify count and length limits in the form; commonly up to 7 terms)*

EN: `asymmetric multiplayer`, `1v7`, `chase game`, `hide and seek`, `survival multiplayer`, `stealth`, `offline bots`

KO: `비대칭 멀티플레이`, `1대7`, `추격 게임`, `술래잡기`, `생존 게임`, `스텔스`, `봇 대전`

Do not add other games' names or "free" unless the game is free in Partner Center pricing.

## 6. Category

- Primary: **Games**
- Sub-category: **Action & adventure** *(verify the sub-category names in Partner Center; "Strategy" and "Puzzle & trivia" do not fit)*. A reasonable second choice is the multiplayer-focused category if one is listed.
- Platform: Windows 10/11, x64 PC. Input: keyboard and mouse. Add gamepad only after it works (SLOP §5 requires keyboard and gamepad navigation for Store).
- Price: the plan says no purchases. Set the base price to Free unless you decide otherwise.

## 7. Age rating (IARC)

Category choice: **Game**. Answer for the build as shipped. Wording below is paraphrased from the usual IARC game questionnaire; the live form wording differs, so match each answer to the closest question.

| Topic | Proposed answer | Reasoning |
|---|---|---|
| Violence | Yes, abstract and non-graphic. No blood, no gore, no injury shown | A capture puts a Survivor into a packet-loss state (slower, screen stutter). A second capture is shown as the connection being cut and the Survivor leaving the match. Characters are basic shapes, not realistic people |
| Realistic violence against humans | No | See above; nothing depicts physical harm |
| Blood or gore | No | None in any state |
| Fear / horror content | Mild tension only | Chase pressure from escalating beeps and static noise. No monsters, corpses or horror imagery |
| Sexual content or nudity | No | |
| Profanity / crude humor | No | No text content beyond the interface |
| Drugs, alcohol, tobacco | No | |
| Gambling, simulated gambling, loot boxes | No | No purchases of any kind |
| In-app purchases | No | |
| Online interactions between users | Yes | Online mode lets players join the same match |
| User-to-user communication (chat, voice) | No free chat or voice | Players cannot send messages to each other |
| User-generated content shared with others | Yes, limited to the nickname | The nickname is free text shown to other players in the same match. State this honestly; see open item 3 |
| Shares user's location with others | No | |
| Shares personal information with third parties | No | Nickname and IP are not stored and not sent to third parties |
| Unrestricted internet access / web browser | No | |
| Digital purchases / ads | No | |

**Expected result:** a low rating. I cannot predict the exact value for each region; the IARC output decides. Save the IARC rating ID and certificate email.

**When to retake the questionnaire:** if you replace the shapes with human or realistic characters, add blood or injury effects, add chat or voice, or add purchases.

## 8. Screenshot shot list

Capture from a release build at 1920×1080 (PNG, no scaling artifacts) *(verify the accepted sizes in Partner Center; at least one screenshot is required and the plan targets 4 or more)*. No placeholders, no debug overlays, no cursor, and no UI that fails the `docs/ui/SLOP_DEFINITION.md` §9 review (SLOP §7: a placeholder in a Store screenshot is a defect).

| # | Game state | What it shows | Caption (EN / KO) |
|---|---|---|---|
| 1 | Survivor, first-person, mid-hack on a relay terminal, **Signal** state | Bottom-center transmit meter in Signal, terminal progress, skill ring with Cooldown | Hacking sends a Signal. / 해킹하는 동안 Signal이 나갑니다. |
| 2 | Survivor, first-person, **Silent** and standing still, threat level 2 visible as edge noise | The meter in Silent, the noise effect. This is the core tension | Standing still hides you. / 가만히 있으면 보이지 않습니다. |
| 3 | Killer, top-down, one Survivor blip fading after Signal ends, **Locked** Exit marked on the map | The map is the HUD; skill and Cooldown bottom-left; no hint of Silent Survivors | The Killer sees only Signals. / 살인마는 Signal만 봅니다. |
| 4 | Survivor in **Lagging** state next to an ally performing a Rescue (3 s progress) | Screen stutter and afterimage effect, rescue progress | Lagging Survivors can be rescued. / Lagging 상태의 생존자는 구출할 수 있습니다. |
| 5 | Lobby with 8 fixed slots: 1 player, 3 bots, 4 empty, role selector and Add/Remove bot controls (Offline Bots) | The slot grid and the bot controls | Add bots from 1v1 to 1v7. / 봇을 1대1부터 1대7까지 추가합니다. |
| 6 | Result screen after a Survivor escapes: winner role, escaped Survivors, hacks and rescues | The plain result screen (no confetti or banner, per SLOP §6) | Result of the match. / 매치 결과. |

Also needed *(verify sizes)*: a 1:1 and a 2:3 box art image and the app tile images listed in the Store listing page. Use the final logo only; the repository has none yet.

## 9. Notes for certification

Paste into **Submission options → Notes for certification** (English; testers read English).

```
Ping the Killer is a multiplayer game that also has a full single-player mode. Please test with Offline Bots mode. It needs no server, no internet connection and no account.

How to test (about 5 minutes):
1. Start the game and choose "Offline (Bots)" in the main menu.
2. In the lobby, choose a role (Survivor or Killer). Use "Add bot" until at least 3 bots are listed, then choose "Start".
3. As a Survivor: move to a relay terminal, hold the interact key to hack it, and hack the required number of terminals (2 to 4, shown on screen). Then walk into the open Exit. A Survivor entering the open Exit ends the match with a Survivor win.
4. As the Killer: you see the map from above and chase Survivors who are sending a Signal. Touching a Survivor within 1.2 m puts it in the Lagging (packet loss) state.
5. A match ends when a Survivor escapes, when every Survivor is Lagging or disconnected, or after 10 minutes.

Online modes (Host, Join by IP) need other players and are not needed to evaluate the app. Hosting listens on TCP port 7777, so Windows may show a Windows Defender Firewall prompt when you choose Host. Offline Bots mode does not show it. [Remove this sentence if the build shows a prompt in Offline mode too.]

No sign-in, no purchases, no ads, no chat. The app stores nothing about the player. Privacy policy: <PRIVACY POLICY URL>
Controls are shown in the in-game pause menu. [Verify before submission.]
```

---

## Open items for the developer

1. Reserve the name in Partner Center and check availability of `Ping the Killer` and `핑 더 킬러`.
2. Replace `<PRIVACY POLICY URL>` after publishing `PRIVACY_POLICY.md`.
3. Decide whether the nickname is filtered. IARC asks about user-generated content and user interaction. The answers above state the nickname truthfully; a filter or character limit reduces the question about inappropriate names.
4. Confirm the menu labels used in the certification notes ("Offline (Bots)", "Add bot", "Start", interact key) match the final UI text, and that controls are shown in-game.
5. Confirm the numbers in the descriptions against the final build: 10 minutes, 1.2 m capture, 3 s Rescue, 60 s timeout, 2/3/4 terminals, 4 Killer and 5 Survivor skills, 8-player limit. They are the `docs/GDD.md` v0.2 defaults marked as adjustable after playtesting.
6. Capture the 6 screenshots from the release build after the UI review passes (≥ 85). Screenshot 5 needs the lobby to have the Add/Remove bot controls; screenshot 4 needs the Lagging visuals.
7. Prepare the logo and box art. None exists in the repository.
8. Decide gamepad support and the minimum hardware requirements to list. Neither is specified in `docs/PLAN.md` or `docs/GDD.md`.
9. In Partner Center, verify: the search-term count and length limits, the category and sub-category names, the screenshot sizes, and the price setting.
10. Complete the IARC questionnaire yourself in Partner Center; the answers above are proposals. You are responsible for accuracy.
