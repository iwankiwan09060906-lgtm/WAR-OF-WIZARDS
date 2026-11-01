// §6.3 조준 규칙 — 검지 레이를 열(L/C/R) · 깊이(0~1)로 변환
//   열 지정(Column)  : 레이 좌우 → L / C / R 스냅
//   범위 지정(Area)  : 좌우 → 열 스냅 + 앞뒤 → 레이 · 지면 교차점 깊이 (최대 사거리 제한)
//   소환 · 설치      : 열만 (라인 / 구역 L · C · R)
// 레이가 지면을 향하지 않으면(수평 이상) 수평 방향으로 최대 사거리 지점을 쓴다.
// 4인 개인전: 왼쪽 · 맞은편 · 오른쪽 상대를 향해 뻗은 세 레인 중 가리킨 방향에 가장 가까운 레인을 고른다.

using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Input
{
    public struct AimResult
    {
        public bool Valid;
        /// <summary>요청에 넣는 열 (시전자 시점)</summary>
        public Column LocalColumn;
        /// <summary>월드 레인 번호 (1:1 = 월드 열)</summary>
        public int WorldColumn;
        /// <summary>0 ~ 1 (Area 전용, 그 외 0)</summary>
        public float Depth;
        /// <summary>표시용 지면 지점 (열 중심으로 스냅됨)</summary>
        public Vector3 MarkerPosition;
    }

    public static class ColumnDepthResolver
    {
        public const float MinAreaDepth = 0.05f;

        public static AimResult Resolve(Ray ray, ArenaGeometry arena, Team team, SpellTargeting targeting, float maxDepth)
        {
            var result = new AimResult();
            Vector3 groundPoint;
            float groundY = arena.GroundY;
            float maxRange = arena.FieldLength * Mathf.Clamp01(maxDepth);

            if (ray.direction.y < -0.02f)
            {
                float t = (groundY - ray.origin.y) / ray.direction.y;
                groundPoint = ray.origin + ray.direction * Mathf.Max(0f, t);
            }
            else
            {
                Vector3 flat = ray.direction;
                flat.y = 0f;
                if (flat.sqrMagnitude < 1e-6f) return result;
                groundPoint = ray.origin + flat.normalized * (maxRange + 50f);
                groundPoint.y = groundY;
            }

            arena.ResolveAim(team, arena.ToArena(groundPoint), out int lane, out Column local, out float depth);
            var frame = arena.GetLane(team, lane);

            result.Valid = true;
            result.WorldColumn = lane;
            result.LocalColumn = local;

            if (targeting == SpellTargeting.Area)
            {
                depth = Mathf.Clamp(depth, MinAreaDepth, Mathf.Clamp01(maxDepth));
                result.Depth = depth;
                result.MarkerPosition = arena.ToWorld(frame.Point(depth * frame.Length), 0.05f);
            }
            else
            {
                result.Depth = 0f;
                // 열 지정 · 소환 · 설치: 레인 중간 지점에 표시
                float showDepth = targeting == SpellTargeting.Column || targeting == SpellTargeting.Beam ? 0.5f : 0.3f;
                result.MarkerPosition = arena.ToWorld(frame.Point(showDepth * frame.Length), 0.05f);
            }
            return result;
        }
    }
}
