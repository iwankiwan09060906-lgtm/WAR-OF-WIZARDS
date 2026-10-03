// §12.4 명중률 — 대상 1개마다 서버 판정, 빗나가면 0 + MISS. 난수는 서버 틱 기반 결정적 방식.

using SpellboundVR.Utils;

namespace SpellboundVR.Combat
{
    public sealed class AccuracyRoll
    {
        private uint _matchSeed;
        private uint _counter;

        public void Reset(uint matchSeed)
        {
            _matchSeed = matchSeed;
            _counter = 0;
        }

        /// <summary>true = 명중</summary>
        public bool Roll(float accuracy, uint tick, int targetId)
        {
            if (accuracy >= 1f) return true;
            if (accuracy <= 0f) return false;
            _counter++;
            float r = DeterministicRandom.Hash01(_matchSeed ^ tick, (uint)targetId, _counter);
            return r < accuracy;
        }
    }
}
