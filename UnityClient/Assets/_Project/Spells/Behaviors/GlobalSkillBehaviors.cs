// 고위력 스킬 동작 (§14.4) — 게임당 3회라 일반 스킬보다 훨씬 강하다
//   H01 프리즈 : 시전자 자리에서 퍼지는 얼음 물결 — 닿은 상대 미니언 · 소환수 · 설치물 · 타워를 EffectDuration 동안 정지 + 피해.
//               상대 플레이어는 멈추지 않고, 물결이 닿을 때 한 번 피해 (회피 불가 — 쉴드 · 반사 · 타워 감쇄는 그대로)
//   H03 흡혈   : 상대 플레이어(서 있는 칸과 상관없이 확정 명중 — 쉴드 · 반사로만 막힘)와 조준한 레인의 적 유닛 · 소환된 설치물 전부에게서
//               동시에 빼앗는다. 회복 = 플레이어 실제 피해 × EffectRatio + 레인 실제 피해 합 × EffectRatio × LaneHealRatio.
//               대상마다 Impact(가는 흡혈 선)가 이어지고, 생명력은 EffectDuration에 걸쳐 시전자에게 날아와 도착하면 회복
//   H05 분노   : 파이어볼처럼 조준한 깊이에 띠 장판 (1:1 은 3열 전체, 개인전은 그 레인 안 · 띠 폭 = 아레나의 타워 ~ 넥서스 거리).
//               EffectDuration 동안 장판에 들어온 아군 유닛은 장판이 끝날 때까지 공격력 · 공격 속도 · 이동 속도 · 방어력 증가

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    /// <summary>
    /// H01 — 시전자 자리에서 얼음 물결이 퍼져 나간다. 물결이 닿은 상대 유닛 · 설치물부터 EffectDuration 동안 얼고 피해,
    /// 그 진영의 타워 자리에 닿으면 타워도 언다. 물결이 지나간 레인에는 얼음 장판(Field)이 차례로 깔린다.
    /// </summary>
    public sealed class FreezeWave : ISpellRoutine
    {
        /// <summary>물결이 퍼지는 속도 (m/s) — 클라이언트의 전장 덮개(VFXPlayer)도 같은 값을 쓴다</summary>
        public const float Speed = 16f;
        /// <summary>얼음 장판 표시 위치: 내 3레인 × 깊이 5줄</summary>
        public static readonly float[] FieldRows = { 0.1f, 0.3f, 0.5f, 0.7f, 0.9f };
        private static readonly List<int> s_buffer = new List<int>(96);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly Vector2 _origin;
        private readonly float _duration;
        private readonly float _dmgFocus;
        private readonly float _maxRadius;
        private readonly HashSet<int> _frozen = new HashSet<int>();
        private readonly bool[] _towerFrozen = new bool[4];
        private readonly bool[] _playerHit = new bool[4];
        private readonly bool[] _fieldShown = new bool[3 * 5];
        private float _radius;

        public FreezeWave(SpellDefinition def, ISpellWorld world, Team casterTeam, float focus)
        {
            _def = def;
            _team = casterTeam;
            _origin = world.GetPlayerPosition(casterTeam);
            _duration = def.EffectDuration * SpellDamage.DurationFocus(def, focus);
            _dmgFocus = SpellDamage.DamageFocus(def, focus);
            _maxRadius = world.Arena.FieldLength * 1.5f; // 가장 먼 모서리까지
            world.PublishFx(def.SpellId, VfxPart.Cast, _origin, 1.5f, world.GetPlayerWorldColumn(casterTeam));
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _radius += Speed * deltaTime;
            float r2 = _radius * _radius;
            var arena = world.Arena;

            for (int c = 0; c < 3; c++)
            {
                int lane = arena.ResolveLane(_team, (Column)c);
                var frame = arena.GetLane(_team, lane);
                for (int r = 0; r < FieldRows.Length; r++)
                {
                    int k = c * FieldRows.Length + r;
                    Vector2 p = frame.Point(frame.Length * FieldRows[r]);
                    if (_fieldShown[k] || (p - _origin).sqrMagnitude > r2) continue;
                    _fieldShown[k] = true;
                    world.PublishFx(_def.SpellId, VfxPart.Field, p, 0f, lane);
                }
            }

            // 개인전: 나를 뺀 모두의 유닛 · 타워
            for (int t = 0; t < arena.TeamCount; t++)
            {
                var enemy = (Team)t;
                if (enemy == _team) continue;
                world.CollectTeamUnits(enemy, true, s_buffer);
                for (int i = 0; i < s_buffer.Count; i++)
                {
                    int id = s_buffer[i];
                    if (_frozen.Contains(id) || !world.TryGetUnitInfo(id, out Vector2 pos, out _, out _) || (pos - _origin).sqrMagnitude > r2) continue;
                    _frozen.Add(id);
                    world.FreezeUnit(id, _duration);
                    if (_def.BaseDamage > 0f) SpellDamage.HitUnit(world, _def.SpellId, _team, id, _def.BaseDamage, _dmgFocus);
                }
                if (!_playerHit[t] && world.IsPlayerAlive(enemy) && (world.GetPlayerPosition(enemy) - _origin).sqrMagnitude <= r2)
                {
                    _playerHit[t] = true;
                    if (_def.BaseDamage > 0f) SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, enemy, _def.BaseDamage, _dmgFocus);
                }
                if (!_towerFrozen[t] && (arena.StructurePosition(enemy, false) - _origin).sqrMagnitude <= r2)
                {
                    _towerFrozen[t] = true;
                    world.FreezeTower(enemy, _duration);
                }
            }
            return _radius < _maxRadius;
        }
    }

    public sealed class DrainLife : IProjectileHitHandler
    {
        /// <summary>플레이어 대신 레인 유닛 · 설치물에게서 빼앗을 때의 회복 비율 (EffectRatio에 곱함)</summary>
        public const float LaneHealRatio = 0.5f;
        /// <summary>레인 흡수 때 Impact 연출(흡혈 선)을 붙이는 최대 대상 수</summary>
        private const int MaxImpactFx = 16;
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly int _spellId;

        private DrainLife(int spellId)
        {
            _spellId = spellId;
        }

        public static void Apply(SpellDefinition def, ISpellWorld world, Team casterTeam, int worldColumn, float focus)
        {
            Team enemy = world.GetLaneTarget(casterTeam, worldColumn);
            float dmgFocus = SpellDamage.DamageFocus(def, focus);
            world.PublishFx(def.SpellId, VfxPart.Telegraph, world.GetPlayerPosition(casterTeam), 1.5f, world.GetPlayerWorldColumn(casterTeam));

            // ① 상대 플레이어 — 서 있는 칸과 상관없이 (흡혈 선이 따라간다)
            if (world.IsPlayerAlive(enemy))
            {
                Vector2 enemyPos = world.GetPlayerPosition(enemy);
                world.PublishFx(def.SpellId, VfxPart.Impact, enemyPos, 1.4f, worldColumn);
                float applied = SpellDamage.HitEnemyPlayer(world, def.SpellId, casterTeam, enemy, def.BaseDamage, dmgFocus);
                if (applied > 0f) SendLife(def, world, casterTeam, enemyPos, 1.4f, worldColumn, applied * def.EffectRatio);
            }

            // ② 같이 그 레인의 적 유닛 · 소환된 설치물(터렛 · 소환탑)에게서
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
        private readonly LaneFrame _band;
        private readonly float _min;
        private readonly float _max;
        private float _elapsed;
        private float _refresh;

        /// <summary>띠 반폭 (m) — 아레나의 타워 ~ 넥서스 거리의 절반. AimPointer 표시도 같은 값을 쓴다</summary>
        public static float HalfDepth(SpellDefinition def, ArenaGeometry arena)
        {
            float gap = Mathf.Abs(arena.TowerDepth - arena.NexusDepth);
            return gap > 0.5f ? gap * 0.5f : Mathf.Max(0.5f, def.FieldHalfDepth);
        }

        public RageRoutine(SpellDefinition def, ISpellWorld world, Team team, Vector2 center, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _duration = def.EffectDuration * SpellDamage.DurationFocus(def, focus);
            float half = HalfDepth(def, world.Arena);
            _band = world.Arena.BandFrame(team, worldColumn);
            float along = _band.Along(center);
            _min = along - half;
            _max = along + half;
            world.PublishFx(def.SpellId, VfxPart.Cast, world.GetPlayerPosition(team), 1.5f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Field, _band.Point(along), 0.02f, world.Arena.IsDuel ? 1 : worldColumn); // 3열 폭 장판
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
                        float along = _band.Along(pos);
                        if (along < _min || along > _max || Mathf.Abs(_band.Lateral(pos)) > world.Arena.BandHalfWidth) continue;
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
