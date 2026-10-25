// 고위력 스킬 동작 (§14.4) — 게임당 3회라 일반 스킬보다 훨씬 강하다
//   H01 프리즈 : 3열 전장 전체 — 상대 미니언 · 소환수 · 설치물 · 타워를 EffectDuration 동안 정지 + 피해. 상대 플레이어는 멈추지 않음
//   H03 흡혈   : 조준한 레인 공격. 상대 플레이어가 그 레인에 서 있고 피해가 들어가면 실제 피해 × EffectRatio 만큼 회복.
//               플레이어를 맞히지 못하면(다른 레인 · 쉴드 · 반사 · MISS) 그 레인의 적 유닛 · 소환된 설치물 전부에게서 빼앗는다
//               (실제 피해 합 × EffectRatio × LaneHealRatio). 생명력은 EffectDuration에 걸쳐 시전자에게 날아와 도착하면 회복
//   H05 분노   : 파이어볼처럼 조준한 깊이에 3열을 가로지르는 띠 장판 (띠 폭 = 아레나의 타워 ~ 넥서스 거리).
//               EffectDuration 동안 장판에 들어온 아군 유닛은 장판이 끝날 때까지 공격력 · 공격 속도 · 이동 속도 · 방어력 증가

using System.Collections.Generic;
using SpellboundVR.Arena;
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
        /// <summary>플레이어 대신 레인 유닛 · 설치물에게서 빼앗을 때의 회복 비율 (EffectRatio에 곱함)</summary>
        public const float LaneHealRatio = 0.5f;
        /// <summary>레인 흡수 때 Impact 연출을 붙이는 최대 대상 수</summary>
        private const int MaxImpactFx = 8;
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly int _spellId;

        private DrainLife(int spellId)
        {
            _spellId = spellId;
        }

        public static void Apply(SpellDefinition def, ISpellWorld world, Team casterTeam, int worldColumn, float focus)
        {
            Team enemy = TeamUtil.Opponent(casterTeam);
            float dmgFocus = SpellDamage.DamageFocus(def, focus);
            world.PublishFx(def.SpellId, VfxPart.Telegraph, world.GetPlayerPosition(casterTeam), 1.5f, world.GetPlayerWorldColumn(casterTeam));

            // ① 조준한 레인에 상대 플레이어가 있으면 플레이어에게서
            if (world.IsPlayerAlive(enemy) && world.GetPlayerWorldColumn(enemy) == worldColumn)
            {
                Vector2 enemyPos = world.GetPlayerPosition(enemy);
                world.PublishFx(def.SpellId, VfxPart.Impact, enemyPos, 1.4f, worldColumn);
                float applied = SpellDamage.HitEnemyPlayer(world, def.SpellId, casterTeam, def.BaseDamage, dmgFocus);
                if (applied > 0f)
                {
                    SendLife(def, world, casterTeam, enemyPos, 1.4f, worldColumn, applied * def.EffectRatio);
                    return;
                }
            }

            // ② 플레이어를 못 맞혔으면 그 레인의 적 유닛 · 소환된 설치물(터렛 · 소환탑)에게서
            world.CollectEnemyUnitsInColumn(casterTeam, worldColumn, s_buffer);
            float total = 0f;
            Vector2 sum = Vector2.zero;
            int hits = 0;
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                if (!world.TryGetUnitInfo(id, out Vector2 pos, out _, out _)) continue;
                float applied = SpellDamage.HitUnit(world, def.SpellId, casterTeam, id, def.BaseDamage, dmgFocus);
                if (applied <= 0f) continue;
                total += applied;
                sum += pos;
                if (hits++ < MaxImpactFx) world.PublishFx(def.SpellId, VfxPart.Impact, pos, 1.0f, worldColumn);
            }
            if (hits == 0) return;
            SendLife(def, world, casterTeam, sum / hits, 1.0f, worldColumn, total * def.EffectRatio * LaneHealRatio);
        }

        /// <summary>빼앗은 생명력이 EffectDuration에 걸쳐 시전자에게 날아온다</summary>
        private static void SendLife(SpellDefinition def, ISpellWorld world, Team casterTeam, Vector2 from, float height, int column, float heal)
        {
            Vector2 self = world.GetPlayerPosition(casterTeam);
            float distance = Vector2.Distance(from, self);
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = casterTeam,
                WorldColumn = column,
                From = from,
                Height = height,
                Speed = Mathf.Max(1f, distance / Mathf.Max(0.2f, def.EffectDuration)),
                Damage = heal, // 회복량
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

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly float _duration;
        private readonly float _vMin;
        private readonly float _vMax;
        private float _elapsed;
        private float _refresh;

        /// <summary>띠 반폭 (m) — 아레나의 타워 ~ 넥서스 거리의 절반. AimPointer 표시도 같은 값을 쓴다</summary>
        public static float HalfDepth(SpellDefinition def, ArenaGeometry arena)
        {
            float gap = Mathf.Abs(arena.TowerDepth - arena.NexusDepth);
            return gap > 0.5f ? gap * 0.5f : Mathf.Max(0.5f, def.FieldHalfDepth);
        }

        public RageRoutine(SpellDefinition def, ISpellWorld world, Team team, Vector2 center, float focus)
        {
            _def = def;
            _team = team;
            _duration = def.EffectDuration * SpellDamage.DurationFocus(def, focus);
            float half = HalfDepth(def, world.Arena);
            _vMin = center.y - half;
            _vMax = center.y + half;
            world.PublishFx(def.SpellId, VfxPart.Cast, world.GetPlayerPosition(team), 1.5f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Field, new Vector2(0f, center.y), 0.02f, 1); // 3열 폭 장판 → 전장 가운데
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
                    {
                        if (!world.TryGetUnitInfo(s_buffer[i], out Vector2 pos, out _, out _)) continue;
                        if (pos.y < _vMin || pos.y > _vMax) continue;
                        // 장판에 들어온 유닛은 장판이 끝날 때까지 강화 (나가도 유지)
                        world.RageUnit(s_buffer[i], remaining, _def.AttackMultiplier, _def.AttackSpeedMultiplier,
                                       _def.MoveSpeedMultiplier, _def.IncomingDamageMultiplier);
                    }
                }
            }
            return _elapsed < _duration;
        }
    }
}
