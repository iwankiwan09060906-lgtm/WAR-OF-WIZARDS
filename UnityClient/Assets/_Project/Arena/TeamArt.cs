// 진영별 아트 (아군 / 적군) — 씬의 정적 아트는 Home · Away 자리에 놓이지만, 색 · 재질은 "보는 사람 기준"이다.
//   로컬 플레이어 진영 쪽 = allyVisual (파란 발판 · 아군 타워 · 본인 넥서스), 상대 진영 쪽 = enemyVisual.
//   진영이 정해지기 전(오프라인 · 서버 화면)에는 Home = 아군. ArenaStateView가 진영 배정 시 ApplyAll을 부른다.
// 아트는 Spellbound > 6. Apply Arena Art가 각 앵커 아래에 Art_Ally / Art_Enemy 로 만든다.

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Arena
{
    public sealed class TeamArt : MonoBehaviour
    {
        public Team team;
        public GameObject allyVisual;
        public GameObject enemyVisual;

        public void Apply(Team localTeam)
        {
            bool ally = team == localTeam;
            if (allyVisual != null && allyVisual.activeSelf != ally) allyVisual.SetActive(ally);
            if (enemyVisual != null && enemyVisual.activeSelf == ally) enemyVisual.SetActive(!ally);
        }

        public static void ApplyAll(Team localTeam)
        {
            foreach (var art in FindObjectsByType<TeamArt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                art.Apply(localTeam);
        }
    }
}
