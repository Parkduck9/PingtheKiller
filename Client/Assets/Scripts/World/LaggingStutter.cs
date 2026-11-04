using PingTheKiller.Client.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 4.5 Lagging frame stutter (own Survivor, Status = Lagging). While active, the world camera renders into an
    // off-screen texture; at 8 Hz (StutterClock) that image is copied out and shown full-screen behind the HUD, blended over
    // the previous presented frame at 30 % (two stacked UI layers: previous at full opacity, newest at 70 %, so the result
    // is 0.7 newest + 0.3 previous). The HUD (UI Toolkit, drawn on top) and the audio stay at full rate. No shake, no flash.
    // Reduced (the Reduced motion setting, or -ptk-reduce-stutter which forces it on): a new frame every frame, still blended 30 %, no hold and no border.
    public sealed class LaggingStutter : MonoBehaviour
    {
        private Camera view;
        private VisualElement back, front;
        private RenderTexture live, newest, previous;
        private readonly StutterClock clock = new StutterClock();
        private bool flagReduced, optionReduced, reduced, active, primed;
        private int startFrame;
        public bool Active => active;
        // Reduced if the dev flag forced it on at start or the player's Reduced motion setting is on.
        public bool Reduced => reduced;
        public void SetReducedOption(bool on) { optionReduced = on; reduced = flagReduced || optionReduced; }
        // Frames presented since the stutter started (dev log and tests).
        public int Presented { get; private set; }

        public void Initialize(Camera camera, VisualElement backLayer, VisualElement frontLayer, bool reduce)
        {
            view = camera; back = backLayer; front = frontLayer; flagReduced = reduce; reduced = flagReduced || optionReduced;
            front.style.unityBackgroundImageTintColor = new Color(1, 1, 1, 1 - StutterClock.PreviousWeight);
            Show(false);
        }

        public void SetActive(bool on)
        {
            if (on == active || view == null) return;
            active = on; primed = false; Presented = 0;
            clock.Reset();
            if (on)
            {
                Allocate();
                view.targetTexture = live;
                startFrame = Time.frameCount;
                Debug.Log("[client] Lagging stutter on (" + (Reduced ? "reduced: blend only" : "8 Hz hold + 30 % blend") + ")");
            }
            else
            {
                view.targetTexture = null;
                Show(false);
            }
        }

        private void LateUpdate()
        {
            if (!active) return;
            if (live == null || live.width != Screen.width || live.height != Screen.height)
            {
                view.targetTexture = null; Allocate(); view.targetTexture = live; startFrame = Time.frameCount; primed = false;
            }
            if (Time.frameCount <= startFrame) return; // the camera has not rendered into the texture yet
            if (!clock.Present(Time.unscaledTimeAsDouble, reduced)) return;
            RenderTexture spare = previous; previous = newest; newest = spare;
            Graphics.Blit(live, newest);
            if (!primed) { Graphics.Blit(live, previous); primed = true; }
            back.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(previous));
            front.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(newest));
            Show(true);
            back.MarkDirtyRepaint(); front.MarkDirtyRepaint();
            Presented++;
        }

        private void Allocate()
        {
            Release();
            int w = Mathf.Max(1, Screen.width), h = Mathf.Max(1, Screen.height);
            live = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "stutter live" };
            newest = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "stutter newest" };
            previous = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "stutter previous" };
        }

        private void Show(bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (back != null) back.style.display = display;
            if (front != null) front.style.display = display;
        }

        private void Release()
        {
            foreach (RenderTexture texture in new[] { live, newest, previous })
                if (texture != null) { texture.Release(); Destroy(texture); }
            live = newest = previous = null;
        }

        private void OnDestroy()
        {
            if (view != null && active) view.targetTexture = null;
            Release();
        }
    }
}
