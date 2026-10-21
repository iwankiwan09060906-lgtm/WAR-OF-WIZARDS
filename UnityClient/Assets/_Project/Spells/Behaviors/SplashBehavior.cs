// 지점 범위 피해 (S03 파이어볼 폭발 · S04 화살 1발 · S06 낙뢰 1회)
//   · 적용 순간의 서버 위치로 판정 (지점 비행 스킬은 착탄 순간 — 그 사이 열을 옮기면 피한다)
//   · 반경 안 적 유닛 전부 + 반경 안에 서 있는 상대 플레이어 (구조물 · 설치물 제외, Rule 5)

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public static class SplashBehavior
    {
        /// <summary>플레이어 몸 반경 (m) — 범위 판정 시 더해준다</summary>
        public const float PlayerBodyRadius = SpellDamage.PlayerBodyRadius;

        private static readonly List<int> s_buffer = new List<int>(48);

        public static void Apply(SpellDefinition def, ISpellWorld world, Team casterTeam, Vector2 center, int worldColumn,
                                 float focus = 1f, bool publishImpact = true)
        {
            Apply(def.SpellId, def.BaseDamage, def.Radius, world, casterTeam, center, worldColumn, focus, publishImpact);
        }

        public static void Apply(int spellId, float damage, float radius, ISpellWorld world, Team casterTeam, Vector2 center,
                                 int worldColumn, float focus, bool publishImpact)
        {
            if (publishImpact) world.PublishFx(spellId, VfxPart.Impact, center, 0f, worldColumn);

            world.CollectEnemyUnitsInRadius(casterTeam, center, radius, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
                SpellDamage.HitUnit(world, spellId, casterTeam, s_buffer[i], damage, focus);

            if (SpellDamage.EnemyPlayerInRadius(world, casterTeam, center, radius))
                SpellDamage.HitEnemyPlayer(world, spellId, casterTeam, damage, focus);
        }
    }

    /// <summary>지점 비행 투사체 착탄 → 범위 피해 (S03 · S04)</summary>
    public sealed class SplashOnArrive : IProjectileHitHandler
    {
        private readonly int _spellId;
        private readonly float _damage;
        private readonly float _radius;
        private readonly bool _publishImpact;

        public SplashOnArrive(int spellId, float damage, float radius, bool publishImpact)
        {
            _spellId = spellId;
            _damage = damage;
            _radius = radius;
            _publishImpact = publishImpact;
        }

        public void OnProjectileArrived(ISpellWorld world, in ProjectileArrival arrival)
        {
            SplashBehavior.Apply(_spellId, _damage, _radius, world, arrival.CasterTeam, arrival.Position, arrival.WorldColumn,
                                 arrival.FocusBonus, _publishImpact);
        }
    }
}
