// 1:1 전장 너비 맞추기 — 씬 아트(발판 · 레인 띠 · 다리 바닥 · 벽 · 절벽)는 만들 때의 열 간격(Home_Commander_R 의 x)으로 놓여 있다.
// ArenaLayout.columnSpacing 이 그와 다르면 실행 중에 좌우 발판 · 레인 띠를 옮기고 바닥 아트를 옆으로 늘린다 (표시 전용 — 판정은 ArenaGeometry).
// 씬을 새 간격으로 다시 만들면(Spellbound > 5 · 6 메뉴) 비율이 1 이 되어 아무것도 하지 않는다.

using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    public sealed class DuelArenaView : MonoBehaviour
    {
        public void Initialize(ClientSession session)
        {
            var arena = GameObject.Find("/Arena");
            if (arena == null) return;
            var root = arena.transform;
            var reference = root.Find("Home/Home_Commander_R");
            float built = reference != null ? Mathf.Abs(reference.localPosition.x) : 0f;
            float spacing = session.Arena.ColumnSpacing;
            if (built < 0.1f || Mathf.Abs(built - spacing) < 0.01f) return;
            float k = spacing / built;

            foreach (var side in new[] { "Home", "Away" })
                foreach (var slot in new[] { "L", "R" })
                    StretchX(root.Find(side + "/" + side + "_Commander_" + slot), k, false);
            var lanes = root.Find("Lanes");
            if (lanes != null)
                foreach (Transform lane in lanes) StretchX(lane, k, true);
            var art = root.Find("Art_Bridge");
            if (art != null)
            {
                var s = art.localScale;
                s.x *= k;
                art.localScale = s;
            }
        }

        private static void StretchX(Transform t, float k, bool widen)
        {
            if (t == null) return;
            var p = t.localPosition;
            p.x *= k;
            t.localPosition = p;
            if (!widen) return;
            var s = t.localScale;
            s.x *= k;
            t.localScale = s;
        }
    }
}
