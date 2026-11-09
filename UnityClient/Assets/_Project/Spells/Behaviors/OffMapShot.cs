// 맵 밖에서 날아오는 스킬 공용 — 레인 안에서만 벌어지지 않게, 시전자 손 대신 전장 바깥에서 출발한다.
//   · 어디서 · 얼마나 높이 · 몇 초에 날아올지는 SpellDefinition 의 OffMap* 데이터가 정한다 (Docs/Battle/OffMapSkills.md).
//   · 판정은 그대로 도착 지점 · 도착 순간으로 한다. 출발점은 표시 경로일 뿐이라 전장 범위로 자르지 않는다.
//   · 도착까지 걸리는 시간이 거리와 상관없이 일정해서, 표시를 보고 피할 시간이 늘 같다.
// S11 · S12 소환수는 맵 밖에서 날아와 도착 지점에서 유닛이 된다 (SummonDrop).

using SpellboundVR.Arena;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public static class OffMapShot
    {
        /// <summary>어느 쪽에서 들어오나 — 1:1 은 조준 지점에 가까운 옆, 그 밖에는 시전마다 다르게</summary>
        public static int Side(ISpellWorld world, Team team, in LaneFrame lane, Vector2 target)
        {
            var arena = world.Arena;
            float lateral = Vector2.Dot(target - arena.Center, lane.Right);
            if (arena.IsDuel && Mathf.Abs(lateral) > 1f) return lateral > 0f ? 1 : -1;
            return SpellDamage.Hash01((uint)(world.Time * 1000f) + (uint)team * 31u) < 0.5f ? -1 : 1;
        }

        /// <summary>OffMap 스킬이면 발사 정보의 출발점 · 높이 · 속도를 맵 밖 기준으로 바꾼다 (None 이면 그대로). side = 0 이면 Side 로 고른다</summary>
        public static void Apply(SpellDefinition def, ISpellWorld world, Team team, Vector2 target, ref ProjectileLaunch launch, int side = 0)
        {
            if (!def.FromOffMap) return;
            var lane = world.Arena.GetLane(team, launch.WorldColumn);
            if (side == 0) side = Side(world, team, lane, target);
            launch.From = world.Arena.OffMapPoint(lane, def.OffMap, target, side, def.OffMapDistance);
            Launch(def, target, ref launch);
        }

        /// <summary>출발점을 이미 정한 경우 (전장 둘레 등)</summary>
        public static void Launch(SpellDefinition def, Vector2 target, ref ProjectileLaunch launch)
        {
            launch.Height = def.OffMapHeight;
            launch.Speed = Vector2.Distance(launch.From, target) / Mathf.Max(0.1f, def.OffMapFlightSeconds);
            if (def.OffMapArrivalHeight > 0f) launch.TargetHeight = def.OffMapArrivalHeight;
        }
    }

    /// <summary>S11 · S12 — 소환수가 맵 밖에서 날아와 소환 지점에 도착하면 유닛이 된다</summary>
    public sealed class SummonDrop : IProjectileHitHandler
    {
        private readonly SpellDefinition _def;
        private readonly float _hpMultiplier;

        private SummonDrop(SpellDefinition def, float hpMultiplier)
        {
            _def = def;
            _hpMultiplier = hpMultiplier;
        }

        public static void Launch(SpellDefinition def, ISpellWorld world, Team team, int lane, Vector2 position, float hpMultiplier)
        {
            var launch = new ProjectileLaunch
            {
                SpellId = def.SpellId,
                CasterTeam = team,
                WorldColumn = lane,
                Mode = ProjectileMode.Point,
                TargetPoint = position,
                TargetHeight = 0.05f,
                FocusBonus = 1f,
                SuppressImpactFx = true,
                OnHit = new SummonDrop(def, hpMultiplier),
            };
            OffMapShot.Apply(def, world, team, position, ref launch, -1); // 내 쪽에서 들어와 내 소환 지점으로
            world.LaunchProjectile(launch);
        }

        public void OnProjectileArrived(ISpellWorld world, in ProjectileArrival arrival)
        {
            if (!world.IsPlayerAlive(arrival.CasterTeam)) return;
            world.PublishFx(_def.SpellId, VfxPart.Cast, arrival.Position, 0.05f, arrival.WorldColumn);
            if (!world.SpawnUnit(arrival.CasterTeam, _def.SummonKind, arrival.WorldColumn, arrival.Position, _hpMultiplier, out _))
                Debug.LogWarning($"[SummonDrop] {_def.Code} 소환 실패 (생존 상한 · 풀 부족)");
        }
    }
}
