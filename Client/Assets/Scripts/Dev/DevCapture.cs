using System;
using System.Collections;
using System.IO;
using PingTheKiller.Shared.Game;
using PingTheKiller.Shared.Packets;
using UnityEngine;

namespace PingTheKiller.Client.Dev
{
    // Scripted run for screenshot QA: menu shot, join, host setup, lobby shot, start, scripted W, play shots, quit.
    // Times are realtime seconds from the first frame; only created when DevArgs.Active.
    public sealed class DevCapture : MonoBehaviour
    {
        private DevArgs args;
        private double origin = -1, lobbyAt = -1, matchAt = -1;
        private bool menuShot, joined, configured, lobbyShot, started, play1, play4, quitting, host, capturing;
        private int participants;
        public event Action<string> Join;
        public event Action<IPacket> HostSend;

        public void Initialize(DevArgs value)
        {
            args = value;
            foreach (string error in args.Errors) Debug.LogWarning("[DEV] " + error);
            if (args.Shots != null) Directory.CreateDirectory(args.Shots);
        }

        // Scripted forward input from the 2nd second of play for HoldW seconds.
        public bool HoldForward
        {
            get
            {
                if (matchAt < 0 || args.HoldW <= 0) return false;
                double t = Now - matchAt;
                return t >= 2 && t < 2 + args.HoldW;
            }
        }

        public void OnLobby(S2C_LobbyState state, bool isHost)
        {
            if (lobbyAt < 0) lobbyAt = Now;
            host = isHost; participants = 0;
            foreach (Slot slot in state.Slots) if (slot.PlayerId != 0) participants++;
            if (configured || !host) return;
            configured = true;
            if (args.Bots >= 0) HostSend?.Invoke(new C2S_SetBots { Count = (byte)args.Bots });
            if (args.RoleChoice.HasValue) HostSend?.Invoke(new C2S_SetRole { RoleChoice = args.RoleChoice.Value });
        }

        public void OnMatchStart() { if (matchAt < 0) matchAt = Now; }

        private double Now => Time.realtimeSinceStartupAsDouble - origin;

        private void Update()
        {
            if (origin < 0) origin = Time.realtimeSinceStartupAsDouble; // engine start-up time does not count
            if (capturing) return; // keep the scripted order: nothing happens while a shot is pending
            double t = Now;
            bool shots = args.Shots != null;
            if (shots && !menuShot && t >= 1) { menuShot = true; Shot("menu.png"); return; }
            if (args.Join != null && !joined && (!shots || menuShot)) { joined = true; Join?.Invoke(args.Join); }
            if (lobbyAt >= 0 && matchAt < 0)
            {
                if (shots && !lobbyShot && t >= lobbyAt + 1) { lobbyShot = true; Shot("lobby.png"); return; }
                if (args.Start && !started && host && participants >= 2 && (!shots || lobbyShot))
                {
                    started = true; HostSend?.Invoke(new C2S_StartMatch());
                }
            }
            if (matchAt >= 0 && shots)
            {
                if (!play1 && t >= matchAt + 1) { play1 = true; Shot("play-1s.png"); return; }
                if (!play4 && t >= matchAt + 4) { play4 = true; Shot("play-4s.png"); return; }
            }
            if (args.QuitAfter > 0 && !quitting && t >= args.QuitAfter)
            {
                quitting = true; Debug.Log("[DEV] quit after " + args.QuitAfter + " s");
                Application.Quit();
            }
        }

        private void Shot(string name) => StartCoroutine(Capture(name));

        // Synchronous end-of-frame capture, so the file holds exactly this frame before the script moves on.
        private IEnumerator Capture(string name)
        {
            capturing = true;
            yield return new WaitForEndOfFrame();
            string path = Path.GetFullPath(Path.Combine(args.Shots, name));
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            try { File.WriteAllBytes(path, frame.EncodeToPNG()); Debug.Log("[DEV] screenshot " + path); }
            catch (Exception error) { Debug.LogWarning("[DEV] screenshot failed: " + error.Message); }
            finally { Destroy(frame); capturing = false; }
        }
    }
}
