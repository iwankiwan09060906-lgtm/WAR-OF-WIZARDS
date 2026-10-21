// H04 반사 결계 (§14.4, §12.5 ⑦ 반사 → 쉴드 순)
//   · 상대 플레이어 스킬 피해를 전부 무효로 하고, 그 피해의 EffectRatio(80%)를 공격자에게 되돌린다.
//   · 되돌리는 피해는 작은 탄으로 날아가며 회피 · 반사 불가 (무조건 명중), 도착 시 파이프라인을 통과한다.
//   · 반사된 피해(IsReflected)는 다시 반사하지 않고 그냥 막는다 (양쪽 모두 결계일 때 무한 반사 방지).
//   · 미니언 · 소환수 공격은 막지 않는다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Effects
{
    public sealed class ReflectBarrierEffect : IStatusEffect
    {
        public const string Id = "H04_Reflect";

        private readonly int _spellId;
        private readonly float _ratio;
        private readonly float _speed;
        private float _remaining;

        public ReflectBarrierEffect(SpellDefinition definition, float durationScale = 1f)
        {
            _spellId = definition.SpellId;
            _ratio = Mathf.Clamp(definition.EffectRatio, 0f, 2f);
            _speed = Mathf.Max(5f, definition.ProjectileSpeed);
            _remaining = Mathf.Max(0.1f, definition.EffectDuration * Mathf.Max(1f, durationScale));
        }

        public string EffectId => Id;
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

        public float AbsorbIncomingDamage(int targetId, float amount, in DamageContext context, ISpellWorld world)
        {
            if (amount <= 0f || !context.IsPlayerSkill || !TeamUtil.IsPlayerId(targetId)) return amount;
            if (context.IsReflected) return 0f; // 되돌아온 피해는 막기만

            Team self = TeamUtil.TeamOfPlayerId(targetId);
            Team attacker = TeamUtil.Opponent(self);
            if (!world.IsPlayerAlive(attacker)) return 0f;

            // 원래 스킬 위력(진영 배율 · 집중 포함, 타워 감쇄 전) 기준으로 되돌린다
            float focus = context.FocusBonusMultiplier > 0f ? context.FocusBonusMultiplier : 1f;
            float reflected = context.BaseDamage * focus * _ratio;
            Vector2 from = world.GetPlayerPosition(self);
            world.PublishFx(_spellId, VfxPart.Cast, from, 1.5f, world.GetPlayerWorldColumn(self));
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = _spellId,
                CasterTeam = self,
                WorldColumn = world.GetPlayerWorldColumn(attacker), // 지금 서 있는 열 → 회피 판정 없음
                From = from,
                Height = 1.5f,
                Speed = _speed,
                Damage = reflected,
                TargetUnitId = 0,
                Dodgeable = false,
                IsPlayerSkill = true,
                FocusBonus = 1f,
                Mode = ProjectileMode.Auto,
                IsReflected = true,
            });
            return 0f;
        }
    }
}
