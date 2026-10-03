// §5 전장 좌표계 — 씬 배치를 ArenaGeometry로 변환하는 컴포넌트.
// 기준: Docs/Input/ArenaCoordinateSpec.md
//
// homeBaseAnchor(기본: 이 오브젝트)의 위치 = Home Commander 중앙 발판(C),
// 그 forward = Home → Away 방향. 열 간격 · 전장 길이 · 구조물 깊이는 인스펙터에서 조정한다.
// 구조물 비주얼(씬에 배치된 오브젝트)을 연결하면 그 위치에서 깊이를 자동으로 읽는다.

using UnityEngine;

namespace SpellboundVR.Arena
{
    public sealed class ArenaLayout : MonoBehaviour
    {
        [Header("기준점")]
        [Tooltip("Home Commander 중앙 발판(C). 비우면 이 오브젝트")]
        public Transform homeBaseAnchor;

        [Header("치수 (m)")]
        public float columnSpacing = 5f;
        public float fieldLength = 20f;
        public float nexusDepth = 5f;
        public float towerDepth = 8f;
        [Tooltip("미니언 스폰 깊이 (자기 발판 기준)")]
        public float spawnDepth = 6f;

        [Header("씬 구조물 비주얼 (선택) — 연결 시 위치에서 깊이를 읽고, 표시용으로 재사용")]
        public Transform homeTowerVisual;
        public Transform homeNexusVisual;
        public Transform awayTowerVisual;
        public Transform awayNexusVisual;

        private ArenaGeometry _geometry;

        public ArenaGeometry Geometry => _geometry ??= Build();

        public ArenaGeometry Build()
        {
            Transform anchor = homeBaseAnchor != null ? homeBaseAnchor : transform;
            Vector3 basePos = anchor.position;
            basePos.y = transform.position.y;
            Vector3 forward = anchor.forward;

            float nexus = nexusDepth;
            float tower = towerDepth;
            var probe = new ArenaGeometry(basePos, forward, fieldLength, columnSpacing, nexus, tower, spawnDepth);
            if (homeNexusVisual != null) nexus = Mathf.Abs(probe.ToArena(homeNexusVisual.position).y);
            if (homeTowerVisual != null) tower = Mathf.Abs(probe.ToArena(homeTowerVisual.position).y);
            if (tower < nexus) tower = nexus + 1f;

            _geometry = new ArenaGeometry(basePos, forward, fieldLength, columnSpacing, nexus, tower, spawnDepth);
            return _geometry;
        }

        public Transform GetStructureVisual(Contracts.Team team, bool isNexus)
        {
            if (team == Contracts.Team.Home) return isNexus ? homeNexusVisual : homeTowerVisual;
            return isNexus ? awayNexusVisual : awayTowerVisual;
        }

        private void OnValidate()
        {
            _geometry = null;
        }

        private void OnDrawGizmos()
        {
            var g = Build();
            for (int c = 0; c < 3; c++)
            {
                float u = g.ColumnU(c);
                Gizmos.color = c == 0 ? new Color(0.3f, 0.6f, 1f) : (c == 1 ? Color.white : new Color(1f, 0.6f, 0.3f));
                Vector3 a = g.ToWorld(u - g.LaneHalfWidth, 0f, 0.05f);
                Vector3 b = g.ToWorld(u + g.LaneHalfWidth, 0f, 0.05f);
                Vector3 c2 = g.ToWorld(u + g.LaneHalfWidth, g.FieldLength, 0.05f);
                Vector3 d = g.ToWorld(u - g.LaneHalfWidth, g.FieldLength, 0.05f);
                Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c2); Gizmos.DrawLine(c2, d); Gizmos.DrawLine(d, a);
                Gizmos.DrawWireCube(g.ToWorld(g.PlayerPosition(Contracts.Team.Home, c), 0.25f), Vector3.one * 0.8f);
                Gizmos.DrawWireCube(g.ToWorld(g.PlayerPosition(Contracts.Team.Away, c), 0.25f), Vector3.one * 0.8f);
            }
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(g.ToWorld(g.StructurePosition(Contracts.Team.Home, true), 1f), 1f);
            Gizmos.DrawWireCube(g.ToWorld(g.StructurePosition(Contracts.Team.Home, false), 1f), new Vector3(1.5f, 2f, 1.5f));
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(g.ToWorld(g.StructurePosition(Contracts.Team.Away, true), 1f), 1f);
            Gizmos.DrawWireCube(g.ToWorld(g.StructurePosition(Contracts.Team.Away, false), 1f), new Vector3(1.5f, 2f, 1.5f));
        }
    }
}
