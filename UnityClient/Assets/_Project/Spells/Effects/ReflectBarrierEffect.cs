// H04 반사 결계 (§14.4, §12.5 ⑦ 반사 → 쉴드 순)
//   · 상대 플레이어 스킬 피해를 전부 무효로 하고, 맞은 그 스킬을 그대로 공격자에게 다시 쓴다 (독이 오면 바로 독을 되돌려 쓴다).
//     여러 번 맞히는 스킬(총난사 · 화살 · 장판 틱)은 처음 한 번만 되돌리고 나머지는 막기만 한다 (ReflectWindow).
//   · 스킬 번호를 모르는 피해는 예전처럼 EffectRatio 만큼 작은 탄으로 되돌린다 (회피 · 반사 불가).
//   · 반사된 피해(IsReflected) · 공격자도 결계를 켠 경우는 되돌리지 않고 막기만 한다 (양쪽 모두 결계일 때 무한 반사 방지).
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
        /// <summary>같은 스킬을 다시 되돌릴 수 있는 시각 (스킬 번호 → 경기 시간)</summary>
        private readonly System.Collections.Generic.Dictionary<int, float> _nextReflect = new System.Collections.Generic.Dictionary<int, float>(4);
        /// <summary>한 번 되돌린 스킬의 나머지 타격(연사 · 장판 틱)은 이 시간(초) 동안 막기만 한다</summary>
        private const float ReflectWindow = 6f;

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
            Team attacker = context.AttackerTeam;
            if (!world.IsPlayerAlive(attacker) || attacker == self) return 0f;
            if (world.StatusEffects != null && world.StatusEffects.HasStatusEffect(world.GetPlayerId(attacker), Id)) return 0f;

            if (context.SpellId > 0)
            {
                if (_nextReflect.TryGetValue(context.SpellId, out float next) && world.Time < next) return 0f;
                _nextReflect[context.SpellId] = world.Time + ReflectWindow;
                world.PublishFx(_spellId, VfxPart.Cast, world.GetPlayerPosition(self), 1.5f, world.GetPlayerWorldColumn(self));
                world.ReflectCast(self, context.SpellId, attacker);
                return 0f;
            }

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
                TargetTeam = attacker,
                AimColumn = world.GetPlayerWorldColumn(attacker),
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
