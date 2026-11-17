using UnityEngine;
using UnityEngine.UIElements;

namespace PingTheKiller.Client.World
{
    // HUD_SPEC 4.5 (11/17, U8) Lagging look on the own Survivor. The world camera always renders to the screen at full
    // rate (the 8 Hz frame hold is gone). Two UI Toolkit layers behind every screen and the HUD carry the look:
    // - back: the steady look while Status = Lagging (a dark veil, a vignette, faint scanlines), faded in over 0.25 s;
    // - front: the 0.4 s glitch burst from the swing's contact (a 20 % Killer-red flash, danger / neutral tear bars).
    // Reduced (the Reduced motion setting, or -ptk-reduce-stutter which forces it on): the steady look only.
    // The class keeps its name so the settings and dev hooks stay as they are; LaggingLook holds the timing.
    public sealed class LaggingStutter : MonoBehaviour
    {
        private static readonly Color VeilColor = new Color32(14, 18, 24, 255), Shade = new Color32(6, 8, 12, 255);
        private static readonly Color Flash = new Color32(229, 72, 77, 255), Neutral = new Color32(154, 152, 144, 255);
        private VisualElement back, front, veil, vignette, scan, flash;
        private readonly VisualElement[] tears = new VisualElement[LaggingLook.TearBars];
        private Texture2D vignetteTexture, scanTexture;
        private readonly LaggingLook look = new LaggingLook();
        private bool flagReduced, optionReduced, reduced;
        public bool Active => look.Active;
        // Reduced if the dev flag forced it on at start or the player's Reduced motion setting is on.
        public bool Reduced => reduced;
        public void SetReducedOption(bool on) { optionReduced = on; reduced = flagReduced || optionReduced; }
        // Frames the look was drawn since it started (dev log and tests).
        public int Presented { get; private set; }
        public LaggingLook.Frame Last { get; private set; }
        private bool backShown, frontShown;
        private float shownSteady = -1;
        public bool BackShown => back != null && back.style.display == DisplayStyle.Flex;
        public bool FrontShown => front != null && front.style.display == DisplayStyle.Flex;

        // camera: kept for the call site; the look never redirects the camera.
        public void Initialize(Camera camera, VisualElement backLayer, VisualElement frontLayer, bool reduce)
        {
            back = backLayer; front = frontLayer; flagReduced = reduce; reduced = flagReduced || optionReduced;
            if (vignetteTexture == null) vignetteTexture = Vignette(128);
            if (scanTexture == null) scanTexture = Scanlines();
            veil = Layer(back); veil.style.backgroundColor = VeilColor;
            // Fixed tints; the per-frame strength is element opacity only (no geometry rebuild). Each layer is one quad: the
            // scanlines are one stretched 1 x 240 texture, never a repeated tile (a 4 x 3 px repeat is ~77k quads at 720p).
            var stretch = new StyleBackgroundSize(new BackgroundSize(Length.Percent(100), Length.Percent(100)));
            vignette = Layer(back); vignette.style.backgroundImage = new StyleBackground(vignetteTexture); vignette.style.backgroundSize = stretch;
            vignette.style.unityBackgroundImageTintColor = new Color(Shade.r, Shade.g, Shade.b, LaggingLook.VignetteAlpha);
            scan = Layer(back);
            scan.style.backgroundImage = new StyleBackground(scanTexture); scan.style.backgroundSize = stretch;
            scan.style.unityBackgroundImageTintColor = new Color(0, 0, 0, LaggingLook.ScanAlpha);
            flash = Layer(front); flash.style.backgroundColor = Flash;
            for (int i = 0; i < tears.Length; i++)
            {
                var bar = new VisualElement { pickingMode = PickingMode.Ignore };
                bar.style.position = Position.Absolute; bar.style.left = 0; bar.style.right = 0;
                bar.style.backgroundColor = i % 2 == 0 ? new Color(Flash.r, Flash.g, Flash.b, .5f) : new Color(Neutral.r, Neutral.g, Neutral.b, .35f); // danger + neutral, never the ally colour
                front.Add(bar); tears[i] = bar;
            }
            Show(back, false); Show(front, false);
        }

        private static VisualElement Layer(VisualElement parent)
        {
            var layer = new VisualElement { pickingMode = PickingMode.Ignore };
            layer.style.position = Position.Absolute;
            layer.style.left = 0; layer.style.right = 0; layer.style.top = 0; layer.style.bottom = 0;
            parent.Add(layer);
            return layer;
        }

        public void SetActive(bool on) => SetActive(on, Time.unscaledTimeAsDouble);

        public void SetActive(bool on, double now)
        {
            if (on == look.Active) return;
            look.SetActive(on, now);
            Presented = 0;
            if (on) Debug.Log("[client] Lagging look on (" + (Reduced ? "reduced: steady look only" : "steady look + capture burst") + ", full frame rate)");
        }

        // The own capture: the glitch burst (skipped in Reduced).
        public void Capture() => look.Capture(Time.unscaledTimeAsDouble);
        public void Capture(double now) => look.Capture(now);

        private void LateUpdate() => Apply(Time.unscaledTimeAsDouble);

        // Draws the look at `now` (tests call it directly).
        public void Apply(double now)
        {
            if (back == null) return;
            LaggingLook.Frame frame = look.Evaluate(now, reduced);
            Last = frame;
            bool steady = frame.Steady > 0, burst = frame.Burst > 0;
            if (steady != backShown) { backShown = steady; Show(back, steady); if (!steady) shownSteady = -1; }
            if (burst != frontShown) { frontShown = burst; Show(front, burst); }
            if (steady)
            {
                if (frame.Steady != shownSteady)
                {
                    shownSteady = frame.Steady;
                    veil.style.opacity = LaggingLook.VeilAlpha * frame.Steady;
                    vignette.style.opacity = frame.Steady; scan.style.opacity = frame.Steady;
                }
                Presented++;
            }
            if (burst)
            {
                flash.style.opacity = frame.Flash;
                for (int i = 0; i < tears.Length; i++)
                {
                    LaggingLook.Tear(frame.TearSeed, i, out float top, out float height, out float direction);
                    tears[i].style.top = Length.Percent(top * 100); tears[i].style.height = Length.Percent(height * 100);
                    tears[i].style.translate = new Translate(direction * LaggingLook.TearPixels * frame.Burst, 0);
                    tears[i].style.opacity = frame.Burst;
                }
            }
        }

        private static void Show(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // Alpha rises from 45 % of the radius to the corners (white; the element tint sets colour and strength).
        private static Texture2D Vignette(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "lagging vignette", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + .5f) / size * 2 - 1, dy = (y + .5f) / size * 2 - 1;
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                    float a = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.45f, 1f, r));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        // One dark row in three, 240 rows, stretched over the screen (3 px apart at 720p).
        private static Texture2D Scanlines()
        {
            var texture = new Texture2D(1, 240, TextureFormat.RGBA32, false) { name = "lagging scanlines", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var rows = new Color32[240];
            for (int i = 0; i < rows.Length; i++) rows[i] = new Color32(255, 255, 255, (byte)(i % 3 == 0 ? 255 : 0));
            texture.SetPixels32(rows);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in new[] { vignetteTexture, scanTexture })
            {
                if (texture == null) continue;
                if (Application.isPlaying) Destroy(texture); else DestroyImmediate(texture);
            }
        }
    }
}
