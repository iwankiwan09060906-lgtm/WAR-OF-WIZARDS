using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NoraSquad.Editor
{
    /// <summary>Getting-started window, shown once after the package is imported.</summary>
    [InitializeOnLoad]
    public class NoraSquadWelcomeWindow : EditorWindow
    {
        const string ShownKey = "NoraSquad.WelcomeShown.v1";
        const string DemoScene = "Assets/Nora Squad/Demo/NS_Showcase.unity";
        const string LineupScene = "Assets/Nora Squad/Demo/NS_Lineup.unity";
        const string Documentation = "Assets/Nora Squad/Documentation/README.md";
        const string SupportEmail = "contact.norasquad@gmail.com";

        static NoraSquadWelcomeWindow()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorPrefs.GetBool(ShownKey, false))
                    return;
                EditorPrefs.SetBool(ShownKey, true);
                Open();
            };
        }

        [MenuItem("Tools/Nora Squad/Welcome", priority = 0)]
        public static void Open()
        {
            var window = GetWindow<NoraSquadWelcomeWindow>(true, "Nora Squad", true);
            window.minSize = new Vector2(480, 660);
        }

        [MenuItem("Tools/Nora Squad/Open Demo Scene", priority = 1)]
        static void OpenDemo() => OpenScene(DemoScene);

        [MenuItem("Tools/Nora Squad/Open Lineup Scene", priority = 2)]
        static void OpenLineup() => OpenScene(LineupScene);

        static void OpenScene(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(path);
        }

        Vector2 scroll;
        GUIStyle titleStyle, body, code;

        void OnGUI()
        {
            titleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 18 };
            body ??= new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
            code ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

            scroll = EditorGUILayout.BeginScrollView(scroll);
            GUILayout.Space(8);
            GUILayout.Label("Nora Squad", titleStyle);
            GUILayout.Label("Stylized fantasy characters with animations and two paint styles.", body);
            EditorGUILayout.HelpBox("This is a character pack. The particle effects in \"Demo Particles\" are simple demo effects " +
                                    "that show where the animation events fire — swap them for your own or remove them (see 5).", MessageType.None);

            Section("1. Render pipeline");
            var urp = GraphicsSettings.currentRenderPipeline != null;
            EditorGUILayout.HelpBox(urp
                    ? "Universal Render Pipeline detected. Everything works out of the box."
                    : "Built-in Render Pipeline detected. Everything works out of the box: the character shader supports both pipelines.",
                MessageType.Info);

            Section("2. Try the demo");
            GUILayout.Label("Browse every character, play its animations and switch Unlit / Lit.", body);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Demo Scene", GUILayout.Height(28)))
                OpenDemo();
            if (GUILayout.Button("Open Lineup Scene", GUILayout.Height(28)))
                OpenLineup();
            GUILayout.EndHorizontal();

            Section("3. Use a character");
            GUILayout.Label(
                "Drag a prefab from <b>Nora Squad/Characters/&lt;Name&gt;/Prefabs</b> into your scene.\n" +
                "Every character is hand painted and works <b>Unlit</b> (exactly as painted) or <b>Lit</b> " +
                "(reacts to your scene lights): set <b>Lighting</b> on the NoraCharacter component or the material's Lighting slider.", body);

            Section("4. Control it from code");
            EditorGUILayout.SelectableLabel(
                "var hero = GetComponent<NoraSquad.NoraCharacter>();\n" +
                "hero.PlayAttack();          // also PlayIdle / PlayMove / PlaySkill\n" +
                "hero.SetLighting(NoraLighting.Lit);\n" +
                "hero.SetAnimationSpeed(1.5f);\n" +
                "hero.onHit.AddListener(() => Debug.Log(\"Hit!\"));",
                code, GUILayout.Height(92));
            GUILayout.Label(
                "<b>onHit</b> and <b>onSkill</b> fire at the impact frame of the animation. " +
                "Assign an effect prefab in the Inspector to spawn it automatically.", body);

            Section("5. Demo effects");
            GUILayout.Label("Each character can spawn a demo effect on hit/skill. The events still fire when effects are off or removed.", body);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Disable on all"))
                NoraDemoEffectsTools.DisableAll();
            if (GUILayout.Button("Enable on all"))
                NoraDemoEffectsTools.EnableAll();
            if (GUILayout.Button("Remove from project…"))
                NoraDemoEffectsTools.RemoveWithConfirmation();
            GUILayout.EndHorizontal();

            Section("6. Help");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Read the documentation"))
                EditorUtility.OpenWithDefaultApp(Documentation);
            if (GUILayout.Button("Contact support"))
                Application.OpenURL("mailto:" + SupportEmail + "?subject=Nora%20Squad%20support");
            GUILayout.EndHorizontal();
            GUILayout.Label($"Questions or bug reports: <b>{SupportEmail}</b>", body);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndScrollView();
            GUILayout.Label("Open this window again from Tools ▸ Nora Squad ▸ Welcome.", EditorStyles.centeredGreyMiniLabel);
        }

        static void Section(string label)
        {
            GUILayout.Space(14);
            GUILayout.Label(label, EditorStyles.boldLabel);
        }
    }
}
