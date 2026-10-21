// 스킬 · 유닛 데이터 에셋 — 코드 기본값(SpellDefinition.CreateDefault · MinionDefinition.CreateDefault)을 에셋으로 내보낸다.
//   · Spell_{SkillId}_{VfxName}.asset 19종 (있으면 GUID 유지한 채 값만 덮어쓰기) + SpellCatalog에 19종 등록
//   · Minion_AxeOrc / Dragon / Skeleton / Turret / SummonHut.asset (있으면 유지 — 수치 튜닝 보존)

using SpellboundVR.Combat;
using SpellboundVR.Spells;
using UnityEditor;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static partial class SpellboundSkillAssetBuilder
    {
        private const string SpellData = "Assets/_Project/Spells/Definition/Data";
        public const string MinionData = "Assets/_Project/Combat/Minion/Data";

        public static readonly MinionKind[] ExtraUnitKinds =
        {
            MinionKind.AxeOrc, MinionKind.Dragon, MinionKind.Skeleton, MinionKind.Turret, MinionKind.SummonHut,
        };

        [MenuItem("Spellbound/Skills/Rebuild Spell Definitions (19)", priority = 211)]
        public static void BuildSpellData()
        {
            SpellboundSetupWizard.EnsureFolder(SpellData);
            SpellboundSetupWizard.EnsureFolder(MinionData);

            var catalog = AssetDatabase.LoadAssetAtPath<SpellCatalog>(SpellData + "/SpellCatalog.asset");
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SpellCatalog>();
                AssetDatabase.CreateAsset(catalog, SpellData + "/SpellCatalog.asset");
            }
            catalog.Spells.Clear();

            foreach (int id in SpellDefinition.AllIds)
            {
                var def = SpellDefinition.CreateDefault(id);
                string path = $"{SpellData}/Spell_{SpellIds.Code(id)}_{def.VfxName}.asset";
                var existing = AssetDatabase.LoadAssetAtPath<SpellDefinition>(path);
                if (existing != null)
                {
                    string keepName = existing.name;
                    EditorUtility.CopySerialized(def, existing);
                    existing.name = keepName;
                    EditorUtility.SetDirty(existing);
                    Object.DestroyImmediate(def);
                    def = existing;
                }
                else
                {
                    def.name = System.IO.Path.GetFileNameWithoutExtension(path);
                    AssetDatabase.CreateAsset(def, path);
                }
                catalog.Spells.Add(def);
            }
            EditorUtility.SetDirty(catalog);

            foreach (var kind in ExtraUnitKinds)
            {
                string path = $"{MinionData}/Minion_{kind}.asset";
                if (AssetDatabase.LoadAssetAtPath<MinionDefinition>(path) != null) continue;
                var def = MinionDefinition.CreateDefault(kind);
                def.name = "Minion_" + kind;
                AssetDatabase.CreateAsset(def, path);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Skill Builder] 스킬 정의 {catalog.Spells.Count}종 → {SpellData} (SpellCatalog 갱신), 소환수 · 설치물 정의 → {MinionData}");
        }

        public static MinionDefinition[] LoadExtraUnitDefinitions()
        {
            var list = new System.Collections.Generic.List<MinionDefinition>();
            foreach (var kind in ExtraUnitKinds)
            {
                var d = AssetDatabase.LoadAssetAtPath<MinionDefinition>($"{MinionData}/Minion_{kind}.asset");
                if (d != null) list.Add(d);
            }
            return list.ToArray();
        }
    }
}
