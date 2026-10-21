using System.Collections.Generic;
using UnityEngine;

namespace NoraSquad.Demo
{
    /// <summary>
    /// Demo scene controller: browse characters, play animations, switch lighting mode and orbit the camera.
    /// Uses IMGUI events only, so it works with the old Input Manager and the new Input System alike.
    ///
    /// Keys: ← / → (or A / D) switch character · 1-4 Idle/Move/Attack/Skill · Space attack · L lighting · E demo effects
    /// Mouse: drag to orbit · scroll to zoom
    /// </summary>
    public class NoraShowcase : MonoBehaviour
    {
        [SerializeField] List<NoraCharacter> characters = new List<NoraCharacter>();
        [SerializeField] Transform spawnPoint;
        [SerializeField] Camera viewCamera;
        [SerializeField] float orbitSpeed = 0.3f;
        [SerializeField] Vector2 pitchLimits = new Vector2(-5f, 60f);
        [Tooltip("Whether the demo effects play when the scene starts (toggle with E or the Effects button).")]
        [SerializeField] bool effectsOnStart = true;

        NoraCharacter current;
        int index;
        float yaw = 180f, pitch = 10f, distance = 6f, minDistance = 1f, maxDistance = 30f;
        Vector3 focus;
        float speed = 1f;
        NoraLighting lighting = NoraLighting.Unlit;
        GUIStyle panel, title, button, small;

        /// <summary>The character prefabs the showcase cycles through.</summary>
        public IReadOnlyList<NoraCharacter> Characters => characters;

        void Start()
        {
            if (viewCamera == null)
                viewCamera = Camera.main;
            NoraCharacter.GlobalEffectsEnabled = effectsOnStart;
            Show(0);
        }

        void Show(int newIndex)
        {
            if (characters.Count == 0)
                return;
            index = (newIndex % characters.Count + characters.Count) % characters.Count;
            if (current != null)
                Destroy(current.gameObject);

            var at = spawnPoint != null ? spawnPoint : transform;
            current = Instantiate(characters[index], at.position, at.rotation);
            current.SetAnimationSpeed(speed);
            if (lighting != NoraLighting.Unlit)
                current.SetLighting(lighting);
            Frame();
        }

        void Frame()
        {
            var renderers = current.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers)
                bounds.Encapsulate(r.bounds);
            focus = bounds.center;
            var radius = bounds.extents.magnitude;
            distance = radius / Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            minDistance = radius * 0.8f;
            maxDistance = radius * 8f;
        }

        void LateUpdate()
        {
            if (viewCamera == null)
                return;
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            viewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }

        void OnGUI()
        {
            HandleInput(Event.current);
            EnsureStyles();
            var scale = Mathf.Max(1f, Screen.height / 900f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            var w = Screen.width / scale;
            var h = Screen.height / scale;

            if (current == null)
                return;

            // Header: character switcher
            GUILayout.BeginArea(new Rect(w * 0.5f - 230f, 16f, 460f, 64f), panel);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", button, GUILayout.Width(56f), GUILayout.Height(44f))) Show(index - 1);
            GUILayout.Label($"{DisplayName(characters[index])}\n<size=12>{index + 1} / {characters.Count}</size>", title, GUILayout.Height(44f));
            if (GUILayout.Button("▶", button, GUILayout.Width(56f), GUILayout.Height(44f))) Show(index + 1);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            // Footer: actions and options
            GUILayout.BeginArea(new Rect(w * 0.5f - 330f, h - 128f, 660f, 112f), panel);
            GUILayout.BeginHorizontal();
            ActionButton("Idle", NoraAction.Idle, true);
            ActionButton("Move", NoraAction.Move, current.HasMove);
            ActionButton("Attack", NoraAction.Attack, true);
            ActionButton("Skill", NoraAction.Skill, current.HasSkill);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(lighting == NoraLighting.Lit ? "Lighting: Lit" : "Lighting: Unlit", button, GUILayout.Width(200f), GUILayout.Height(32f)))
                ToggleLighting();
            GUILayout.Space(8f);
            if (GUILayout.Button(NoraCharacter.GlobalEffectsEnabled ? "Effects: On" : "Effects: Off", button, GUILayout.Width(130f), GUILayout.Height(32f)))
                NoraCharacter.GlobalEffectsEnabled = !NoraCharacter.GlobalEffectsEnabled;
            GUILayout.Space(8f);
            GUILayout.Label($"Speed {speed:0.0}x", small, GUILayout.Width(80f), GUILayout.Height(32f));
            var newSpeed = GUILayout.HorizontalSlider(speed, 0.1f, 2f, GUILayout.Width(150f));
            if (!Mathf.Approximately(newSpeed, speed))
            {
                speed = newSpeed;
                current.SetAnimationSpeed(speed);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();

            GUI.Label(new Rect(16f, h - 28f, w - 32f, 20f),
                "← → switch · 1-4 animations · Space attack · L lighting · E demo effects · drag to orbit · scroll to zoom", small);
        }

        void ActionButton(string label, NoraAction action, bool available)
        {
            GUI.enabled = available;
            if (GUILayout.Button(label, button, GUILayout.Height(44f)))
                current.Play(action);
            GUI.enabled = true;
        }

        void ToggleLighting()
        {
            lighting = lighting == NoraLighting.Lit ? NoraLighting.Unlit : NoraLighting.Lit;
            current.SetLighting(lighting);
        }

        void HandleInput(Event e)
        {
            if (current == null)
                return;
            switch (e.type)
            {
                case EventType.KeyDown:
                    switch (e.keyCode)
                    {
                        case KeyCode.LeftArrow: case KeyCode.A: Show(index - 1); break;
                        case KeyCode.RightArrow: case KeyCode.D: Show(index + 1); break;
                        case KeyCode.Alpha1: current.PlayIdle(); break;
                        case KeyCode.Alpha2: current.PlayMove(); break;
                        case KeyCode.Alpha3: case KeyCode.Space: current.PlayAttack(); break;
                        case KeyCode.Alpha4: current.PlaySkill(); break;
                        case KeyCode.L: ToggleLighting(); break;
                        case KeyCode.E: NoraCharacter.GlobalEffectsEnabled = !NoraCharacter.GlobalEffectsEnabled; break;
                        default: return;
                    }
                    e.Use();
                    break;
                case EventType.MouseDrag when e.button == 0 || e.button == 1:
                    yaw += e.delta.x * orbitSpeed;
                    pitch = Mathf.Clamp(pitch + e.delta.y * orbitSpeed, pitchLimits.x, pitchLimits.y);
                    break;
                case EventType.ScrollWheel:
                    distance = Mathf.Clamp(distance * (1f + e.delta.y * 0.05f), minDistance, maxDistance);
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
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.08f, 0.09f, 0.12f, 0.82f));
            bg.Apply();
            var buttonBg = new Texture2D(1, 1);
            buttonBg.SetPixel(0, 0, new Color(0.22f, 0.25f, 0.33f, 1f));
            buttonBg.Apply();
            var buttonHover = new Texture2D(1, 1);
            buttonHover.SetPixel(0, 0, new Color(0.32f, 0.38f, 0.52f, 1f));
            buttonHover.Apply();

            panel = new GUIStyle { normal = { background = bg }, padding = new RectOffset(10, 10, 10, 10) };
            title = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, fontSize = 22, fontStyle = FontStyle.Bold, richText = true,
                normal = { textColor = Color.white }
            };
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 16, fontStyle = FontStyle.Bold,
                normal = { background = buttonBg, textColor = Color.white },
                hover = { background = buttonHover, textColor = Color.white },
                active = { background = buttonHover, textColor = new Color(1f, 0.85f, 0.5f) }
            };
            small = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(1f, 1f, 1f, 0.75f) }
            };
        }
    }
}
