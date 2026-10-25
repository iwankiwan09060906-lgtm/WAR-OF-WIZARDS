// 장판 · 레인 계열 스킬 동작 (§14.4, §11.2 지속 장판 = 매 틱 판정)
//   S02 독 장판  : 파이어볼처럼 조준 지점(열 + 깊이)으로 독병이 포물선 비행 → 떨어진 깊이를 중심으로 3열 전체를 가로지르는 띠 장판.
//                  틱마다 피해, 머문 틱 수만큼 피해 증가(상한)
//   S05 토네이도 : 조준한 레인을 따라 회오리가 시전자 앞에서 상대 진영 쪽으로 쓸고 지나간다.
//                  닿은 적 유닛은 피해 + KnockbackDistance만큼 뒤로(상대 진영 쪽) 밀려나고 잠시 정지 (유닛마다 1회, 설치물은 피해만)
//   S06 번개     : 조준 지점(열 + 깊이) 중심, 레인 폭 × 깊이 ±FieldHalfDepth(세로로 긴 직사각형)에 예고 후 ShotCount번 낙뢰.
//                  매 낙뢰마다 범위 안 적 유닛 전부 + 범위 안에 서 있는 상대 플레이어 (그 순간 위치 판정)
//   H02 레이저 빔 : 0.5초 예고선 후 EffectDuration 동안 레인 전체를 태우는 빔. 틱마다 레인 안 유닛 + 그 열에 선 상대 플레이어

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    /// <summary>S02 독병 — 조준 지점에 떨어지면 독 장판을 편다</summary>
    public sealed class PoisonFlask : IProjectileHitHandler
    {
        private readonly SpellDefinition _def;
        private readonly float _focus;

        private PoisonFlask(SpellDefinition def, float focus)
        {
            _def = def;
            _focus = focus;
        }

        public static void Launch(SpellDefinition def, ISpellWorld world, Team casterTeam, Vector2 center, int worldColumn, float focus)
        {
            Vector2 from = SpellDamage.Muzzle(world, casterTeam);
            world.PublishFx(def.SpellId, VfxPart.Cast, from, 1.5f, world.GetPlayerWorldColumn(casterTeam));
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = casterTeam,
                WorldColumn = worldColumn,
                From = from,
                Height = 1.6f,
                Speed = Mathf.Max(1f, def.ProjectileSpeed),
                Damage = 0f,
                IsPlayerSkill = true,
                FocusBonus = 1f,
                Mode = ProjectileMode.Point,
                TargetPoint = center,
                TargetHeight = 0.1f,
                ArcHeight = def.ArcHeight,
                SuppressImpactFx = true, // 장판이 3열 폭으로 직접 발행
                OnHit = new PoisonFlask(def, focus),
            });
        }

        public void OnProjectileArrived(ISpellWorld world, in ProjectileArrival arrival)
        {
            world.StartRoutine(new PoisonFieldRoutine(_def, world, arrival.CasterTeam, arrival.Position, arrival.WorldColumn, _focus));
        }
    }

    public sealed class PoisonFieldRoutine : ISpellRoutine
    {
        private const int MaxTracked = 128;
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly Vector2 _center;
        private readonly int _worldColumn;
        private readonly float _focus;
        private readonly int[] _ids = new int[MaxTracked];
        private readonly int[] _stacks = new int[MaxTracked];
        private int _tracked;
        private float _elapsed;
        private float _tickTimer;

        public PoisonFieldRoutine(SpellDefinition def, ISpellWorld world, Team team, Vector2 center, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _center = center;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            // 장판 VFX는 3열 폭으로 제작 → 전장 가운데(u = 0)에 둔다
            var bandCenter = new Vector2(0f, center.y);
            world.PublishFx(def.SpellId, VfxPart.Impact, bandCenter, 0f, worldColumn);
            world.PublishFx(def.SpellId, VfxPart.Field, bandCenter, 0.02f, worldColumn);
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _elapsed += deltaTime;
            _tickTimer -= deltaTime;
            while (_tickTimer <= 0f && _elapsed <= _def.EffectDuration + 1e-3f)
            {
                _tickTimer += Mathf.Max(0.1f, _def.TickInterval);
                ApplyTick(world);
            }
            return _elapsed < _def.EffectDuration;
        }

        private void ApplyTick(ISpellWorld world)
        {
            var arena = world.Arena;
            float vMin = _center.y - _def.FieldHalfDepth;
            float vMax = _center.y + _def.FieldHalfDepth;
            world.CollectEnemyUnitsInRect(_team, arena.MinU, arena.MaxU, vMin, vMax, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                SpellDamage.HitUnit(world, _def.SpellId, _team, id, TickDamage(Stack(id)), _focus);
            }

            Team enemy = TeamUtil.Opponent(_team);
            if (world.IsPlayerAlive(enemy))
            {
                float v = world.GetPlayerPosition(enemy).y;
                if (v >= vMin - SpellDamage.PlayerBodyRadius && v <= vMax + SpellDamage.PlayerBodyRadius)
                    SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, TickDamage(Stack(world.GetPlayerId(enemy))), _focus);
            }
        }

        private float TickDamage(int stack)
        {
            float d = _def.BaseDamage + _def.TickDamageRamp * stack;
            return _def.MaxTickDamage > 0f ? Mathf.Min(d, _def.MaxTickDamage) : d;
        }

        /// <summary>대상별 머문 틱 수 (0부터) — 호출할 때마다 1 증가</summary>
        private int Stack(int id)
        {
            for (int i = 0; i < _tracked; i++)
                if (_ids[i] == id) return _stacks[i]++;
            if (_tracked < MaxTracked)
            {
                _ids[_tracked] = id;
                _stacks[_tracked] = 1;
                _tracked++;
            }
            return 0;
        }
    }

    public sealed class TornadoRoutine : ISpellRoutine
    {
        private const int MaxTracked = 64;
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly int _worldColumn;
        private readonly float _focus;
        private readonly float _startDepth;
        private readonly float _endDepth;
        private readonly float _duration;
        private readonly int[] _hitIds = new int[MaxTracked];
        private int _hitCount;
        private float _elapsed;

        public TornadoRoutine(SpellDefinition def, ISpellWorld world, Team team, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            var arena = world.Arena;
            // 깊이(m, 시전자 발판 기준)로 계산 — 진영 방향과 무관
            _startDepth = Mathf.Clamp01(def.LaneStartDepth) * arena.FieldLength;
            _endDepth = Mathf.Max(_startDepth + 1f, Mathf.Clamp01(def.LaneEndDepth) * arena.FieldLength);
            _duration = Mathf.Max(0.3f, def.EffectDuration);

            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.5f, world.GetPlayerWorldColumn(team));
            // 회오리 비주얼 = 레인을 따라 같은 속도로 움직이는 지점 투사체 (판정은 이 루틴이 한다)
            float u = arena.ColumnU(worldColumn);
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = team,
                WorldColumn = worldColumn,
                From = new Vector2(u, arena.BaseV(team) + arena.ForwardSign(team) * _startDepth),
                Height = 0.05f,
                Speed = (_endDepth - _startDepth) / _duration,
                Damage = 0f,
                IsPlayerSkill = true,
                FocusBonus = 1f,
                Mode = ProjectileMode.Point,
                TargetPoint = new Vector2(u, arena.BaseV(team) + arena.ForwardSign(team) * _endDepth),
                TargetHeight = 0.05f,
                SuppressImpactFx = true,
            });
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _elapsed += deltaTime;
            var arena = world.Arena;
            float fwd = arena.ForwardSign(_team);
            float baseV = arena.BaseV(_team);
            float front = Mathf.Lerp(_startDepth, _endDepth, Mathf.Clamp01(_elapsed / _duration));
            float frontV = baseV + fwd * front;
            float r = Mathf.Max(0.5f, _def.Radius);
            float u = arena.ColumnU(_worldColumn);
            float half = arena.LaneHalfWidth;

            world.CollectEnemyUnitsInRect(_team, u - half, u + half, Mathf.Min(frontV - r, frontV + r), Mathf.Max(frontV - r, frontV + r), s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                if (AlreadyHit(id)) continue;
                MarkHit(id);
                SpellDamage.HitUnit(world, _def.SpellId, _team, id, _def.BaseDamage, _focus);
                // 회오리 앞쪽(상대 진영 방향)으로 밀어낸다 — 회오리가 다시 잡지 않도록 반경 밖까지
                if (!world.TryGetUnitInfo(id, out Vector2 pos, out _, out _)) continue;
                float depth = (pos.y - baseV) * fwd;
                float target = Mathf.Max(depth, front) + r + Mathf.Max(0f, _def.KnockbackDistance);
                world.PushUnit(id, new Vector2(0f, (target - depth) * fwd), Mathf.Max(0f, _def.EffectRatio));
            }
            return _elapsed < _duration;
        }

        private bool AlreadyHit(int id)
        {
            for (int i = 0; i < _hitCount; i++) if (_hitIds[i] == id) return true;
            return false;
        }

        private void MarkHit(int id)
        {
            if (_hitCount < MaxTracked) _hitIds[_hitCount++] = id;
        }
    }

    public sealed class LightningRoutine : ISpellRoutine
    {
        private static readonly List<int> s_buffer = new List<int>(48);
        /// <summary>범위 안 예고 · 낙뢰 표시 위치 (반폭 비율)</summary>
        private static readonly float[] s_marks = { -0.6f, 0f, 0.6f };

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly Vector2 _center;
        private readonly int _worldColumn;
        private readonly float _focus;
        private readonly float _halfDepth;
        private float _timer;
        private int _strikes;

        public LightningRoutine(SpellDefinition def, ISpellWorld world, Team team, Vector2 center, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _center = center;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            _halfDepth = Mathf.Max(0.5f, def.FieldHalfDepth);
            _timer = Mathf.Max(0f, def.TelegraphDelay);
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 2f, world.GetPlayerWorldColumn(team));
            for (int i = 0; i < s_marks.Length; i++)
                world.PublishFx(def.SpellId, VfxPart.Telegraph, Mark(world, s_marks[i]), 0.05f, worldColumn);
        }

        private Vector2 Mark(ISpellWorld world, float t)
        {
            return world.Arena.ClampToField(new Vector2(_center.x, _center.y + t * _halfDepth));
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            if (!world.IsPlayerAlive(_team)) return false;
            _timer -= deltaTime;
            while (_timer <= 0f && _strikes < _def.ShotCount)
            {
                Strike(world, _strikes);
                _strikes++;
                _timer += Mathf.Max(0.05f, _def.ShotInterval);
            }
            return _strikes < _def.ShotCount;
        }

        private void Strike(ISpellWorld world, int index)
        {
            // 낙뢰 2줄기: 범위 안에서 매번 다른 자리 (표시 전용 — 피해는 범위 전체)
            world.PublishFx(_def.SpellId, VfxPart.Impact, Mark(world, s_marks[index % s_marks.Length]), 0f, _worldColumn);
            world.PublishFx(_def.SpellId, VfxPart.Impact, Mark(world, s_marks[(index + 2) % s_marks.Length] * 0.7f), 0f, _worldColumn);

            var arena = world.Arena;
            float half = arena.LaneHalfWidth;
            float vMin = _center.y - _halfDepth, vMax = _center.y + _halfDepth;
            world.CollectEnemyUnitsInRect(_team, _center.x - half, _center.x + half, vMin, vMax, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
                SpellDamage.HitUnit(world, _def.SpellId, _team, s_buffer[i], _def.BaseDamage, _focus);

            // 상대 플레이어: 낙뢰 순간 범위 안(그 열 + 깊이)에 서 있으면 명중 (§11.2 회피 = 순간 위치)
            Team enemy = TeamUtil.Opponent(_team);
            if (!world.IsPlayerAlive(enemy)) return;
            Vector2 p = world.GetPlayerPosition(enemy);
            if (p.y >= vMin - SpellDamage.PlayerBodyRadius && p.y <= vMax + SpellDamage.PlayerBodyRadius)
                SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, _def.BaseDamage, _focus, _def.Dodgeable, _worldColumn);
        }
    }

    public sealed class LaserRoutine : ISpellRoutine
    {
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly int _worldColumn;
        private readonly float _focus;
        private float _elapsed;
        private float _tickTimer;
        private bool _fired;
        private int _playerHits;

        public LaserRoutine(SpellDefinition def, ISpellWorld world, Team team, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.6f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Telegraph, LaneCenter(world), 1.4f, worldColumn); // §11.2 예고선 (상대에게도 보임)
        }

        private Vector2 LaneCenter(ISpellWorld world)
        {
            var arena = world.Arena;
            return new Vector2(arena.ColumnU(_worldColumn), arena.FieldLength * 0.5f);
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            if (!world.IsPlayerAlive(_team)) return false;
            _elapsed += deltaTime;
            float beamTime = _elapsed - _def.TelegraphDelay;
            if (beamTime < 0f) return true;

            if (!_fired)
            {
                _fired = true;
                world.PublishFx(_def.SpellId, VfxPart.Field, LaneCenter(world), 1.4f, _worldColumn);
            }

            _tickTimer -= deltaTime;
            while (_tickTimer <= 0f && beamTime <= _def.EffectDuration + 1e-3f)
            {
                _tickTimer += Mathf.Max(0.05f, _def.TickInterval);
                ApplyTick(world);
            }
            return beamTime < _def.EffectDuration;
        }

        private void ApplyTick(ISpellWorld world)
        {
            var arena = world.Arena;
            float u = arena.ColumnU(_worldColumn);
            float half = Mathf.Min(arena.LaneHalfWidth, _def.Radius);
            world.CollectEnemyUnitsInRect(_team, u - half, u + half, 0f, arena.FieldLength, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
                SpellDamage.HitUnit(world, _def.SpellId, _team, s_buffer[i], _def.BaseDamage, _focus);

            // 상대 플레이어: 매 틱 그 열에 있으면 명중 (열을 옮기면 그 틱부터 피함)
            Team enemy = TeamUtil.Opponent(_team);
            if (world.IsPlayerAlive(enemy) && world.GetPlayerWorldColumn(enemy) == _worldColumn)
            {
                float applied = SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, _def.BaseDamage, _focus);
                if (applied > 0f && (_playerHits++ % 3) == 0)
                    world.PublishFx(_def.SpellId, VfxPart.Impact, world.GetPlayerPosition(enemy), 1.2f, _worldColumn);
            }
        }
    }
}
