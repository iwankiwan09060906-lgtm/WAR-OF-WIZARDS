// §20.2 기본 봇 두뇌 — 유일한 봇 두뇌 (VLM · 로컬 모델을 쓰던 AI 전략 두뇌는 삭제)
//   시전: 정해진 간격마다 Ready 슬롯 중 랜덤 1개 (시연은 19종 로테이션이라 결국 19종을 랜덤으로 쓴다), 없으면 다음 간격까지 대기
//   성공률: BotPlayer 가 시전 준비 중 BotConfig.CastFailChance(기본 40%) 확률로 그리기를 실패한다 → 성공 60%
//   조준: 상대(열) · 깊이 · 소환 · 설치 위치 모두 랜덤 (다인전은 상대 5명 중, 소환 · 설치는 조준선의 PlacementMaxDepth 까지)
//   이동: MoveIntervalMin ~ Max 랜덤 간격으로 랜덤 칸

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
                    bool placement = def != null && (def.Targeting == SpellTargeting.Summon || def.Targeting == SpellTargeting.Structure);
                    float maxPlace = obs.PlacementMaxDepth > 0f ? obs.PlacementMaxDepth : 1f;
                    cmd.HasCastRequest = true;
                    cmd.CastRequest = new SpellCastRequest
                    {
                        SpellId = spellId,
                        SlotIndex = slot,
                        TargetColumn = (Column)PickBit(placement ? obs.SpawnColumnMask : obs.ColumnMask, 3),
                        TargetDepth = def != null && def.Targeting == SpellTargeting.Area
                            ? _rng.Range(_config.AreaDepthMin, _config.AreaDepthMax)
                            : placement ? _rng.Range(0.1f, maxPlace) : 0f,
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

        /// <summary>mask 의 켜진 비트 중 하나 (없으면 0 ~ fallbackCount-1 중 하나)</summary>
        private int PickBit(byte mask, int fallbackCount)
        {
            if (mask == 0) return _rng.Range(0, fallbackCount);
            int count = 0;
            for (int i = 0; i < 8; i++) if ((mask & (1 << i)) != 0) count++;
            int k = _rng.Range(0, count);
            for (int i = 0; i < 8; i++)
                if ((mask & (1 << i)) != 0 && k-- == 0) return i;
            return 0;
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
