using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NoraSquad.Demo
{
    /// <summary>
    /// Demo scene controller that shows every Nora Squad character in the project at once, smallest in front.
    /// Uses IMGUI events only, so it works with the old Input Manager and the new Input System alike.
    ///
    /// Click a name to make that character attack · 1-4 play Idle/Move/Attack/Skill on everyone · L lighting · E demo effects
    /// Mouse: left/right drag to orbit · middle drag to pan · scroll to zoom
    /// </summary>
    public class NoraLineup : MonoBehaviour
    {
        [SerializeField] List<NoraCharacter> characters = new List<NoraCharacter>();
        [SerializeField] Camera viewCamera;
        [SerializeField, Min(1)] int columns = 7;
        [SerializeField, Min(0.5f)] float spacing = 3.2f;
        [SerializeField] bool showNames = true;
        [Tooltip("Whether the demo effects play when the scene starts (toggle with E or the Effects button).")]
        [SerializeField] bool effectsOnStart;

        readonly List<NoraCharacter> spawned = new List<NoraCharacter>();
        readonly Dictionary<NoraCharacter, float> heights = new Dictionary<NoraCharacter, float>();
        float yaw = 180f, pitch = 22f, distance = 30f;
        Vector3 focus;
        NoraLighting lighting = NoraLighting.Unlit;
        GUIStyle panel, button, label, small;

        /// <summary>The character prefabs shown in the lineup.</summary>
        public IReadOnlyList<NoraCharacter> Characters => characters;

        void Start()
        {
            if (viewCamera == null)
                viewCamera = Camera.main;
            NoraCharacter.GlobalEffectsEnabled = effectsOnStart;

            // Spawn everything first, then place the smallest characters in the front row so nobody is hidden.
            foreach (var prefab in characters.Where(c => c != null))
            {
                var character = Instantiate(prefab, transform);
                character.name = prefab.name;
                heights[character] = Height(character);
                spawned.Add(character);
            }
            spawned.Sort((a, b) => heights[a].CompareTo(heights[b]));

            var rows = Mathf.CeilToInt(spawned.Count / (float)columns);
            for (var i = 0; i < spawned.Count; i++)
            {
                var row = i / columns;
                var inRow = Mathf.Min(columns, spawned.Count - row * columns);
                var col = i % columns;
                spawned[i].transform.position = transform.position +
                    new Vector3((col - (inRow - 1) * 0.5f) * spacing, 0f, (rows - 1 - row) * spacing);
                // Small random offset so the idles are not in sync.
                spawned[i].Animator.Update(Random.Range(0f, 2f));
            }

            focus = transform.position + new Vector3(0f, 1f, (rows - 1) * spacing * 0.5f);
            distance = Mathf.Max(columns, rows) * spacing * 1.25f;
        }

        static float Height(NoraCharacter character)
        {
            var renderers = character.GetComponentsInChildren<Renderer>();
            return renderers.Length == 0 ? 0f : renderers.Max(r => r.bounds.max.y) - renderers.Min(r => r.bounds.min.y);
        }

        void LateUpdate()
        {
            if (viewCamera == null)
                return;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }

        void PlayAll(NoraAction action)
        {
            foreach (var c in spawned)
                c.Play(action);
        }

        void ToggleLighting()
        {
            lighting = lighting == NoraLighting.Lit ? NoraLighting.Unlit : NoraLighting.Lit;
            foreach (var c in spawned)
                c.SetLighting(lighting);
        }

        void OnGUI()
        {
            HandleInput(Event.current);
            EnsureStyles();

            if (showNames && viewCamera != null)
            {
                foreach (var c in spawned)
                {
                    var head = c.transform.position + Vector3.up * (heights[c] + 0.25f);
                    var screen = viewCamera.WorldToScreenPoint(head);
                    if (screen.z <= 0f)
                        continue;
                    var text = DisplayName(c);
                    var size = label.CalcSize(new GUIContent(text));
                    var rect = new Rect(screen.x - size.x * 0.5f, Screen.height - screen.y - size.y, size.x, size.y);
                    if (GUI.Button(rect, text, label))
                        c.PlayAttack();
                }
            }

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 400f, Screen.height - 76f, 800f, 60f), panel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Idle", button, GUILayout.Height(40f))) PlayAll(NoraAction.Idle);
            if (GUILayout.Button("Move", button, GUILayout.Height(40f))) PlayAll(NoraAction.Move);
            if (GUILayout.Button("Attack", button, GUILayout.Height(40f))) PlayAll(NoraAction.Attack);
            if (GUILayout.Button("Skill", button, GUILayout.Height(40f))) PlayAll(NoraAction.Skill);
            if (GUILayout.Button(lighting == NoraLighting.Lit ? "Lighting: Lit" : "Lighting: Unlit", button, GUILayout.Width(190f), GUILayout.Height(40f)))
                ToggleLighting();
            if (GUILayout.Button(NoraCharacter.GlobalEffectsEnabled ? "Effects: On" : "Effects: Off", button, GUILayout.Width(130f), GUILayout.Height(40f)))
                NoraCharacter.GlobalEffectsEnabled = !NoraCharacter.GlobalEffectsEnabled;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUI.Label(new Rect(16f, 12f, Screen.width - 32f, 20f),
                $"{spawned.Count} characters · click a name to attack · 1-4 animations · L lighting · E demo effects · drag to orbit · middle drag to pan · scroll to zoom", small);
        }

        void HandleInput(Event e)
        {
            switch (e.type)
            {
                case EventType.KeyDown:
                    switch (e.keyCode)
                    {
                        case KeyCode.Alpha1: PlayAll(NoraAction.Idle); break;
                        case KeyCode.Alpha2: PlayAll(NoraAction.Move); break;
                        case KeyCode.Alpha3: case KeyCode.Space: PlayAll(NoraAction.Attack); break;
                        case KeyCode.Alpha4: PlayAll(NoraAction.Skill); break;
                        case KeyCode.L: ToggleLighting(); break;
                        case KeyCode.E: NoraCharacter.GlobalEffectsEnabled = !NoraCharacter.GlobalEffectsEnabled; break;
                        default: return;
                    }
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 2:
                    var right = viewCamera.transform.right;
                    var forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
                    focus -= (right * e.delta.x - forward * e.delta.y) * distance * 0.0015f;
                    break;
                case EventType.MouseDrag:
                    yaw += e.delta.x * 0.3f;
                    pitch = Mathf.Clamp(pitch + e.delta.y * 0.3f, 2f, 80f);
                    break;
                case EventType.ScrollWheel:
                    distance = Mathf.Clamp(distance * (1f + e.delta.y * 0.05f), 3f, 120f);
                    break;
            }
        }

        static string DisplayName(NoraCharacter character)
        {
            var name = character.name.StartsWith("NS_") ? character.name.Substring(3) : character.name;
            var result = new System.Text.StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    result.Append(' ');
                result.Append(name[i]);
            }
            return result.ToString();
        }

        void EnsureStyles()
        {
            if (panel != null)
                return;
            Texture2D Solid(Color c)
            {
                var t = new Texture2D(1, 1);
                t.SetPixel(0, 0, c);
                t.Apply();
                return t;
            }
            panel = new GUIStyle { normal = { background = Solid(new Color(0.08f, 0.09f, 0.12f, 0.82f)) }, padding = new RectOffset(8, 8, 8, 8) };
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15, fontStyle = FontStyle.Bold,
                normal = { background = Solid(new Color(0.22f, 0.25f, 0.33f)), textColor = Color.white },
                hover = { background = Solid(new Color(0.32f, 0.38f, 0.52f)), textColor = Color.white },
                active = { background = Solid(new Color(0.32f, 0.38f, 0.52f)), textColor = new Color(1f, 0.85f, 0.5f) }
            };
            label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                wordWrap = false, clipping = TextClipping.Overflow,
                normal = { background = Solid(new Color(0f, 0f, 0f, 0.45f)), textColor = Color.white },
                hover = { background = Solid(new Color(0.3f, 0.35f, 0.5f, 0.8f)), textColor = new Color(1f, 0.9f, 0.6f) },
                padding = new RectOffset(6, 6, 2, 2)
            };
            small = new GUIStyle(GUI.skin.label) { fontSize = 13, normal = { textColor = new Color(1f, 1f, 1f, 0.8f) } };
        }
    }
}
