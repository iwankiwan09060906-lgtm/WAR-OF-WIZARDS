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
        public float fieldLength = 41f;
        public float nexusDepth = 5f;
        public float towerDepth = 13f;
        [Tooltip("미니언 스폰 깊이 (자기 발판 기준)")]
        public float spawnDepth = 6f;
        [Tooltip("Commander 발판 윗면 높이 (표시 전용 — 상대 아바타 · 쉴드 링을 이 높이에 올린다)")]
        public float commanderPlatformHeight = 0f;

        [Header("4인 개인전 (발판 4개가 마름모로 놓이는 전장 — 대각선 = fieldLength)")]
        [Tooltip("넥서스 깊이 — 옆 레인에서도 닿도록 1:1 보다 발판 가까이")]
        public float ffaNexusDepth = 4f;
        [Tooltip("타워 깊이")]
        public float ffaTowerDepth = 9f;
        [Tooltip("시야를 1:1 보다 이만큼 높인다 (m) — 세 레인이 한눈에 들어오게")]
        public float ffaViewLift = 2f;
        [Tooltip("타워 · 넥서스 · 상대 아바타 크기 배율 (표시 전용, 판정과 무관). 1:1 보다 가까이 있어 그대로 두면 커 보인다")]
        public float ffaModelScale = 0.65f;

        [Header("씬 구조물 비주얼 (선택) — 연결 시 위치에서 깊이를 읽고, 표시용으로 재사용")]
        public Transform homeTowerVisual;
        public Transform homeNexusVisual;
        public Transform awayTowerVisual;
        public Transform awayNexusVisual;

        private ArenaGeometry _geometry;
        private ArenaMode _mode = ArenaMode.Duel;

        public ArenaGeometry Geometry => _geometry ??= Build();

        public ArenaMode Mode => _mode;

        /// <summary>경기 종류를 바꾼다 — 다음 Geometry 부터 적용</summary>
        public void SetMode(ArenaMode mode)
        {
            _mode = mode;
            _geometry = null;
        }

        public ArenaGeometry Build()
        {
            Transform anchor = homeBaseAnchor != null ? homeBaseAnchor : transform;
            Vector3 basePos = anchor.position;
            basePos.y = transform.position.y;
            Vector3 forward = anchor.forward;

            if (_mode == ArenaMode.Ffa4)
            {
                _geometry = new ArenaGeometry(basePos, forward, fieldLength, columnSpacing, ffaNexusDepth, ffaTowerDepth, spawnDepth, ArenaMode.Ffa4);
                return _geometry;
            }

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
            if (team == Contracts.Team.Away) return isNexus ? awayNexusVisual : awayTowerVisual;
            return null; // 4인 개인전 2 · 4번 자리는 씬에 없다 — ArenaStateView 가 1번 구조물을 복제한다
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
