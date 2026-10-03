// §12.4 명중률 — "난수는 서버 틱 기반 결정적 방식"
// 할당 없는 xorshift32 난수. 같은 시드 → 같은 결과(서버 재현 · 봇 테스트용).

namespace SpellboundVR.Utils
{
    public sealed class DeterministicRandom
    {
        private uint _state;

        public DeterministicRandom(uint seed)
        {
            Reseed(seed);
        }

        public void Reseed(uint seed)
        {
            _state = seed == 0 ? 0x9E3779B9u : seed;
        }

        public uint NextUInt()
        {
            uint x = _state;
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            _state = x;
            return x;
        }

        /// <summary>[0, 1)</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1f / 16777216f);
        }

        /// <summary>[min, max)</summary>
        public float Range(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }

        /// <summary>[min, maxExclusive)</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextUInt() % (uint)(maxExclusive - min));
        }

        /// <summary>틱 · 대상 등으로부터 결정적 시드를 만든다 (상태를 바꾸지 않는 1회성 판정용).</summary>
        public static float Hash01(uint a, uint b, uint c)
        {
            uint h = a * 0x8DA6B343u ^ b * 0xD8163841u ^ c * 0xCB1AB31Fu;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h >> 8) * (1f / 16777216f);
        }
    }
}
