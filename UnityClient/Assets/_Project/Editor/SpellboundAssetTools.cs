// 에셋 투입 보조 메뉴 (가이드: Docs/Art/AssetIntegrationGuide.md)
//   Spellbound > Assets > Assign Rune Icons      : Assets/Art/Runes/Icons/{SkillId}_s.png (또는 Icon_{SkillId}*.png) → RuneTemplate.Icon
//   Spellbound > Runes  > Build Rune Templates    : 19종 룬 템플릿 에셋 (RuneDesigns 기본 도형, 녹화 샘플은 유지) + 라이브러리 + 아이콘
//   Spellbound > Runes  > Record Last Stroke → …  : (플레이 중) 방금 인식을 시도한 룬(여러 획 포함)을 해당 룬 템플릿의 샘플로 저장
//   Spellbound > Runes  > Clear Recorded Samples   : 녹화 샘플(recorded_*)만 삭제, 기본 도형 샘플은 유지

using System.IO;
using System.Linq;
using SpellboundVR.Gesture;
using SpellboundVR.Input;
using SpellboundVR.Spells;
using UnityEditor;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundAssetTools
    {
        public const string RuneIconFolder = "Assets/Art/Runes/Icons";
        private const string RuneData = "Assets/_Project/Gesture/Templates/Data";
        private const string RecordedPrefix = "recorded_";

        // ── 룬 아이콘 ────────────────────────────────────────

        [MenuItem("Spellbound/Assets/Assign Rune Icons (Art/Runes/Icons)", priority = 120)]
        public static void AssignRuneIcons()
        {
            SpellboundSetupWizard.EnsureFolder(RuneIconFolder);
            int assigned = 0;
            foreach (var template in AllRuneTemplates())
            {
                string code = SpellIds.Code(template.SpellId);
                var tex = FindIcon(code);
                if (tex == null)
                {
                    // 파일이 삭제돼 깨진(Missing) 참조만 정리하고, 직접 지정한 정상 아이콘은 유지
                    if (template.Icon == null)
                    {
                        template.Icon = null;
                        EditorUtility.SetDirty(template);
                    }
                    continue;
                }
                template.Icon = tex;
                EditorUtility.SetDirty(template);
                assigned++;
                Debug.Log($"[Asset Tools] {template.name} ← {AssetDatabase.GetAssetPath(tex)}");
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Asset Tools] 룬 아이콘 {assigned}개 할당 (규칙: {RuneIconFolder}/S03_s.png 또는 Icon_S03_*.png)");
        }

        private static Texture2D FindIcon(string code)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RuneIconFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith("Icon_" + code) && file != code && !file.StartsWith(code + "_")) continue;

                // 손 옆에 작게 · 멀리서도 보이므로 밉맵 켜고(VR 반짝임 방지) 512로 충분
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer != null && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp ||
                                         !importer.mipmapEnabled || importer.maxTextureSize > 512))
                {
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.mipmapEnabled = true;
                    importer.maxTextureSize = 512;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        // ── 룬 템플릿 19종 ────────────────────────────────────

        /// <summary>RuneDesigns 19종으로 Rune_{SkillId}_{Name}.asset을 만들거나 기본 샘플을 다시 채운다 (recorded_* 샘플은 유지).
        /// 라이브러리 = 19종 전부, 아이콘도 같이 할당</summary>
        [MenuItem("Spellbound/Runes/Build Rune Templates (19 designs)", priority = 130)]
        public static void BuildRuneTemplates()
        {
            SpellboundSetupWizard.EnsureFolder(RuneData);
            var existing = AllRuneTemplates().ToList();
            var library = AssetDatabase.LoadAssetAtPath<RuneTemplateLibrary>(RuneData + "/RuneTemplateLibrary.asset");
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<RuneTemplateLibrary>();
                AssetDatabase.CreateAsset(library, RuneData + "/RuneTemplateLibrary.asset");
            }
            library.Templates.Clear();
            foreach (var design in RuneDesigns.All)
            {
                var template = existing.FirstOrDefault(t => t.SpellId == design.SpellId);
                if (template == null)
                {
                    template = RuneTemplateLibrary.CreateTemplate(design);
                    AssetDatabase.CreateAsset(template, $"{RuneData}/{template.name}.asset");
                }
                else
                {
                    template.RuneName = design.Name;
                    template.Samples.RemoveAll(s => s == null || s.Label == null || !s.Label.StartsWith(RecordedPrefix));
                    foreach (var (label, strokes) in RuneDesigns.Samples(design)) template.AddSample(label, strokes);
                }
                var icon = FindIcon(SpellIds.Code(design.SpellId));
                if (icon != null) template.Icon = icon;
                EditorUtility.SetDirty(template);
                library.Templates.Add(template);
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            int icons = library.Templates.Count(t => t.Icon != null);
            Debug.Log($"[Asset Tools] 룬 템플릿 {library.Templates.Count}종 (아이콘 {icons}개) — {RuneData}");
        }

        // ── 룬 샘플 녹화 ──────────────────────────────────────

        [MenuItem("Spellbound/Runes/Record Last Stroke/S01 Smoke", priority = 140)]
        public static void RecordS01() => Record(SpellIds.S01_Smoke);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S02 PoisonField", priority = 141)]
        public static void RecordS02() => Record(SpellIds.S02_PoisonField);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S03 Fireball", priority = 142)]
        public static void RecordS03() => Record(SpellIds.S03_Fireball);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S04 ArrowRain", priority = 143)]
        public static void RecordS04() => Record(SpellIds.S04_ArrowRain);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S05 Tornado", priority = 144)]
        public static void RecordS05() => Record(SpellIds.S05_Tornado);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S06 Lightning", priority = 145)]
        public static void RecordS06() => Record(SpellIds.S06_Lightning);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S07 Gatling", priority = 146)]
        public static void RecordS07() => Record(SpellIds.S07_Gatling);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S08 Shield", priority = 147)]
        public static void RecordS08() => Record(SpellIds.S08_Shield);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S09 TowerBuff", priority = 148)]
        public static void RecordS09() => Record(SpellIds.S09_TowerBuff);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S10 Focus", priority = 149)]
        public static void RecordS10() => Record(SpellIds.S10_Focus);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S11 AxeWarrior", priority = 150)]
        public static void RecordS11() => Record(SpellIds.S11_AxeWarrior);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S12 Mage", priority = 151)]
        public static void RecordS12() => Record(SpellIds.S12_Mage);

        [MenuItem("Spellbound/Runes/Record Last Stroke/S13 Turret", priority = 152)]
        public static void RecordS13() => Record(SpellIds.S13_Turret);

        [MenuItem("Spellbound/Runes/Record Last Stroke/H01 Freeze", priority = 153)]
        public static void RecordH01() => Record(SpellIds.H01_Freeze);

        [MenuItem("Spellbound/Runes/Record Last Stroke/H02 Laser", priority = 154)]
        public static void RecordH02() => Record(SpellIds.H02_Laser);

        [MenuItem("Spellbound/Runes/Record Last Stroke/H03 Drain", priority = 155)]
        public static void RecordH03() => Record(SpellIds.H03_Drain);

        [MenuItem("Spellbound/Runes/Record Last Stroke/H04 ReflectBarrier", priority = 156)]
        public static void RecordH04() => Record(SpellIds.H04_ReflectBarrier);

        [MenuItem("Spellbound/Runes/Record Last Stroke/H05 Rage", priority = 157)]
        public static void RecordH05() => Record(SpellIds.H05_Rage);

        [MenuItem("Spellbound/Runes/Record Last Stroke/H06 SummonTower", priority = 158)]
        public static void RecordH06() => Record(SpellIds.H06_SummonTower);

        [MenuItem("Spellbound/Runes/Clear Recorded Samples (keep defaults)", priority = 170)]
        public static void ClearRecorded()
        {
            int removed = 0;
            foreach (var t in AllRuneTemplates())
            {
                removed += t.Samples.RemoveAll(s => s != null && s.Label != null && s.Label.StartsWith(RecordedPrefix));
                EditorUtility.SetDirty(t);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Asset Tools] 녹화 샘플 {removed}개 삭제");
        }

        private static void Record(int spellId)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[Asset Tools] 플레이 중에 룬을 그린 뒤 실행하세요 (마우스 드래그).");
                return;
            }
            var cast = Object.FindFirstObjectByType<CastStateMachine>();
            int total = 0;
            if (cast != null) foreach (var s in cast.LastStrokes) total += s.Length;
            if (total < 8)
            {
                Debug.LogWarning("[Asset Tools] 녹화할 궤적이 없습니다. 먼저 룬을 한 번 그리세요.");
                return;
            }
            RuneTemplate template = null;
            foreach (var t in AllRuneTemplates()) if (t.SpellId == spellId) template = t;
            if (template == null)
            {
                Debug.LogError($"[Asset Tools] {SpellIds.Code(spellId)} 룬 템플릿 에셋이 없습니다 (Spellbound > 1. Setup 실행).");
                return;
            }
            template.AddSample(RecordedPrefix + System.DateTime.Now.ToString("MMdd_HHmmss"), cast.LastStrokes);
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Asset Tools] {template.name}에 샘플 추가 ({cast.LastStrokes.Count}획 {total}점) — 총 {template.Samples.Count}개");
        }

        private static System.Collections.Generic.IEnumerable<RuneTemplate> AllRuneTemplates()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:RuneTemplate"))
            {
                var t = AssetDatabase.LoadAssetAtPath<RuneTemplate>(AssetDatabase.GUIDToAssetPath(guid));
                if (t != null) yield return t;
            }
        }
    }
}
