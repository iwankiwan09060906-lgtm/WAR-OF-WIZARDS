// 에셋 투입 보조 메뉴 (가이드: Docs/Art/AssetIntegrationGuide.md)
//   Spellbound > Assets > Assign Rune Icons      : Assets/Art/Runes/Icons/Icon_{SkillId}*.png → RuneTemplate.Icon
//   Spellbound > Runes  > Record Last Stroke → …  : (플레이 중) 방금 그린 궤적을 해당 룬 템플릿의 샘플로 저장
//   Spellbound > Runes  > Clear Recorded Samples   : 녹화 샘플(recorded_*)만 삭제, 기본 도형 샘플은 유지

using System.IO;
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
            Debug.Log($"[Asset Tools] 룬 아이콘 {assigned}개 할당 (규칙: {RuneIconFolder}/Icon_S03_*.png)");
        }

        private static Texture2D FindIcon(string code)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RuneIconFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith("Icon_" + code)) continue;

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                if (importer != null && (!importer.alphaIsTransparency || importer.wrapMode != TextureWrapMode.Clamp))
                {
                    importer.alphaIsTransparency = true;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.mipmapEnabled = false;
                    importer.SaveAndReimport();
                }
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            return null;
        }

        // ── 룬 샘플 녹화 ──────────────────────────────────────

        [MenuItem("Spellbound/Runes/Record Last Stroke → S03 Fireball", priority = 140)]
        public static void RecordFireball() => Record(SpellIds.S03_Fireball);

        [MenuItem("Spellbound/Runes/Record Last Stroke → S07 Gatling", priority = 141)]
        public static void RecordGatling() => Record(SpellIds.S07_Gatling);

        [MenuItem("Spellbound/Runes/Record Last Stroke → S08 Shield", priority = 142)]
        public static void RecordShield() => Record(SpellIds.S08_Shield);

        [MenuItem("Spellbound/Runes/Clear Recorded Samples (keep defaults)", priority = 160)]
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
            if (cast == null || cast.LastStroke == null || cast.LastStroke.Length < 8)
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
            var points = (Vector2[])cast.LastStroke.Clone();
            template.AddSample(RecordedPrefix + System.DateTime.Now.ToString("MMdd_HHmmss"), points);
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Asset Tools] {template.name}에 샘플 추가 ({points.Length}점) — 총 {template.Samples.Count}개");
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
