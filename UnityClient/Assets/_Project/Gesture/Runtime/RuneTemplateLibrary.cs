// 19종 룬 템플릿 묶음. 에셋이 없으면 RuneDesigns(디자인 이미지 Art/Runes/Designs 기준)의 절차 생성 템플릿을 쓴다.
// 에셋은 Spellbound > Runes > Build Rune Templates가 만든다 (Gesture/Templates/Data/Rune_{SkillId}_{Name}.asset).

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
            foreach (var design in RuneDesigns.All) lib.Templates.Add(CreateTemplate(design));
            return lib;
        }

        /// <summary>디자인 기본 도형 + 변형 샘플로 템플릿을 만든다</summary>
        public static RuneTemplate CreateTemplate(RuneDesigns.Design design)
        {
            var t = CreateInstance<RuneTemplate>();
            t.SpellId = design.SpellId;
            t.RuneName = design.Name;
            t.name = "Rune_" + SpellIds.Code(design.SpellId) + "_" + design.Name;
            foreach (var (label, strokes) in RuneDesigns.Samples(design)) t.AddSample(label, strokes);
            return t;
        }

        // ── 도형 도우미 ──────────────────────────────────────

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
