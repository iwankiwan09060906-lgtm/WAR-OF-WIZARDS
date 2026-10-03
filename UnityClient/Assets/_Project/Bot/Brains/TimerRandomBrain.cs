// §20.2 기본 봇 두뇌 (IBotBrain Mock 겸 실사용)
//   시전: 정해진 간격마다 Ready 슬롯 중 랜덤 1개, 없으면 다음 간격까지 대기
//   조준: 열 · 깊이 · 소환 라인 · 설치 칸 모두 랜덤
//   이동: 5 ~ 20초 랜덤 간격으로 랜덤 칸

using SpellboundVR.Contracts;
using SpellboundVR.Deck;
using SpellboundVR.Spells;
using SpellboundVR.Utils;

namespace SpellboundVR.Bot
{
    public sealed class TimerRandomBrain : IBotBrain
    {
        private readonly int[] _deck;
        private readonly SpellCatalog _catalog;
        private readonly BotConfig _config;
        private readonly DeterministicRandom _rng;
        private float _nextCastTime = -1f;
        private float _nextMoveTime = -1f;
        private float _lastTime;

        public TimerRandomBrain(int[] deck, SpellCatalog catalog, BotConfig config, uint seed)
        {
            _deck = deck;
            _catalog = catalog;
            _config = config;
            _rng = new DeterministicRandom(seed);
        }

        public void Tick(in BotObservation obs, ref BotCommand cmd)
        {
            float t = obs.MatchTimeSeconds;
            if (t < _lastTime) ResetTimers(); // 새 경기 (경기 시간이 되감김)
            _lastTime = t;
            if (_nextCastTime < 0f) _nextCastTime = t + _rng.Range(_config.CastIntervalMin, _config.CastIntervalMax);
            if (_nextMoveTime < 0f) _nextMoveTime = t + _rng.Range(_config.MoveIntervalMin, _config.MoveIntervalMax);

            if (t >= _nextCastTime)
            {
                _nextCastTime = t + _rng.Range(_config.CastIntervalMin, _config.CastIntervalMax);
                int slot = PickReadySlot(obs.SelfSlotReadyMask);
                if (slot >= 0)
                {
                    int spellId = _deck[slot];
                    var def = _catalog.Get(spellId);
                    cmd.HasCastRequest = true;
                    cmd.CastRequest = new SpellCastRequest
                    {
                        SpellId = spellId,
                        SlotIndex = slot,
                        TargetColumn = (Column)_rng.Range(0, 3),
                        TargetDepth = def != null && def.Targeting == SpellTargeting.Area
                            ? _rng.Range(_config.AreaDepthMin, _config.AreaDepthMax)
                            : 0f,
                    };
                }
            }

            if (t >= _nextMoveTime)
            {
                _nextMoveTime = t + _rng.Range(_config.MoveIntervalMin, _config.MoveIntervalMax);
                int current = (int)obs.SelfColumn;
                int target = _rng.Range(0, 3);
                if (target == current) target = current == 1 ? (_rng.NextFloat() < 0.5f ? 0 : 2) : 1;
                cmd.HasMoveRequest = true;
                cmd.MoveRequest = new MoveRequest { Direction = (sbyte)(target > current ? 1 : -1) };
            }
        }

        /// <summary>경기 재시작 시 타이머 초기화</summary>
        public void ResetTimers()
        {
            _nextCastTime = -1f;
            _nextMoveTime = -1f;
        }

        private int PickReadySlot(byte mask)
        {
            int count = 0;
            for (int i = 0; i < DeckData.SlotCount; i++) if ((mask & (1 << i)) != 0) count++;
            if (count == 0) return -1;
            int k = _rng.Range(0, count);
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                if ((mask & (1 << i)) == 0) continue;
                if (k-- == 0) return i;
            }
            return -1;
        }
    }
}
