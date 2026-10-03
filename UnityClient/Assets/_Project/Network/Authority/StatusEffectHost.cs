// IStatusEffectHost 실제 구현 (§25.1 제공자 ㅈㅈㅇ)
// "일정 시간 붙어있는 효과"를 붙이고 · 갱신하고 · 떼는 공용 틀. 효과 동작은 IStatusEffect가 담당한다.
// 서버 틱에서만 호출되며, 리스트는 미리 용량을 잡아 전투 중 할당을 피한다.

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Spells.Effects;
using SpellboundVR.Spells.Executor;

namespace SpellboundVR.Network
{
    public sealed class StatusEffectHost : IStatusEffectHost
    {
        private struct Entry
        {
            public int TargetId;
            public IStatusEffect Effect;
        }

        private readonly List<Entry> _entries = new List<Entry>(64);
        private ISpellWorld _world;

        public void Bind(ISpellWorld world)
        {
            _world = world;
        }

        public int Count => _entries.Count;

        // ── IStatusEffectHost (계약) ─────────────────────────

        public void ApplyStatusEffect(int targetId, string effectId, float duration)
        {
            Add(targetId, new TimedTagEffect(effectId, duration));
        }

        public void RemoveStatusEffect(int targetId, string effectId)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.TargetId == targetId && e.Effect.EffectId == effectId)
                {
                    _entries.RemoveAt(i);
                    e.Effect.OnRemoved(targetId, _world);
                }
            }
        }

        public bool HasStatusEffect(int targetId, string effectId)
        {
            return TryGet(targetId, effectId, out _);
        }

        // ── 확장 ─────────────────────────────────────────────

        public void Add(int targetId, IStatusEffect effect)
        {
            if (effect == null) return;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.TargetId == targetId && e.Effect.EffectId == effect.EffectId)
                {
                    e.Effect.Refresh(effect);
                    return;
                }
            }
            _entries.Add(new Entry { TargetId = targetId, Effect = effect });
            effect.OnApplied(targetId, _world);
        }

        public bool TryGet(int targetId, string effectId, out IStatusEffect effect)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.TargetId == targetId && e.Effect.EffectId == effectId && !e.Effect.IsExpired)
                {
                    effect = e.Effect;
                    return true;
                }
            }
            effect = null;
            return false;
        }

        public void Tick(float deltaTime)
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                e.Effect.Tick(e.TargetId, deltaTime, _world);
            }
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                var e = _entries[i];
                if (e.Effect.IsExpired)
                {
                    _entries.RemoveAt(i);
                    e.Effect.OnRemoved(e.TargetId, _world);
                }
            }
        }

        /// <summary>§12.5 ⑦ — 대상에게 붙은 효과가 순서대로 피해를 가공한다.</summary>
        public float AbsorbIncoming(int targetId, float amount, in DamageContext context)
        {
            for (int i = 0; i < _entries.Count && amount > 0f; i++)
            {
                var e = _entries[i];
                if (e.TargetId != targetId || e.Effect.IsExpired) continue;
                amount = e.Effect.AbsorbIncomingDamage(targetId, amount, context, _world);
            }
            return amount < 0f ? 0f : amount;
        }

        public void ClearTarget(int targetId)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].TargetId == targetId) _entries.RemoveAt(i);
            }
        }

        public void Clear()
        {
            _entries.Clear();
        }
    }
}
