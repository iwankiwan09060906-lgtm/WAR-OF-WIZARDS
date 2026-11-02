// 장판 · 레인 계열 스킬 동작 (§14.4, §11.2 지속 장판 = 매 틱 판정)
//   S02 독 장판  : 파이어볼처럼 조준 지점(열 + 깊이)으로 독병이 포물선 비행 → 떨어진 깊이를 중심으로 띠 장판 (1:1 은 3열 전체, 개인전은 그 레인 안).
//                  틱마다 피해, 머문 틱 수만큼 피해 증가(상한)
//   S05 토네이도 : 조준한 레인을 따라 회오리가 상대 진영 쪽으로 쓸고 지나간다 (OffMap 이면 시전자 등 뒤 맵 밖에서 레인 축을 따라 들어온다).
//                  닿은 적 유닛은 피해 + KnockbackDistance만큼 뒤로(상대 진영 쪽) 밀려나고 잠시 정지 (유닛마다 1회, 설치물은 피해만)
//   S06 번개     : 조준 지점(열 + 깊이) 중심, 레인 폭 × 깊이 ±FieldHalfDepth(세로로 긴 직사각형)에 예고 후 ShotCount번 낙뢰.
//                  매 낙뢰마다 범위 안 적 유닛 전부 + 범위 안에 서 있는 상대 플레이어 (그 순간 위치 판정)
//   H02 레이저 빔 : 0.5초 예고선 후 EffectDuration 동안 레인 전체를 태우는 빔. 틱마다 레인 안 유닛 + 그 열에 선 상대 플레이어

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
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
            var launch = new ProjectileLaunch
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
            };
            OffMapShot.Apply(def, world, casterTeam, center, ref launch);
            world.LaunchProjectile(launch);
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
        private readonly LaneFrame _band;
        private readonly float _along;
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
            _band = world.Arena.BandFrame(team, worldColumn);
            _along = _band.Along(center);
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            // 장판 VFX는 3열 폭으로 제작 → 1:1 은 전장 가운데, 개인전은 레인 위에 레인을 가로질러 둔다
            var bandCenter = _band.Point(_along);
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
            float min = _along - _def.FieldHalfDepth;
            float max = _along + _def.FieldHalfDepth;
            world.CollectEnemyUnitsInLaneRect(_team, _band, min, max, arena.BandHalfWidth, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                SpellDamage.HitUnit(world, _def.SpellId, _team, id, TickDamage(Stack(id)), _focus);
            }

            for (int t = 0; t < arena.TeamCount; t++)
            {
                var enemy = (Team)t;
                if (enemy == _team || !world.IsPlayerAlive(enemy)) continue;
                Vector2 p = world.GetPlayerPosition(enemy);
                float along = _band.Along(p);
                if (along >= min - SpellDamage.PlayerBodyRadius && along <= max + SpellDamage.PlayerBodyRadius &&
                    Mathf.Abs(_band.Lateral(p)) <= arena.BandHalfWidth + SpellDamage.PlayerBodyRadius)
                    SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, enemy, TickDamage(Stack(world.GetPlayerId(enemy))), _focus);
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
        private readonly LaneFrame _lane;
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
            _lane = world.Arena.GetLane(team, worldColumn);
            _focus = SpellDamage.DamageFocus(def, focus);
            // 깊이(m, 시전자 발판 기준)로 계산 — 진영 방향과 무관. 맵 밖 출발은 레인 축의 시전자 뒤쪽
            _startDepth = def.FromOffMap ? -def.OffMapDistance : Mathf.Clamp01(def.LaneStartDepth) * _lane.Length;
            _endDepth = Mathf.Max(_startDepth + 1f, Mathf.Clamp01(def.LaneEndDepth) * _lane.Length);
            _duration = Mathf.Max(0.3f, def.EffectDuration);

            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.5f, world.GetPlayerWorldColumn(team));
            // 회오리 비주얼 = 레인을 따라 같은 속도로 움직이는 지점 투사체 (판정은 이 루틴이 한다)
            world.LaunchProjectile(new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = team,
                WorldColumn = worldColumn,
                From = _lane.Point(_startDepth),
                Height = 0.05f,
                Speed = (_endDepth - _startDepth) / _duration,
                Damage = 0f,
                IsPlayerSkill = true,
                FocusBonus = 1f,
                Mode = ProjectileMode.Point,
                TargetPoint = _lane.Point(_endDepth),
                TargetHeight = 0.05f,
                SuppressImpactFx = true,
            });
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _elapsed += deltaTime;
            float front = Mathf.Lerp(_startDepth, _endDepth, Mathf.Clamp01(_elapsed / _duration));
            float r = Mathf.Max(0.5f, _def.Radius);

            world.CollectEnemyUnitsInLaneRect(_team, _lane, front - r, front + r, world.Arena.LaneHalfWidth, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                if (AlreadyHit(id)) continue;
                MarkHit(id);
                SpellDamage.HitUnit(world, _def.SpellId, _team, id, _def.BaseDamage, _focus);
                // 회오리 앞쪽(상대 진영 방향)으로 밀어낸다 — 회오리가 다시 잡지 않도록 반경 밖까지
                if (!world.TryGetUnitInfo(id, out Vector2 pos, out _, out _)) continue;
                float depth = _lane.Along(pos);
                float target = Mathf.Max(depth, front) + r + Mathf.Max(0f, _def.KnockbackDistance);
                world.PushUnit(id, _lane.Dir * (target - depth), Mathf.Max(0f, _def.EffectRatio));
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
        private readonly LaneFrame _lane;
        private readonly float _along;
        private readonly Team _target;
        private readonly int _aimColumn;
        private readonly int _worldColumn;
        private readonly float _focus;
        private readonly float _halfDepth;
        private float _timer;
        private int _strikes;

        public LightningRoutine(SpellDefinition def, ISpellWorld world, Team team, Vector2 center, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _lane = world.Arena.GetLane(team, worldColumn);
            _along = _lane.Along(center);
            _target = world.GetLaneTarget(team, worldColumn);
            _aimColumn = world.GetAimColumn(team, worldColumn);
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
            return world.Arena.ClampToField(_lane.Point(_along + t * _halfDepth));
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

            float half = world.Arena.LaneHalfWidth;
            float min = _along - _halfDepth, max = _along + _halfDepth;
            world.CollectEnemyUnitsInLaneRect(_team, _lane, min, max, half, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
                SpellDamage.HitUnit(world, _def.SpellId, _team, s_buffer[i], _def.BaseDamage, _focus);

            // 상대 플레이어: 낙뢰 순간 범위 안(그 열 + 깊이)에 서 있으면 명중 (§11.2 회피 = 순간 위치)
            if (!world.IsPlayerAlive(_target)) return;
            float along = _lane.Along(world.GetPlayerPosition(_target));
            if (along >= min - SpellDamage.PlayerBodyRadius && along <= max + SpellDamage.PlayerBodyRadius)
                SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, _target, _def.BaseDamage, _focus, _def.Dodgeable, _aimColumn);
        }
    }

    public sealed class LaserRoutine : ISpellRoutine
    {
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly LaneFrame _lane;
        private readonly Team _target;
        private readonly int _aimColumn;
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
            _lane = world.Arena.GetLane(team, worldColumn);
            _target = world.GetLaneTarget(team, worldColumn);
            _aimColumn = world.GetAimColumn(team, worldColumn);
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.6f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Telegraph, _lane.Point(_lane.Length * 0.5f), 1.4f, worldColumn); // §11.2 예고선 (상대에게도 보임)
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
                world.PublishFx(_def.SpellId, VfxPart.Field, _lane.Point(_lane.Length * 0.5f), 1.4f, _worldColumn);
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
            float half = Mathf.Min(world.Arena.LaneHalfWidth, _def.Radius);
            world.CollectEnemyUnitsInLaneRect(_team, _lane, 0f, _lane.Length, half, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
                SpellDamage.HitUnit(world, _def.SpellId, _team, s_buffer[i], _def.BaseDamage, _focus);

            // 상대 플레이어: 매 틱 그 칸에 있으면 명중 (칸을 옮기면 그 틱부터 피함)
            if (world.IsPlayerAlive(_target) && world.GetPlayerWorldColumn(_target) == _aimColumn)
            {
                float applied = SpellDamage.HitEnemyPlayer(world, _def.SpellId, _team, _target, _def.BaseDamage, _focus);
                if (applied > 0f && (_playerHits++ % 3) == 0)
                    world.PublishFx(_def.SpellId, VfxPart.Impact, world.GetPlayerPosition(_target), 1.2f, _worldColumn);
            }
        }
    }
}
