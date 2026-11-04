using System;
using PingTheKiller.Client.World;
using UnityEngine;

namespace PingTheKiller.Client.Settings
{
    // Where the settings live. PlayerPrefs in the game; a dictionary in tests.
    public interface ISettingsStore
    {
        bool TryGet(string key, out float value);
        void Set(string key, float value);
        void Flush();
    }

    public sealed class PlayerPrefsStore : ISettingsStore
    {
        public bool TryGet(string key, out float value)
        {
            // GetFloat returns the fallback when the key is missing or holds another type, so NaN means "not a float".
            value = PlayerPrefs.GetFloat(key, float.NaN);
            return !float.IsNaN(value);
        }
        public void Set(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public void Flush() => PlayerPrefs.Save();
    }

    // Player options (U7a). Presentation only: nothing here is sent to the server. FOV is the horizontal angle at 16:9
    // (the same convention as HUD_SPEC 6.0); the rigs convert it to the vertical angle Unity wants, so other aspect
    // ratios stay Hor+. Load reads each value on its own: a missing, NaN or out-of-range stored value becomes the default.
    // The setters clamp, so the screen can never push a value outside its range.
    public sealed class GameSettings
    {
        public const string Prefix = "ptk.settings.";
        public const string SurvivorFovKey = Prefix + "survivorFov", KillerFovKey = Prefix + "killerFov";
        public const string SensitivityKey = Prefix + "sensitivity", InvertYKey = Prefix + "invertY";
        public const string ReducedMotionKey = Prefix + "reducedMotion", VolumeKey = Prefix + "volume";
        public const float ReferenceAspect = 16f / 9;
        public const float SurvivorFovMin = 60, SurvivorFovMax = 90, KillerFovMin = 60, KillerFovMax = 100;
        public const float SensitivityMin = .25f, SensitivityMax = 3, SensitivityDefault = 1;
        public const int VolumeMin = 0, VolumeMax = 100, VolumeDefault = 100;
        // Today's rigs: Killer 90 degrees horizontal; Survivor vertical 50 (about 79.2 horizontal at 16:9).
        public const float KillerFovDefault = 90;
        public static readonly float SurvivorFovDefault = HorizontalFromVertical(SurvivorOrbitRig.VerticalFov, ReferenceAspect);

        private float survivorFov = SurvivorFovDefault, killerFov = KillerFovDefault, sensitivity = SensitivityDefault;
        private bool invertY, reducedMotion;
        private int volume = VolumeDefault;
        private readonly ISettingsStore store;
        public event Action Changed;

        public GameSettings(ISettingsStore store = null) { this.store = store; }

        public float SurvivorFov { get => survivorFov; set => Assign(ref survivorFov, Clamp(value, SurvivorFovMin, SurvivorFovMax, SurvivorFovDefault)); }
        public float KillerFov { get => killerFov; set => Assign(ref killerFov, Clamp(value, KillerFovMin, KillerFovMax, KillerFovDefault)); }
        public float Sensitivity { get => sensitivity; set => Assign(ref sensitivity, Clamp(value, SensitivityMin, SensitivityMax, SensitivityDefault)); }
        public bool InvertY { get => invertY; set { if (invertY == value) return; invertY = value; Changed?.Invoke(); } }
        public bool ReducedMotion { get => reducedMotion; set { if (reducedMotion == value) return; reducedMotion = value; Changed?.Invoke(); } }
        public int Volume
        {
            get => volume;
            set
            {
                int next = Mathf.Clamp(value, VolumeMin, VolumeMax);
                if (volume == next) return;
                volume = next; Changed?.Invoke();
            }
        }
        public float Volume01 => volume / 100f;
        // Vertical FOV for the rigs (Hor+ from the 16:9 horizontal angle).
        public float SurvivorVerticalFov => VerticalFromHorizontal(survivorFov, ReferenceAspect);
        public float KillerVerticalFov => VerticalFromHorizontal(killerFov, ReferenceAspect);

        // Mouse counts -> degrees. Mouse up looks up; Invert Y flips only the vertical sign.
        public Vector2 LookDelta(Vector2 counts, float degreesPerCount)
        {
            float scale = degreesPerCount * sensitivity;
            return new Vector2(counts.x * scale, counts.y * scale * (invertY ? -1 : 1));
        }

        public void ResetToDefaults()
        {
            survivorFov = SurvivorFovDefault; killerFov = KillerFovDefault; sensitivity = SensitivityDefault;
            invertY = false; reducedMotion = false; volume = VolumeDefault;
            Changed?.Invoke();
        }

        public static GameSettings Load(ISettingsStore store)
        {
            var settings = new GameSettings(store);
            if (store == null) return settings;
            settings.survivorFov = Read(store, SurvivorFovKey, SurvivorFovMin, SurvivorFovMax, SurvivorFovDefault);
            settings.killerFov = Read(store, KillerFovKey, KillerFovMin, KillerFovMax, KillerFovDefault);
            settings.sensitivity = Read(store, SensitivityKey, SensitivityMin, SensitivityMax, SensitivityDefault);
            settings.volume = (int)Read(store, VolumeKey, VolumeMin, VolumeMax, VolumeDefault);
            settings.invertY = Read(store, InvertYKey, 0, 1, 0) >= .5f;
            settings.reducedMotion = Read(store, ReducedMotionKey, 0, 1, 0) >= .5f;
            return settings;
        }

        public void Save()
        {
            if (store == null) return;
            store.Set(SurvivorFovKey, survivorFov); store.Set(KillerFovKey, killerFov);
            store.Set(SensitivityKey, sensitivity); store.Set(VolumeKey, volume);
            store.Set(InvertYKey, invertY ? 1 : 0); store.Set(ReducedMotionKey, reducedMotion ? 1 : 0);
            store.Flush();
        }

        public static float VerticalFromHorizontal(float horizontal, float aspect)
            => 2 * Mathf.Atan(Mathf.Tan(horizontal * .5f * Mathf.Deg2Rad) / aspect) * Mathf.Rad2Deg;
        public static float HorizontalFromVertical(float vertical, float aspect)
            => 2 * Mathf.Atan(Mathf.Tan(vertical * .5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;

        private static float Read(ISettingsStore store, string key, float min, float max, float fallback)
            => store.TryGet(key, out float value) && !float.IsNaN(value) && !float.IsInfinity(value) && value >= min && value <= max ? value : fallback;

        private static float Clamp(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        private void Assign(ref float field, float next)
        {
            if (Mathf.Approximately(field, next)) return;
            field = next; Changed?.Invoke();
        }
    }
}
