// §5 전장 좌표계 — 서버 시뮬레이션이 쓰는 순수 데이터 좌표계 (MonoBehaviour 아님)
//
// 아레나 공간 (u, v):
//   u = Home 플레이어 기준 오른쪽(+) 방향 거리 (m). 열 중심 = (col - 1) * ColumnSpacing
//   v = Home 발판 → Away 발판 방향 거리 (m). Home 발판 v = 0, Away 발판 v = FieldLength
// 깊이(Depth 0~1)는 "시전자 자기 진영 → 상대 진영" 방향 정규화 거리다.

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Arena
{
    public sealed class ArenaGeometry
    {
        public Vector3 HomeBase { get; }
        public Vector3 Forward { get; }
        public Vector3 Right { get; }
        public float GroundY { get; }
        public float FieldLength { get; }
        public float ColumnSpacing { get; }
        public float NexusDepth { get; }
        public float TowerDepth { get; }
        public float SpawnDepth { get; }

        public float LaneHalfWidth => ColumnSpacing * 0.5f;
        public float MinU => -ColumnSpacing * 1.5f;
        public float MaxU => ColumnSpacing * 1.5f;

        public ArenaGeometry(Vector3 homeBase, Vector3 forward, float fieldLength, float columnSpacing,
                             float nexusDepth, float towerDepth, float spawnDepth)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
            Forward = forward.normalized;
            Right = Vector3.Cross(Vector3.up, Forward).normalized;
            HomeBase = homeBase;
            GroundY = homeBase.y;
            FieldLength = Mathf.Max(4f, fieldLength);
            ColumnSpacing = Mathf.Max(0.5f, columnSpacing);
            NexusDepth = Mathf.Clamp(nexusDepth, 0.5f, FieldLength * 0.5f);
            TowerDepth = Mathf.Clamp(towerDepth, NexusDepth, FieldLength * 0.5f);
            SpawnDepth = Mathf.Clamp(spawnDepth, 0.5f, FieldLength * 0.5f);
        }

        public static ArenaGeometry CreateDefault()
        {
            return new ArenaGeometry(Vector3.zero, Vector3.forward, 20f, 5f, 5f, 8f, 6f);
        }

        // ── 변환 ──────────────────────────────────────────────

        public Vector3 ToWorld(float u, float v, float height = 0f)
        {
            return HomeBase + Right * u + Forward * v + Vector3.up * height;
        }

        public Vector3 ToWorld(Vector2 uv, float height = 0f) => ToWorld(uv.x, uv.y, height);

        public Vector2 ToArena(Vector3 world)
        {
            Vector3 d = world - HomeBase;
            return new Vector2(Vector3.Dot(d, Right), Vector3.Dot(d, Forward));
        }

        public float ColumnU(int worldColumn)
        {
            return (Mathf.Clamp(worldColumn, 0, 2) - 1) * ColumnSpacing;
        }

        public int NearestWorldColumn(float u)
        {
            int c = Mathf.RoundToInt(u / ColumnSpacing) + 1;
            return Mathf.Clamp(c, 0, 2);
        }

        public float BaseV(Team team) => team == Team.Home ? 0f : FieldLength;

        /// <summary>그 진영의 "앞쪽"(상대 진영 방향) v 부호</summary>
        public float ForwardSign(Team team) => team == Team.Home ? 1f : -1f;

        public float DepthToV(Team caster, float depth01)
        {
            return BaseV(caster) + ForwardSign(caster) * Mathf.Clamp01(depth01) * FieldLength;
        }

        public float VToDepth(Team caster, float v)
        {
            return Mathf.Clamp01((v - BaseV(caster)) * ForwardSign(caster) / FieldLength);
        }

        // ── 주요 지점 ─────────────────────────────────────────

        public Vector2 PlayerPosition(Team team, int worldColumn)
        {
            return new Vector2(ColumnU(worldColumn), BaseV(team));
        }

        public Vector2 StructurePosition(Team team, bool isNexus)
        {
            float depth = isNexus ? NexusDepth : TowerDepth;
            return new Vector2(0f, BaseV(team) + ForwardSign(team) * depth);
        }

        public Vector2 SpawnPosition(Team team, int worldColumn)
        {
            return new Vector2(ColumnU(worldColumn), BaseV(team) + ForwardSign(team) * SpawnDepth);
        }

        /// <summary>§11.1 돌파 지점 — 대상 진영 Commander 발판 앞</summary>
        public Vector2 BreachPoint(Team targetTeam, int worldColumn, float offset)
        {
            return new Vector2(ColumnU(worldColumn), BaseV(targetTeam) + ForwardSign(targetTeam) * offset);
        }

        public Quaternion FacingRotation(Team team)
        {
            return Quaternion.LookRotation(Forward * ForwardSign(team), Vector3.up);
        }

        public Vector2 ClampToField(Vector2 uv)
        {
            uv.x = Mathf.Clamp(uv.x, MinU, MaxU);
            uv.y = Mathf.Clamp(uv.y, 0f, FieldLength);
            return uv;
        }
    }
}
