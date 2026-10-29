using System;
using System.Security.Cryptography;

namespace PingTheKiller.ServerCore
{
    // xoshiro256** (256-bit state) behind the System.Random surface. Production instances take 32 CSPRNG bytes, so the
    // hidden draws (bots, phantoms, killer-view order) cannot be recovered by sweeping a 31-bit System.Random seed.
    internal sealed class PrivateRandom : Random
    {
        private ulong _s0, _s1, _s2, _s3;

        private PrivateRandom() : base(0) { }

        internal static PrivateRandom FromCsprng()
        {
            var random = new PrivateRandom();
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                do rng.GetBytes(bytes);
                while (IsZero(bytes));
            }
            random._s0 = BitConverter.ToUInt64(bytes, 0);
            random._s1 = BitConverter.ToUInt64(bytes, 8);
            random._s2 = BitConverter.ToUInt64(bytes, 16);
            random._s3 = BitConverter.ToUInt64(bytes, 24);
            return random;
        }

        // Test hook path: one independent stream per (seed, stream) pair, expanded with SplitMix64.
        internal static PrivateRandom FromSeed(int seed, int stream)
        {
            var random = new PrivateRandom();
            ulong x = (ulong)(uint)seed | ((ulong)(uint)stream << 32);
            random._s0 = SplitMix(ref x);
            random._s1 = SplitMix(ref x);
            random._s2 = SplitMix(ref x);
            random._s3 = SplitMix(ref x);
            return random;
        }

        private static bool IsZero(byte[] bytes)
        {
            foreach (byte b in bytes) if (b != 0) return false;
            return true;
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        internal ulong NextUInt64()
        {
            ulong result = Rotl(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }

        // Unbiased draw in [0, bound) by rejecting the short top range.
        private ulong Below(ulong bound)
        {
            ulong threshold = (0UL - bound) % bound;
            while (true)
            {
                ulong value = NextUInt64();
                if (value >= threshold) return value % bound;
            }
        }

        protected override double Sample() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));
        public override double NextDouble() => Sample();
        public override int Next() => (int)Below(int.MaxValue);

        public override int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            return maxValue <= 1 ? 0 : (int)Below((ulong)maxValue);
        }

        public override int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            long range = (long)maxValue - minValue;
            return range <= 1 ? minValue : (int)(minValue + (long)Below((ulong)range));
        }

        public override void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            NextBytes(buffer.AsSpan());
        }

        public override void NextBytes(Span<byte> buffer)
        {
            for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(NextUInt64() >> 56);
        }
    }
}
