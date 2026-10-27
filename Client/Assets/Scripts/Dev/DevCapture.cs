using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PingTheKiller.Client.UI;
using PingTheKiller.Client.World;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Dev
{
    // Scripted run for screenshot QA: menu shot, join or offline, host setup, lobby shot, start, scripted W or wander,
    // play shots, event shots (hacking, exit open, lagging, result), optional rematch, optional synthetic result preview,
    // quit. Times are realtime seconds from the first frame; only created when DevArgs.Active.
    public sealed class DevCapture : MonoBehaviour
    {
        private DevArgs args;
        private ClientScreens screens;
        private double origin = -1, lobbyAt = -1, matchAt = -1, previewAt = -1, laggingAt = -1, resultAt = -1, reopenedAt = -1;
        private double backAt = -1, doneAt = -1;
        private bool menuShot, joined, configured, lobbyShot, started, play1, play4, previewShot, quitting, host, capturing;
        private bool restarted, position2, position5;
        private int participants, match;
        private Role role;
        private Wander wander;
        private bool located;
        private Vector3 position, positionAt2;
        private byte localStatus;
        private string localHud;
        private byte[] progress = Array.Empty<byte>();
        private int exitState = -1;
        private readonly HashSet<string> taken = new HashSet<string>();
        private readonly Queue<KeyValuePair<string, int>> pending = new Queue<KeyValuePair<string, int>>();
        public event Action<string> Join;
        public event Action Offline;
        // Local dispatch only: the handler feeds the packet to its own receive path and never sends it.
        public event Action<S2C_GameResult> PreviewResult;
        public event Action<IPacket> HostSend;

        public void Initialize(DevArgs value, ClientScreens ui = null)
        {
            args = value; screens = ui;
            foreach (string error in args.Errors) Debug.LogWarning("[DEV] " + error);
            if (args.Shots != null) Directory.CreateDirectory(args.Shots);
        }

        private bool Wandering => match == 1 && role == Role.Survivor && args.Wander > 0 && InWindow(args.Wander);
        private bool InWindow(float seconds)
        {
            if (matchAt < 0) return false;
            double t = Now - matchAt;
            return t >= 2 && t < 2 + seconds;
        }

        // Scripted forward input from the 2nd second of play: the wander window in the first match, else HoldW seconds.
        public bool HoldForward => Wandering || (!(match == 1 && args.Wander > 0 && role == Role.Survivor) && args.HoldW > 0 && InWindow(args.HoldW));

        // Wander steering: the yaw to use this frame (from the last tracked local position), or false when not wandering.
        public bool Steer(float currentYaw, float dt, out float yaw)
        {
            yaw = currentYaw;
            if (!Wandering || !located) return false;
            if (wander == null) wander = new Wander(currentYaw);
            yaw = wander.Next(SharedMap.Map, position.x, position.z, dt);
            return true;
        }

        // The local player's rendered position this frame (survivor: the interpolated own entry).
        public void Track(bool found, Vector3 value) { located = found; if (found) position = value; }

        public void OnLobby(S2C_LobbyState state, bool isHost)
        {
            if (lobbyAt < 0) lobbyAt = Now;
            host = isHost; participants = 0;
            foreach (Slot slot in state.Slots) if (slot.PlayerId != 0) participants++;
            if (resultAt >= 0 && reopenedAt < 0)
            {
                reopenedAt = Now;
                Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " lobby reopened after the result; back to lobby enabled=" + (screens != null && screens.BackToLobbyEnabled));
            }
            if (configured || !host) return;
            configured = true;
            if (args.Bots >= 0) HostSend?.Invoke(new C2S_SetBots { Count = (byte)args.Bots });
            if (args.RoleChoice.HasValue) HostSend?.Invoke(new C2S_SetRole { RoleChoice = args.RoleChoice.Value });
        }

        public void OnMatchStart(Role value, byte skill)
        {
            matchAt = Now; match++; role = value; wander = null; located = false;
            laggingAt = resultAt = reopenedAt = backAt = -1; localStatus = 0; localHud = null; progress = Array.Empty<byte>(); exitState = -1;
            Debug.Log("[DEV] match " + match + " start role=" + value + " skill=" + skill);
        }

        // Every WorldState of the match: event shots and the terminal/exit/status log.
        public void OnWorld(S2C_WorldState state, int localId)
        {
            double t = Now - matchAt;
            bool hacking = false, changed = progress.Length != state.Terminals.Count;
            if (changed) progress = new byte[state.Terminals.Count];
            for (int i = 0; i < state.Terminals.Count; i++)
            {
                byte value = state.Terminals[i].Progress;
                if (value >= 1 && value <= 99) hacking = true;
                if (progress[i] != value) { progress[i] = value; changed = true; }
            }
            bool exitOpen = state.ExitOpen != 0 && state.ExitLocked == 0;
            int exit = (state.ExitOpen != 0 ? 1 : 0) + (state.ExitLocked != 0 ? 2 : 0);
            if (changed || exit != exitState)
            {
                exitState = exit;
                var line = new StringBuilder("[DEV] t=").Append(Stamp(t)).Append(" terminals");
                foreach (TerminalState terminal in state.Terminals) line.Append(' ').Append(terminal.Id).Append(':').Append(terminal.Progress);
                line.Append(" required=").Append(state.TerminalsRequired).Append(" exitOpen=").Append(state.ExitOpen).Append(" exitLocked=").Append(state.ExitLocked)
                    .Append(" hud=\"").Append(HudCopy.TerminalsLine(state.Terminals, state.TerminalsRequired, exitOpen)).Append('"');
                Debug.Log(line.ToString());
            }
            // World shots wait until the camera and avatars are placed (the first ~100 ms of a match render nothing yet).
            if (hacking && located) EventShot("hacking.png");
            if (state.ExitOpen != 0 && located) EventShot("exit-open.png");
            foreach (PlayerState player in state.Players)
            {
                if (player.Id != localId) continue;
                if (player.Status == (byte)PlayerStatus.Lagging && localStatus != (byte)PlayerStatus.Lagging && laggingAt < 0) laggingAt = Now;
                string hud = HudCopy.StatusLine((PlayerStatus)player.Status, player.StatusTimer);
                if (player.Status != localStatus || hud != localHud)
                    Debug.Log("[DEV] t=" + Stamp(t) + " local status=" + (PlayerStatus)player.Status + " timer=" + player.StatusTimer.ToString("0.00", CultureInfo.InvariantCulture)
                        + " hud=\"" + hud + "\" pos=" + Stamp(player.X) + "," + Stamp(player.Z));
                localStatus = player.Status; localHud = hud;
            }
        }

        public void OnResult(S2C_GameResult result, string headline, string outcome)
        {
            resultAt = Now;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " match " + match + " end winner=" + (Role)result.WinnerRole + " reason=" + (GameEndReason)result.Reason
                + " escaped=[" + string.Join(",", result.EscapedIds) + "] back to lobby enabled=" + (screens != null && screens.BackToLobbyEnabled));
            Debug.Log("[DEV] result screen: \"" + headline + "\" / \"" + outcome + "\"");
            EventShot("result.png");
        }

        private double Now => Time.realtimeSinceStartupAsDouble - origin;
        private static string Stamp(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private void Update()
        {
            if (origin < 0) origin = Time.realtimeSinceStartupAsDouble; // engine start-up time does not count
            if (capturing) return; // keep the scripted order: nothing happens while a shot is pending
            double t = Now;
            bool shots = args.Shots != null;
            if (shots && !menuShot && t >= 1) { menuShot = true; Shot("menu.png"); return; }
            if ((args.Offline || args.Join != null) && !joined && (!shots || menuShot))
            {
                joined = true;
                if (args.Offline) Offline?.Invoke(); else Join?.Invoke(args.Join);
            }
            if (lobbyAt >= 0 && matchAt < 0)
            {
                if (shots && !lobbyShot && t >= lobbyAt + 1) { lobbyShot = true; Shot("lobby.png"); return; }
                if (args.Start && !started && host && participants >= 2 && (!shots || lobbyShot))
                {
                    started = true; HostSend?.Invoke(new C2S_StartMatch());
                }
            }
            if (match == 1 && shots)
            {
                if (!play1 && t >= matchAt + 1) { play1 = true; Shot("play-1s.png"); return; }
                if (!play4 && t >= matchAt + 4) { play4 = true; Shot("play-4s.png"); return; }
            }
            // The countdown is only on screen while the match runs; a capture that ends the match leaves no lagging shot.
            if (laggingAt >= 0 && resultAt < 0 && t >= laggingAt + 1) EventShot("lagging.png");
            if (laggingAt >= 0 && resultAt >= 0 && !taken.Contains("lagging.png") && taken.Add("lagging.png (skipped)"))
                Debug.Log("[DEV] lagging shot skipped: the match ended within 1 s of the capture");
            if (args.Rematch) Rematch(t);
            else if (resultAt >= 0 && doneAt < 0 && (!shots || taken.Contains("result.png"))) doneAt = t;
            if (match == 1 && args.PreviewResult.HasValue && previewAt < 0 && t >= matchAt + 5 && (!shots || play4))
            {
                previewAt = t;
                PreviewResult?.Invoke(new S2C_GameResult { WinnerRole = (byte)(args.PreviewResult == (byte)GameEndReason.SurvivorEscaped ? Role.Survivor : Role.Killer), Reason = args.PreviewResult.Value });
            }
            if (previewAt >= 0 && shots && !previewShot && t >= previewAt + 1)
            {
                previewShot = true; Shot("result-preview-" + ((GameEndReason)args.PreviewResult.Value).ToString().ToLowerInvariant() + ".png"); return;
            }
            // A run with a quit time ends early once everything it waits for has happened.
            bool done = doneAt >= 0 && t >= doneAt + 2;
            if (args.QuitAfter > 0 && !quitting && (t >= args.QuitAfter || done))
            {
                quitting = true; Debug.Log(done ? "[DEV] script complete, quitting" : "[DEV] quit after " + args.QuitAfter + " s");
                Application.Quit();
            }
        }

        // After the first result: wait for the lobby, press Back to lobby, then Start; hold W in the second match.
        private void Rematch(double t)
        {
            bool shots = args.Shots != null;
            if (match == 1 && resultAt >= 0 && reopenedAt >= 0 && backAt < 0 && t >= resultAt + 1.5 && (!shots || taken.Contains("result.png")))
            {
                bool pressed = screens != null && screens.PressBackToLobby();
                Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " press back to lobby: " + (pressed ? "done" : "disabled"));
                backAt = t;
                if (pressed) EventShot("lobby-return.png"); else doneAt = t;
            }
            if (match == 1 && backAt >= 0 && !restarted && doneAt < 0 && t >= backAt + 1.5)
            {
                restarted = true;
                bool pressed = screens != null && screens.PressStart();
                Debug.Log("[DEV] t=" + Stamp(t - matchAt) + " press start: " + (pressed ? "done" : "disabled"));
                if (!pressed) doneAt = t;
            }
            if (match != 2) return;
            double m = t - matchAt;
            if (!position2 && m >= 2)
            {
                position2 = true; positionAt2 = position;
                Debug.Log("[DEV] match 2 t=" + Stamp(m) + " local position " + Stamp(position.x) + "," + Stamp(position.z) + " located=" + located);
            }
            if (shots && !taken.Contains("rematch-play.png") && m >= 4) { taken.Add("rematch-play.png"); Shot("rematch-play.png"); }
            if (!position5 && m >= 5)
            {
                position5 = true;
                float moved = Vector2.Distance(new Vector2(positionAt2.x, positionAt2.z), new Vector2(position.x, position.z));
                Debug.Log("[DEV] match 2 t=" + Stamp(m) + " local position " + Stamp(position.x) + "," + Stamp(position.z) + " moved=" + Stamp(moved) + " m");
            }
            if (doneAt < 0 && position5 && m >= 6) doneAt = t;
        }

        // Each event shot is taken once, one frame after the event.
        private void EventShot(string name)
        {
            if (args.Shots == null || !taken.Add(name)) return;
            Debug.Log("[DEV] t=" + Stamp(Now - matchAt) + " event " + name); // match time, like the other [DEV] t= lines
            Shot(name, 1);
        }

        private void Shot(string name, int delayFrames = 0)
        {
            pending.Enqueue(new KeyValuePair<string, int>(name, Time.frameCount + delayFrames));
            if (!capturing) StartCoroutine(Capture());
        }

        // Synchronous end-of-frame capture, so each file holds exactly its frame before the script moves on.
        private IEnumerator Capture()
        {
            capturing = true;
            while (pending.Count > 0)
            {
                KeyValuePair<string, int> next = pending.Dequeue();
                while (Time.frameCount < next.Value) yield return null;
                yield return new WaitForEndOfFrame();
                string path = Path.GetFullPath(Path.Combine(args.Shots, next.Key));
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                try { File.WriteAllBytes(path, frame.EncodeToPNG()); Debug.Log("[DEV] screenshot " + path); }
                catch (Exception error) { Debug.LogWarning("[DEV] screenshot failed: " + error.Message); }
                finally { Destroy(frame); }
            }
            capturing = false;
        }
    }
}
