// §14.2 StructureExecutor — 조준: 설치 구역(열) / 스킬: S13 터렛, H06 소환탑
// §14.4 설치 구역
//   · S13 터렛 : 내 타워가 있으면 타워 앞, 타워가 파괴됐으면 넥서스 앞 (조준한 열). 진영당 최대 3기
//   · H06 소환탑: 조준한 깊이 — 내 발판 앞부터 상대 타워 앞까지 어디든. 진영당 최대 4기
//   · 다른 구조물(타워 · 넥서스 · 터렛 · 소환탑)과 겹치면 레인을 따라 가장 가까운 빈자리로 옮긴다 (레인 안 양옆 포함).
//     레인에 빈자리가 없으면 시전을 거절한다 (쿨타임 미소모)
//   · 최대 수를 넘으면 가장 오래된 것을 교체
// S10 집중: 설치물 체력 × 집중 배율.

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Spells.Behaviors;
using UnityEngine;

namespace SpellboundVR.Spells.Executor
{
    public sealed class StructureExecutor : ISpellExecutor
    {
        /// <summary>구조물끼리 최소 간격 (m, 중심 거리)</summary>
        public const float Spacing = 2.6f;
        /// <summary>빈자리를 찾을 때 레인을 따라 움직이는 간격 (m)</summary>
        private const float SearchStep = 0.75f;
        private const int SearchSteps = 40;

        private static readonly List<int> s_units = new List<int>(32);
        private static readonly List<int> s_buffer = new List<int>(64);

        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public StructureExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        /// <summary>진영당 최대 설치 수 (넘으면 가장 오래된 것을 교체)</summary>
        public static int MaxCount(MinionKind kind) => kind == MinionKind.SummonHut ? 4 : 3;

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int lane = _world.Arena.ResolveLane(casterTeam, request.TargetColumn);
            if (!TryPlace(_world, _def, request, casterTeam, out Vector2 pos))
            {
                Debug.LogWarning($"[StructureExecutor] {_def.Code} 설치할 빈자리 없음"); // 보통은 시전 검사(MatchSimulation.CanPlaceUnits)에서 먼저 거절된다
                return;
            }
            if (_world.CountTeamUnits(casterTeam, _def.SummonKind, out int oldest) >= MaxCount(_def.SummonKind)) _world.RemoveUnit(oldest);
            _world.PublishFx(_def.SpellId, VfxPart.Cast, pos, 0.05f, lane);
            if (!_world.SpawnUnit(casterTeam, _def.SummonKind, lane, pos, SpellDamage.HpFocus(_def, _world.CastFocus), out _))
                Debug.LogWarning($"[StructureExecutor] {_def.Code} 설치 실패 (생존 상한 · 풀 부족)");
        }

        /// <summary>
        /// 설치 자리 — 겹치지 않는 자리가 없으면 false (시전 거절, 쿨타임 미소모).
        /// 최대 수에 닿았으면 교체될 가장 오래된 것은 빈자리로 친다.
        /// </summary>
        public static bool TryPlace(ISpellWorld world, SpellDefinition def, in SpellCastRequest request, Team team, out Vector2 pos)
        {
            var arena = world.Arena;
            int lane = arena.ResolveLane(team, request.TargetColumn);
            var frame = arena.GetLane(team, lane);
            float min = Spacing;                                   // 내 발판 바로 앞
            float max = Mathf.Max(min, frame.Length - arena.TowerDepth - Spacing); // 상대 타워 앞
            float along = def.SummonKind == MinionKind.SummonHut
                ? Mathf.Clamp(Mathf.Clamp01(request.TargetDepth) * frame.Length, min, max)
                : frame.Along(world.GetPlacementPosition(team, lane));
            int replaced = world.CountTeamUnits(team, def.SummonKind, out int oldest) >= MaxCount(def.SummonKind) ? oldest : 0;
            CollectPlaced(world, replaced);

            // 가까운 깊이부터(앞 · 뒤 번갈아), 깊이마다 레인 가운데 → 양옆
            float side = arena.LaneHalfWidth * 0.6f;
            for (int step = 0; step < SearchSteps; step++)
            {
                int k = (step + 1) / 2;
                float a = along + (step % 2 == 1 ? k : -k) * SearchStep;
                if (a < min || a > max) continue;
                for (int l = 0; l < 3; l++)
                {
                    pos = frame.Point(a, l == 0 ? 0f : (l == 1 ? side : -side));
                    if (IsFree(world, pos)) return true;
                }
            }
            pos = default;
            return false;
        }

        private static void CollectPlaced(ISpellWorld world, int ignoreId)
        {
            s_units.Clear();
            for (int t = 0; t < world.Arena.TeamCount; t++)
            {
                world.CollectTeamUnits((Team)t, true, s_buffer);
                for (int i = 0; i < s_buffer.Count; i++)
                    if (s_buffer[i] != ignoreId && world.TryGetUnitInfo(s_buffer[i], out _, out TargetKind kind, out _) && kind == TargetKind.Structure)
                        s_units.Add(s_buffer[i]);
            }
        }

        private static bool IsFree(ISpellWorld world, Vector2 p)
        {
            var arena = world.Arena;
            float min2 = Spacing * Spacing;
            for (int t = 0; t < arena.TeamCount; t++)
            {
                var team = (Team)t;
                if ((arena.StructurePosition(team, true) - p).sqrMagnitude < min2) return false;
                for (int c = 0; c < TeamUtil.ColumnCount; c++)
                    if ((arena.TowerPosition(team, (Column)c) - p).sqrMagnitude < min2) return false;
            }
            for (int i = 0; i < s_units.Count; i++)
                if (world.TryGetUnitInfo(s_units[i], out Vector2 q, out _, out _) && (q - p).sqrMagnitude < min2) return false;
            return true;
        }
    }
}
