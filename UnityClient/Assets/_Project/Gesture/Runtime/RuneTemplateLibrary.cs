// 19종 룬 템플릿 묶음. 에셋이 없으면 구현 대상 3종(S03 / S07 / S08)의 절차 생성 기본 템플릿을 쓴다.
//
// 기본 룬 디자인 (서로 닮지 않게 선택, §9 "19종 전체 룬 간 형태 유사도 검증 필수"):
//   S03 파이어볼 : 원 (○)
//   S07 총난사   : 지그재그 Z
//   S08 방어 쉴드 : 역삼각형 (▽, 방패 모양)

using System.Collections.Generic;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Gesture
{
    [CreateAssetMenu(menuName = "Spellbound/Rune Template Library", fileName = "RuneTemplateLibrary")]
    public sealed class RuneTemplateLibrary : ScriptableObject
    {
        public List<RuneTemplate> Templates = new List<RuneTemplate>();

        public RuneTemplate Find(int spellId)
        {
            for (int i = 0; i < Templates.Count; i++)
                if (Templates[i] != null && Templates[i].SpellId == spellId) return Templates[i];
            return null;
        }

        public static RuneTemplateLibrary CreateDefault()
        {
            var lib = CreateInstance<RuneTemplateLibrary>();
            lib.name = "RuneTemplateLibrary (Runtime Default)";
            lib.Templates.Add(CreateFireballTemplate());
            lib.Templates.Add(CreateGatlingTemplate());
            lib.Templates.Add(CreateShieldTemplate());
            return lib;
        }

        // ── 절차 생성 기본 템플릿 ─────────────────────────────

        public static RuneTemplate CreateFireballTemplate()
        {
            var t = CreateInstance<RuneTemplate>();
            t.SpellId = SpellIds.S03_Fireball;
            t.RuneName = "Circle";
            t.name = "Rune_S03_Fireball";
            t.AddSample("circle_cw", Ellipse(1f, 1f, 90f, -360f, 48));
            t.AddSample("circle_ccw", Ellipse(1f, 1f, 90f, 360f, 48));
            t.AddSample("ellipse_wide", Ellipse(1.3f, 1f, 90f, -360f, 48));
            t.AddSample("ellipse_tall", Ellipse(1f, 1.25f, 0f, 360f, 48));
            return t;
        }

        public static RuneTemplate CreateGatlingTemplate()
        {
            var t = CreateInstance<RuneTemplate>();
            t.SpellId = SpellIds.S07_Gatling;
            t.RuneName = "Zigzag";
            t.name = "Rune_S07_Gatling";
            t.AddSample("z", Polyline(new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) }, 16));
            t.AddSample("z_italic", Polyline(new[] { new Vector2(0.15f, 1f), new Vector2(1.15f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) }, 16));
            t.AddSample("z_wide", Polyline(new[] { new Vector2(0f, 1f), new Vector2(1.4f, 1f), new Vector2(0f, 0f), new Vector2(1.4f, 0f) }, 16));
            return t;
        }

        public static RuneTemplate CreateShieldTemplate()
        {
            var t = CreateInstance<RuneTemplate>();
            t.SpellId = SpellIds.S08_Shield;
            t.RuneName = "Shield";
            t.name = "Rune_S08_Shield";
            t.AddSample("tri_down", Polyline(new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 1f) }, 16));
            t.AddSample("tri_down_tall", Polyline(new[] { new Vector2(0f, 1.3f), new Vector2(1f, 1.3f), new Vector2(0.5f, 0f), new Vector2(0f, 1.3f) }, 16));
            t.AddSample("tri_down_wide", Polyline(new[] { new Vector2(0f, 1f), new Vector2(1.4f, 1f), new Vector2(0.7f, 0f), new Vector2(0f, 1f) }, 16));
            return t;
        }

        public static Vector2[] Ellipse(float rx, float ry, float startDeg, float sweepDeg, int count)
        {
            var pts = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                float a = (startDeg + sweepDeg * i / (count - 1)) * Mathf.Deg2Rad;
                pts[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
            }
            return pts;
        }

        public static Vector2[] Polyline(Vector2[] corners, int pointsPerSegment)
        {
            int segs = corners.Length - 1;
            var pts = new Vector2[segs * pointsPerSegment + 1];
            int k = 0;
            for (int s = 0; s < segs; s++)
            {
                for (int i = 0; i < pointsPerSegment; i++)
                    pts[k++] = Vector2.Lerp(corners[s], corners[s + 1], (float)i / pointsPerSegment);
            }
            pts[k] = corners[corners.Length - 1];
            return pts;
        }
    }
}
