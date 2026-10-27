using UnityEngine;

namespace PingTheKiller.Client.World
{
    public sealed class ThreatAudio : MonoBehaviour
    {
        private const int Rate = 44100;
        private AudioSource source;
        private AudioClip beep;
        private byte level;
        private double next = double.PositiveInfinity, lastBeep = double.NegativeInfinity;

        // 880 Hz, 60 ms, 5 ms linear attack/release.
        public static float[] BuildBeepSamples(int sampleRate)
        {
            var samples = new float[Mathf.RoundToInt(sampleRate * .06f)];
            float ramp = sampleRate * .005f;
            for (int i = 0; i < samples.Length; i++)
            {
                float envelope = Mathf.Min(1, Mathf.Min(i / ramp, (samples.Length - 1 - i) / ramp));
                samples[i] = .15f * envelope * Mathf.Sin(2 * Mathf.PI * 880 * i / sampleRate);
            }
            return samples;
        }

        // Rising threat beeps now and restarts the schedule; falling threat never beeps early.
        public static double NextBeepAfterChange(byte from, byte to, double now, double lastBeep, double scheduledNext)
        {
            float interval = ClientMath.BeepInterval(to);
            if (float.IsInfinity(interval)) return double.PositiveInfinity;
            if (to > from) return now;
            if (double.IsNegativeInfinity(lastBeep)) return scheduledNext;
            return System.Math.Min(scheduledNext, lastBeep + interval);
        }

        private void Awake()
        {
            float[] samples = BuildBeepSamples(Rate);
            beep = AudioClip.Create("Threat 880 Hz 60 ms", samples.Length, 1, Rate, false);
            beep.SetData(samples, 0);
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0;
            source.clip = beep;
        }
        public void SetLevel(byte threat)
        {
            if (level != threat) next = NextBeepAfterChange(level, threat, Time.unscaledTimeAsDouble, lastBeep, next);
            level = threat;
            if (level == 0 && source != null) source.Stop();
        }
        private void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            if (level == 0 || source == null || !Application.isFocused || now < next) return;
            source.Play();
            lastBeep = now;
            next = now + ClientMath.BeepInterval(level);
        }
        private void OnDestroy() { if (beep != null) Destroy(beep); }
    }
}
