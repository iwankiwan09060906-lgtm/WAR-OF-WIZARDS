// 상태 효과 동작 공용 틀. IStatusEffectHost(계약, ㅈㅈㅇ)가 인스턴스를 보관 · 틱 · 제거하고,
// 동작(쉴드 흡수 · 회복 등)은 이 인터페이스 구현(ㅊㄱㅇ, Spells/Effects)이 담당한다.
// §12.5 ⑦ 대상 방어 효과 단계에서 AbsorbIncomingDamage가 호출된다.

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;

namespace SpellboundVR.Spells.Effects
{
    public interface IStatusEffect
    {
        string EffectId { get; }

        /// <summary>남은 시간 (초)</summary>
        float Remaining { get; }

        bool IsExpired { get; }

        void OnApplied(int targetId, ISpellWorld world);

        void Tick(int targetId, float deltaTime, ISpellWorld world);

        void OnRemoved(int targetId, ISpellWorld world);

        /// <summary>같은 EffectId가 다시 걸릴 때 호출 (지속 시간 · 수치 갱신)</summary>
        void Refresh(IStatusEffect newer);

        /// <summary>
        /// §12.5 ⑦ — 들어오는 피해를 가공한다 (반사 → 쉴드 흡수 순). 남은 피해를 반환한다.
        /// </summary>
        float AbsorbIncomingDamage(int targetId, float amount, in DamageContext context, ISpellWorld world);
    }

    /// <summary>동작 없는 시간 제한 태그 (IStatusEffectHost.ApplyStatusEffect 기본 구현용)</summary>
    public sealed class TimedTagEffect : IStatusEffect
    {
        private float _remaining;

        public TimedTagEffect(string effectId, float duration)
        {
            EffectId = effectId;
            _remaining = duration;
        }

        public string EffectId { get; }
        public float Remaining => _remaining;
        public bool IsExpired => _remaining <= 0f;

        public void OnApplied(int targetId, ISpellWorld world) { }

        public void Tick(int targetId, float deltaTime, ISpellWorld world)
        {
            _remaining -= deltaTime;
        }

        public void OnRemoved(int targetId, ISpellWorld world) { }

        public void Refresh(IStatusEffect newer)
        {
            if (newer.Remaining > _remaining) _remaining = newer.Remaining;
        }

        public float AbsorbIncomingDamage(int targetId, float amount, in DamageContext context, ISpellWorld world) => amount;
    }
}
