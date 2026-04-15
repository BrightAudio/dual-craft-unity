// ═══════════════════════════════════════════════════════
//  DUAL CRAFT — Secure RNG (Cryptographic Random)
//
//  "Never shuffle client-side. Use a cryptographic
//   random number generator on the server to ensure
//   fair play and prevent manipulation."
//
//  Wraps System.Security.Cryptography for deterministic
//  seeding and tamper-proof random generation. The server
//  uses this for deck shuffling, dice rolls, and any
//  gameplay randomness. Pure C# — no Unity dependencies.
// ═══════════════════════════════════════════════════════

using System;
using System.Security.Cryptography;

namespace DualCraft.Networking
{
    /// <summary>
    /// Cryptographically secure random number generator for
    /// server-side gameplay randomness. Thread-safe.
    /// </summary>
    public static class SecureRNG
    {
        /// <summary>
        /// Generate a cryptographically random int (full range).
        /// </summary>
        public static int NextInt()
        {
            Span<byte> buf = stackalloc byte[4];
            RandomNumberGenerator.Fill(buf);
            return BitConverter.ToInt32(buf);
        }

        /// <summary>
        /// Generate a random int in [0, maxExclusive).
        /// </summary>
        public static int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            return Math.Abs(NextInt()) % maxExclusive;
        }

        /// <summary>
        /// Generate a random int in [minInclusive, maxExclusive).
        /// </summary>
        public static int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));

            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>
        /// Generate a random seed for deterministic replay.
        /// </summary>
        public static int GenerateSeed()
        {
            return NextInt();
        }

        /// <summary>
        /// Fill a byte array with cryptographically random bytes.
        /// </summary>
        public static void FillBytes(byte[] buffer)
        {
            RandomNumberGenerator.Fill(buffer);
        }

        /// <summary>
        /// Fisher-Yates shuffle using cryptographic randomness.
        /// "Never shuffle client-side."
        /// </summary>
        public static void Shuffle<T>(T[] array)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }

        /// <summary>
        /// Fisher-Yates shuffle for lists.
        /// </summary>
        public static void Shuffle<T>(System.Collections.Generic.List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    /// <summary>
    /// Seeded RNG for deterministic replay playback.
    /// Given the same seed, produces the same sequence.
    /// Used to verify that a replay matches what actually happened.
    /// </summary>
    public class DeterministicRNG
    {
        private readonly Random _rng;

        public DeterministicRNG(int seed)
        {
            _rng = new Random(seed);
        }

        public int NextInt() => _rng.Next();
        public int NextInt(int maxExclusive) => _rng.Next(maxExclusive);
        public int NextInt(int minInclusive, int maxExclusive) => _rng.Next(minInclusive, maxExclusive);

        /// <summary>
        /// Fisher-Yates shuffle with the seeded RNG.
        /// Produces the same result given the same seed.
        /// </summary>
        public void Shuffle<T>(T[] array)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (array[i], array[j]) = (array[j], array[i]);
            }
        }

        public void Shuffle<T>(System.Collections.Generic.List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
