// 장판 · 레인 계열 스킬 동작 (§14.4, §11.2 지속 장판 = 매 틱 판정)
//   S02 독 장판  : 조준 깊이를 중심으로 3열 전체를 가로지르는 얇고 넓은 띠. 틱마다 피해, 머문 틱 수만큼 피해 증가(상한)
//   S05 토네이도 : 한 레인의 가운데 구간(LaneStart~LaneEnd)에 회오리 장판. 안의 적 유닛은 붙잡혀 멈추고 틱 피해
//   S06 번개     : 예고 후 ShotCount번 낙뢰. 매번 레인의 가장 앞 적 유닛 주변, 없으면 그 열의 상대 발판 (그 순간 위치 판정)
//   H02 레이저 빔 : 0.5초 예고선 후 EffectDuration 동안 레인 전체를 태우는 빔. 틱마다 레인 안 유닛 + 그 열에 선 상대 플레이어

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
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
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.5f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Impact, center, 0f, worldColumn);
            world.PublishFx(def.SpellId, VfxPart.Field, center, 0.02f, worldColumn);
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
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly int _worldColumn;
        private readonly float _focus;
        private readonly float _vMin;
        private readonly float _vMax;
        private float _elapsed;
        private float _tickTimer;

        public TornadoRoutine(SpellDefinition def, ISpellWorld world, Team team, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            var arena = world.Arena;
            float a = arena.DepthToV(team, def.LaneStartDepth);
            float b = arena.DepthToV(team, def.LaneEndDepth);
            _vMin = Mathf.Min(a, b);
            _vMax = Mathf.Max(a, b);
            _tickTimer = Mathf.Max(0.1f, def.TickInterval) * 0.5f;

            float u = arena.ColumnU(worldColumn);
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 1.5f, world.GetPlayerWorldColumn(team));
            int funnels = Mathf.Max(1, def.ShotCount);
            for (int i = 0; i < funnels; i++)
            {
                float t = funnels == 1 ? 0.5f : (i + 0.5f) / funnels;
                world.PublishFx(def.SpellId, VfxPart.Field, new Vector2(u, Mathf.Lerp(_vMin, _vMax, t)), 0f, worldColumn);
            }
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            _elapsed += deltaTime;
            var arena = world.Arena;
            float u = arena.ColumnU(_worldColumn);
            float half = arena.LaneHalfWidth;
            world.CollectEnemyUnitsInRect(_team, u - half, u + half, _vMin, _vMax, s_buffer);

            // 매 틱 붙잡기 (장판 안에 있는 동안 계속 정지 — 들어오는 유닛도 바로 멈춘다)
            for (int i = 0; i < s_buffer.Count; i++) world.StunUnit(s_buffer[i], 0.2f);

            _tickTimer -= deltaTime;
            if (_tickTimer <= 0f)
            {
                _tickTimer += Mathf.Max(0.1f, _def.TickInterval);
                for (int i = 0; i < s_buffer.Count; i++)
                    SpellDamage.HitUnit(world, _def.SpellId, _team, s_buffer[i], _def.BaseDamage, _focus);
            }
            return _elapsed < _def.EffectDuration;
        }
    }

    public sealed class LightningRoutine : ISpellRoutine
    {
        private static readonly List<int> s_buffer = new List<int>(48);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly int _worldColumn;
        private readonly float _focus;
        private float _timer;
        private int _strikes;

        public LightningRoutine(SpellDefinition def, ISpellWorld world, Team team, int worldColumn, float focus)
        {
            _def = def;
            _team = team;
            _worldColumn = worldColumn;
            _focus = SpellDamage.DamageFocus(def, focus);
            _timer = Mathf.Max(0f, def.TelegraphDelay);
            world.PublishFx(def.SpellId, VfxPart.Cast, SpellDamage.Muzzle(world, team), 2f, world.GetPlayerWorldColumn(team));
            world.PublishFx(def.SpellId, VfxPart.Telegraph, PickTarget(world, out _), 0.05f, worldColumn);
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            if (!world.IsPlayerAlive(_team)) return false;
            _timer -= deltaTime;
            while (_timer <= 0f && _strikes < _def.ShotCount)
            {
                Strike(world);
                _strikes++;
                _timer += Mathf.Max(0.05f, _def.ShotInterval);
            }
            return _strikes < _def.ShotCount;
        }

        /// <summary>레인의 가장 앞(시전자에 가까운) 적 유닛, 없으면 그 열의 상대 발판</summary>
        private Vector2 PickTarget(ISpellWorld world, out bool atPlayer)
        {
            world.CollectEnemyUnitsInColumn(_team, _worldColumn, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                if (world.TryGetUnitInfo(s_buffer[i], out Vector2 pos, out _, out _))
                {
                    atPlayer = false;
                    return pos;
                }
            }
            atPlayer = true;
            return world.Arena.PlayerPosition(TeamUtil.Opponent(_team), _worldColumn);
        }

        private void Strike(ISpellWorld world)
        {
            Vector2 point = PickTarget(world, out bool atPlayer);
            world.PublishFx(_def.SpellId, VfxPart.Impact, point, 0f, _worldColumn);

            world.CollectEnemyUnitsInRadius(_team, point, _def.Radius, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
                SpellDamage.HitUnit(world, _def.SpellId, _team, s_buffer[i], _def.BaseDamage, _focus);

            // 상대 플레이어: 낙뢰 순간 그 열에 서 있으면 명중 (§11.2 회피 = 순간 위치)
            if (atPlayer || SpellDamage.EnemyPlayerInRadius(world, _team, point, _def.Radius))
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
