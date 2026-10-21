using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NoraSquad.Demo.Editor
{
    /// <summary>
    /// Keeps the character lists of the demo scenes in sync with the project: when a demo scene is opened (or play
    /// mode starts) every Nora Squad character prefab in the project is added, so owners of several packs see all of
    /// their characters in the Showcase and Lineup scenes.
    /// </summary>
    [InitializeOnLoad]
    static class NoraDemoCharacterCollector
    {
        static NoraDemoCharacterCollector()
        {
            EditorSceneManager.sceneOpened += (scene, _) => Collect(scene);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingEditMode)
                    return;
                for (var i = 0; i < SceneManager.sceneCount; i++)
                    Collect(SceneManager.GetSceneAt(i));
            };
        }

        /// <summary>Fills the character list of every demo controller in the scene. Returns how many changed.</summary>
        public static int Collect(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return 0;
            List<NoraCharacter> characters = null;
            var changed = 0;
            foreach (var root in scene.GetRootGameObjects())
            foreach (var demo in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (!(demo is NoraShowcase) && !(demo is NoraLineup))
                    continue;
                characters ??= FindCharacters();
                var so = new SerializedObject(demo);
                var list = so.FindProperty("characters");
                if (Matches(list, characters))
                    continue;
                list.arraySize = characters.Count;
                for (var i = 0; i < characters.Count; i++)
                    list.GetArrayElementAtIndex(i).objectReferenceValue = characters[i];
                so.ApplyModifiedProperties();
                changed++;
                Debug.Log($"[Nora Squad] {demo.name}: {characters.Count} characters found in the project.", demo);
            }
            return changed;
        }

        /// <summary>Every character prefab (an NS_ prefab with a NoraCharacter on its root) in the project, by name.</summary>
        public static List<NoraCharacter> FindCharacters() =>
            AssetDatabase.FindAssets("t:Prefab NS_")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<NoraCharacter>)
                .Where(c => c != null)
                .OrderBy(c => c.name, System.StringComparer.Ordinal)
                .ToList();

        static bool Matches(SerializedProperty list, List<NoraCharacter> characters)
        {
            if (list.arraySize != characters.Count)
                return false;
            for (var i = 0; i < characters.Count; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue != characters[i])
                    return false;
            return true;
        }
    }
}
