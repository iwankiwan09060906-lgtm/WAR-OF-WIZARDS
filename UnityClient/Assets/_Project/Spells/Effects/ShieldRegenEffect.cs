// S08 방어 쉴드 — 피해 흡수, 일정 시간 무피격 시 HP 회복 (§14.3)
//   · ShieldAmount 만큼 피해를 흡수한다 (§12.5 ⑦ 쉴드 흡수).
//   · 쉴드가 남아 있는 동안 RegenDelay 초 동안 피격이 없으면 초당 RegenPerSecond 회복.
//   · EffectDuration이 끝나거나 쉴드가 0이 되면 사라진다.

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Effects
{
    public sealed class ShieldRegenEffect : IStatusEffect
    {
        private readonly int _spellId;
        private float _remaining;
        private float _shield;
        private float _regenDelay;
        private float _regenPerSecond;
        private float _timeSinceHit;

        public ShieldRegenEffect(SpellDefinition definition)
        {
            _spellId = definition.SpellId;
            EffectId = string.IsNullOrEmpty(definition.StatusEffectId) ? "S08_Shield" : definition.StatusEffectId;
            _remaining = Mathf.Max(0.1f, definition.EffectDuration);
            _shield = Mathf.Max(0f, definition.ShieldAmount);
            _regenDelay = Mathf.Max(0f, definition.RegenDelay);
            _regenPerSecond = Mathf.Max(0f, definition.RegenPerSecond);
            _timeSinceHit = 0f;
        }

        public string EffectId { get; }
        public float Remaining => _remaining;
        public float ShieldPoints => _shield;
        public bool IsExpired => _remaining <= 0f || _shield <= 0f;

        public void OnApplied(int targetId, ISpellWorld world)
        {
            _timeSinceHit = 0f;
        }

        public void Tick(int targetId, float deltaTime, ISpellWorld world)
        {
            _remaining -= deltaTime;
            _timeSinceHit += deltaTime;
            if (_remaining > 0f && _regenPerSecond > 0f && _timeSinceHit >= _regenDelay)
            {
                world.Heal(targetId, _regenPerSecond * deltaTime, _spellId);
            }
        }

        public void OnRemoved(int targetId, ISpellWorld world) { }

        public void Refresh(IStatusEffect newer)
        {
            if (newer is ShieldRegenEffect s)
            {
                _remaining = Mathf.Max(_remaining, s._remaining);
                _shield = Mathf.Max(_shield, s._shield);
                _regenDelay = s._regenDelay;
                _regenPerSecond = s._regenPerSecond;
                _timeSinceHit = 0f;
            }
        }

        public float AbsorbIncomingDamage(int targetId, float amount, in DamageContext context, ISpellWorld world)
        {
            if (amount <= 0f) return 0f;
            _timeSinceHit = 0f;
            float absorbed = Mathf.Min(_shield, amount);
            _shield -= absorbed;
            return amount - absorbed;
        }
    }
}
