// 투사체 계열 스킬 동작 (§14.4)
//   S01 연막    : 상대 플레이어에게 연기탄 (열 지정 · 회피 가능, 유닛을 뚫고 지나감). 실제 피해가 들어가면 EffectDuration 동안 시야 암전.
//   S03 파이어볼 : 조준 지점으로 포물선 비행 → 착탄 순간 범위 폭발 (그 순간 위치로 판정)
//   S04 화살 세례 : 조준 지점 주변(레인 폭 × 깊이 ±FieldHalfDepth)에 화살 ShotCount발이 포물선으로 낙하, 1발마다 작은 범위 피해

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public sealed class BlindShot : IProjectileHitHandler
    {
        private readonly float _duration;

        private BlindShot(float duration)
        {
            _duration = duration;
        }

        public static void Launch(SpellDefinition def, ISpellWorld world, Team casterTeam, int worldColumn, float focus)
        {
            Vector2 from = SpellDamage.Muzzle(world, casterTeam);
            world.PublishFx(def.SpellId, VfxPart.Cast, from, 1.5f, world.GetPlayerWorldColumn(casterTeam));
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = casterTeam,
                WorldColumn = worldColumn,
                From = from,
                Height = 1.5f,
                Speed = Mathf.Max(1f, def.ProjectileSpeed),
                Damage = def.BaseDamage,
                TargetUnitId = 0, // 유닛을 뚫고 상대 플레이어에게
                Dodgeable = def.Dodgeable,
                IsPlayerSkill = true,
                FocusBonus = SpellDamage.DamageFocus(def, focus),
                ArcHeight = def.ArcHeight,
                OnHit = new BlindShot(def.EffectDuration),
            });
        }

        public void OnProjectileArrived(ISpellWorld world, in ProjectileArrival arrival)
        {
            // 회피 · MISS · 쉴드 흡수 · 반사면 0 → 암전 없음 ("방어하지 않았다면")
            if (arrival.AppliedDamage <= 0f) return;
            world.BlindPlayer(TeamUtil.Opponent(arrival.CasterTeam), _duration);
        }
    }

    public static class FireballLob
    {
        public static void Launch(SpellDefinition def, ISpellWorld world, Team casterTeam, Vector2 center, int worldColumn, float focus)
        {
            Vector2 from = SpellDamage.Muzzle(world, casterTeam);
            world.PublishFx(def.SpellId, VfxPart.Cast, from, 1.6f, world.GetPlayerWorldColumn(casterTeam));
            world.PublishFx(def.SpellId, VfxPart.Telegraph, center, 0.05f, worldColumn);
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = casterTeam,
                WorldColumn = worldColumn,
                From = from,
                Height = 1.8f,
                Speed = Mathf.Max(1f, def.ProjectileSpeed),
                Damage = def.BaseDamage,
                IsPlayerSkill = true,
                FocusBonus = SpellDamage.DamageFocus(def, focus),
                Mode = ProjectileMode.Point,
                TargetPoint = center,
                TargetHeight = 0.3f,
                ArcHeight = def.ArcHeight,
                SuppressImpactFx = true, // 폭발은 처리기가 (반경 기준) 발행
                OnHit = new SplashOnArrive(def.SpellId, def.BaseDamage, def.Radius, true),
            });
        }
    }

    public sealed class ArrowVolleyRoutine : ISpellRoutine
    {
        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly Vector2 _center;
        private readonly int _worldColumn;
        private readonly float _focus;
        private readonly SplashOnArrive _onHit;
        private readonly uint _seed;
        private int _fired;
        private float _timer;

        public ArrowVolleyRoutine(SpellDefinition def, ISpellWorld world, Team team, Vector2 center, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _center = center;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            _onHit = new SplashOnArrive(def.SpellId, def.BaseDamage, def.Radius, false);
            _seed = (uint)(world.Time * 1000f) * 2654435761u + (uint)team * 97u + (uint)worldColumn;
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.6f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Telegraph, center, 0.05f, worldColumn);
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _timer -= deltaTime;
            while (_timer <= 0f && _fired < _def.ShotCount)
            {
                FireOne(world, _fired);
                _fired++;
                _timer += Mathf.Max(0.01f, _def.ShotInterval);
            }
            return _fired < _def.ShotCount;
        }

        private void FireOne(ISpellWorld world, int index)
        {
            var arena = world.Arena;
            float halfU = arena.LaneHalfWidth * 0.8f;
            float u = arena.ColumnU(_worldColumn) + (SpellDamage.Hash01(_seed + (uint)index * 7919u) * 2f - 1f) * halfU;
            float v = _center.y + (SpellDamage.Hash01(_seed + (uint)index * 104729u + 13u) * 2f - 1f) * _def.FieldHalfDepth;
            Vector2 target = arena.ClampToField(new Vector2(u, v));

            // 활시위처럼 시전자 양옆에서 조금씩 다른 위치로 발사
            Vector2 from = SpellDamage.Muzzle(world, _team, 0.4f);
            from.x += (index % 3 - 1) * 0.35f;
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = _def.SpellId,
                CasterTeam = _team,
                WorldColumn = _worldColumn,
                From = from,
                Height = 2.0f,
                Speed = Mathf.Max(1f, _def.ProjectileSpeed),
                Damage = _def.BaseDamage,
                IsPlayerSkill = true,
                FocusBonus = _focus,
                Mode = ProjectileMode.Point,
                TargetPoint = target,
                TargetHeight = 0.05f,
                ArcHeight = _def.ArcHeight,
                OnHit = _onHit, // Impact VFX(먼지)는 시스템이 착탄 지점에 자동 발행
            });
        }
    }
}
