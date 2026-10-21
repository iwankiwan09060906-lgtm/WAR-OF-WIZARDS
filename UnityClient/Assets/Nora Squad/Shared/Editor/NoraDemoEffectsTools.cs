using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NoraSquad.Editor
{
    /// <summary>
    /// Manage the demo effects that come with the characters: switch them off on every prefab,
    /// switch them back on, or remove the "Demo Particles" folder from the project cleanly.
    /// </summary>
    public static class NoraDemoEffectsTools
    {
        const string Menu = "Tools/Nora Squad/Demo Effects/";

        [MenuItem(Menu + "Disable On All Characters", priority = 20)]
        public static void DisableAll() => SetEnabled(false);

        [MenuItem(Menu + "Enable On All Characters", priority = 21)]
        public static void EnableAll() => SetEnabled(true);

        [MenuItem(Menu + "Enable On All Characters", true)]
        static bool CanEnable() => DemoParticlesFolder() != null;

        [MenuItem(Menu + "Remove Demo Effects From Project...", priority = 40)]
        public static void RemoveWithConfirmation()
        {
            var folder = DemoParticlesFolder();
            var message = "This removes the demo effects from every Nora Squad character prefab" +
                          (folder != null ? $" and deletes the folder:\n\n{folder}\n\n" : ".\n\n") +
                          "Characters keep all animations and still raise onHit / onSkill. " +
                          "To get the demo effects back, import the package again.";
            if (EditorUtility.DisplayDialog("Remove Nora Squad demo effects", message, "Remove", "Cancel"))
                Remove();
        }

        [MenuItem(Menu + "Remove Demo Effects From Project...", true)]
        static bool CanRemove() => DemoParticlesFolder() != null || CharacterPrefabs().Any(HasEffects);

        /// <summary>Clears every effect reference on the character prefabs and deletes the Demo Particles folder.</summary>
        public static void Remove()
        {
            var changed = Edit(so =>
            {
                so.FindProperty("hitEffect").objectReferenceValue = null;
                so.FindProperty("skillEffect").objectReferenceValue = null;
                so.FindProperty("projectile").objectReferenceValue = null;
            });

            var folder = DemoParticlesFolder();
            if (folder != null)
                AssetDatabase.DeleteAsset(folder);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Nora Squad] Demo effects removed from {changed} prefabs" + (folder != null ? $" and {folder} deleted." : "."));
        }

        static void SetEnabled(bool enabled)
        {
            var changed = Edit(so => so.FindProperty("spawnEffects").boolValue = enabled);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Nora Squad] Demo effects {(enabled ? "enabled" : "disabled")} on {changed} character prefabs.");
        }

        static int Edit(System.Action<SerializedObject> change)
        {
            var count = 0;
            foreach (var character in CharacterPrefabs())
            {
                var so = new SerializedObject(character);
                change(so);
                if (so.ApplyModifiedPropertiesWithoutUndo())
                {
                    EditorUtility.SetDirty(character);
                    count++;
                }
            }
            return count;
        }

        static bool HasEffects(NoraCharacter character)
        {
            var so = new SerializedObject(character);
            return so.FindProperty("hitEffect").objectReferenceValue != null ||
                   so.FindProperty("skillEffect").objectReferenceValue != null ||
                   so.FindProperty("projectile").objectReferenceValue != null;
        }

        /// <summary>Every prefab in the project whose root has a NoraCharacter (base prefabs first, then variants).</summary>
        static IEnumerable<NoraCharacter> CharacterPrefabs() =>
            AssetDatabase.FindAssets("t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(go => go != null)
                .Select(go => go.GetComponent<NoraCharacter>())
                .Where(c => c != null)
                .OrderBy(c => PrefabUtility.IsPartOfVariantPrefab(c) ? 1 : 0)
                .ToList();

        /// <summary>The "Demo Particles" folder, wherever the pack was moved to (found through its shader).</summary>
        static string DemoParticlesFolder()
        {
            var shader = AssetDatabase.FindAssets("NS_Particle t:Shader").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
            if (shader == null)
                return null;
            var folder = Path.GetDirectoryName(Path.GetDirectoryName(shader))?.Replace('\\', '/');
            return folder != null && folder.EndsWith("Demo Particles") ? folder : null;
        }
    }
}
