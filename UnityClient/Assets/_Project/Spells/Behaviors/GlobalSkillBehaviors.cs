// 전역 · 대상 지정 스킬 동작 (§14.4)
//   H01 프리즈 : 3열 전장 전체 — 상대 미니언 · 소환수 · 설치물 · 타워를 EffectDuration 동안 정지 + 작은 피해. 상대 플레이어는 멈추지 않음
//   H03 흡혈   : 상대 플레이어에게 무조건 명중(회피 불가 — 쉴드 · 반사 결계로만 막힘). 실제 피해 × EffectRatio 만큼의 생명력이
//               EffectDuration(2초)에 걸쳐 시전자에게 날아와 도착하면 회복
//   H05 분노   : 3열을 덮는 장판 — EffectDuration 동안 아군 유닛 공격력 · 공격 속도 · 이동 속도 · 방어력 증가 (그동안 새로 나온 유닛 포함)

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public static class FreezeAll
    {
        private static readonly List<int> s_buffer = new List<int>(96);

        /// <summary>프리즈 장판 표시 위치: 3열 × 깊이 5줄</summary>
        public static readonly float[] FieldRows = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };

        public static void Apply(SpellDefinition def, ISpellWorld world, Team casterTeam, float focus)
        {
            Team enemy = TeamUtil.Opponent(casterTeam);
            float duration = def.EffectDuration * SpellDamage.DurationFocus(def, focus);
            float dmgFocus = SpellDamage.DamageFocus(def, focus);
            var arena = world.Arena;

            world.PublishFx(def.SpellId, VfxPart.Cast, world.GetPlayerPosition(casterTeam), 1.5f, world.GetPlayerWorldColumn(casterTeam));
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < FieldRows.Length; r++)
                    world.PublishFx(def.SpellId, VfxPart.Field, new Vector2(arena.ColumnU(c), arena.FieldLength * FieldRows[r]), 0f, c);

            world.CollectTeamUnits(enemy, true, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                world.FreezeUnit(id, duration);
                if (def.BaseDamage > 0f) SpellDamage.HitUnit(world, def.SpellId, casterTeam, id, def.BaseDamage, dmgFocus);
            }
            world.FreezeTower(enemy, duration);
        }
    }

    public sealed class DrainLife : IProjectileHitHandler
    {
        private readonly int _spellId;

        private DrainLife(int spellId)
        {
            _spellId = spellId;
        }

        public static void Apply(SpellDefinition def, ISpellWorld world, Team casterTeam, float focus)
        {
            Team enemy = TeamUtil.Opponent(casterTeam);
            if (!world.IsPlayerAlive(enemy)) return;
            Vector2 enemyPos = world.GetPlayerPosition(enemy);
            int enemyColumn = world.GetPlayerWorldColumn(enemy);
            world.PublishFx(def.SpellId, VfxPart.Telegraph, world.GetPlayerPosition(casterTeam), 1.5f, world.GetPlayerWorldColumn(casterTeam));
            world.PublishFx(def.SpellId, VfxPart.Impact, enemyPos, 1.4f, enemyColumn);

            float applied = SpellDamage.HitEnemyPlayer(world, def.SpellId, casterTeam, def.BaseDamage, SpellDamage.DamageFocus(def, focus));
            if (applied <= 0f) return; // 쉴드 · 반사 · MISS → 빼앗을 생명력 없음

            Vector2 self = world.GetPlayerPosition(casterTeam);
            float distance = Vector2.Distance(enemyPos, self);
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = casterTeam,
                WorldColumn = enemyColumn,
                From = enemyPos,
                Height = 1.4f,
                Speed = Mathf.Max(1f, distance / Mathf.Max(0.2f, def.EffectDuration)),
                Damage = applied * def.EffectRatio, // 회복량
                IsPlayerSkill = true,
                FocusBonus = 1f,
                Mode = ProjectileMode.OwnPlayer,
                TargetHeight = 1.3f,
                ArcHeight = 2.5f,
                SuppressImpactFx = true,
                OnHit = new DrainLife(def.SpellId),
            });
        }

        public void OnProjectileArrived(ISpellWorld world, in ProjectileArrival arrival)
        {
            if (!world.IsPlayerAlive(arrival.CasterTeam)) return;
            world.Heal(world.GetPlayerId(arrival.CasterTeam), arrival.Damage, _spellId);
            world.PublishFx(_spellId, VfxPart.Loop, world.GetPlayerPosition(arrival.CasterTeam), 0.05f, world.GetPlayerWorldColumn(arrival.CasterTeam));
        }
    }

    public sealed class RageRoutine : ISpellRoutine
    {
        private const float RefreshInterval = 0.25f;
        private static readonly List<int> s_buffer = new List<int>(64);

        /// <summary>분노 장판 표시 위치 (전장 길이 비율)</summary>
        public static readonly float[] FieldRows = { 0.2f, 0.5f, 0.8f };

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly float _duration;
        private float _elapsed;
        private float _refresh;

        public RageRoutine(SpellDefinition def, ISpellWorld world, Team team, float focus)
        {
            _def = def;
            _team = team;
            _duration = def.EffectDuration * SpellDamage.DurationFocus(def, focus);
            var arena = world.Arena;
            world.PublishFx(def.SpellId, VfxPart.Cast, world.GetPlayerPosition(team), 1.5f, world.GetPlayerWorldColumn(team));
            for (int i = 0; i < FieldRows.Length; i++)
                world.PublishFx(def.SpellId, VfxPart.Field, new Vector2(0f, arena.FieldLength * FieldRows[i]), 0.02f, 1);
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _elapsed += deltaTime;
            _refresh -= deltaTime;
            if (_refresh <= 0f)
            {
                _refresh += RefreshInterval;
                float remaining = _duration - _elapsed;
                if (remaining > 0f)
                {
                    world.CollectTeamUnits(_team, true, s_buffer);
                    for (int i = 0; i < s_buffer.Count; i++)
                        world.RageUnit(s_buffer[i], remaining, _def.AttackMultiplier, _def.AttackSpeedMultiplier,
                                       _def.MoveSpeedMultiplier, _def.IncomingDamageMultiplier);
                }
            }
            return _elapsed < _duration;
        }
    }
}
