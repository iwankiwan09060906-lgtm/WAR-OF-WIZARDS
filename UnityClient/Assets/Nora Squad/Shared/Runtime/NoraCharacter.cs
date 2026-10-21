using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace NoraSquad
{
    /// <summary>The animation states every Nora Squad character supports.</summary>
    public enum NoraAction
    {
        Idle,
        Move,
        Attack,
        Skill
    }

    /// <summary>How the hand-painted materials react to scene lighting.</summary>
    public enum NoraLighting
    {
        /// <summary>Shown exactly as painted; not affected by scene lights (cheapest).</summary>
        Unlit,
        /// <summary>Soft stylized lighting from the scene's main light, ambient light and shadows.</summary>
        Lit
    }

    /// <summary>
    /// Drop-in controller for a Nora Squad character. Plays its animations, switches lighting mode and
    /// raises <see cref="onHit"/> / <see cref="onSkill"/> at the impact frames of the animations so
    /// you can hook up your own damage, sounds or effects from the Inspector or from code.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator))]
    [HelpURL("https://github.com/nora-squad")]
    public class NoraCharacter : MonoBehaviour
    {
        static readonly int IdleState = Animator.StringToHash("Idle");
        static readonly int MoveState = Animator.StringToHash("Move");
        static readonly int AttackState = Animator.StringToHash("Attack");
        static readonly int SkillState = Animator.StringToHash("Skill");

        [Tooltip("Animation started when the character is enabled.")]
        [SerializeField] NoraAction playOnStart = NoraAction.Idle;

        [Tooltip("Keep repeating Attack/Skill instead of returning to Idle.")]
        [SerializeField] bool loopActions;

        [Tooltip("Playback speed multiplier for every animation.")]
        [SerializeField, Min(0f)] float animationSpeed = 1f;

        [Tooltip("Cross-fade duration between animations, in seconds.")]
        [SerializeField, Min(0f)] float transitionDuration = 0.1f;

        [Tooltip("Unlit shows the textures exactly as painted. Lit reacts to the scene's lights and shadows. " +
                 "Applied when playing; to preview it in the editor, set the material's Lighting slider.")]
        [SerializeField] NoraLighting lighting = NoraLighting.Unlit;

        [Header("Events")]
        [Tooltip("Raised at the impact frame of the Attack animation.")]
        public UnityEvent onHit = new UnityEvent();

        [Tooltip("Raised at the impact frame of the Skill animation.")]
        public UnityEvent onSkill = new UnityEvent();

        [Header("Optional Effects")]
        [Tooltip("Spawn the effects below on hit/skill. The events (onHit/onSkill) are raised either way.")]
        [SerializeField] bool spawnEffects = true;

        [Tooltip("Spawned at Hit Point when onHit is raised.")]
        [SerializeField] GameObject hitEffect;
        [SerializeField] Transform hitPoint;

        [Tooltip("Spawned at Skill Point when onSkill is raised.")]
        [SerializeField] GameObject skillEffect;
        [SerializeField] Transform skillPoint;

        [Tooltip("Fired from Hit Point when onHit is raised (ranged characters). Overrides Hit Effect.")]
        [SerializeField] NoraProjectile projectile;

        [Tooltip("Projectiles fly to this target. When empty they fly forward by Projectile Range.")]
        [SerializeField] Transform target;

        [SerializeField, Min(0.5f)] float projectileRange = 6f;

        [Tooltip("Spawned effects are destroyed after this many seconds.")]
        [SerializeField, Min(0.1f)] float effectLifetime = 3f;

        // Filled in by the build tools: which actions have a dedicated animation.
        [SerializeField, HideInInspector] bool hasMove = true;
        [SerializeField, HideInInspector] bool hasSkill;

        Animator animator;
        NoraAction current;
        Renderer[] renderers;
        Material[][] originalMaterials;

        // Lit copies are shared between all characters so the SRP Batcher keeps batching them.
        static readonly Dictionary<Material, Material> LitCopies = new Dictionary<Material, Material>();
        static readonly int LightingId = Shader.PropertyToID("_Lighting");

        /// <summary>
        /// Global switch for the optional effects of every character (the events are still raised).
        /// The demo scenes toggle this with their "Effects" button.
        /// </summary>
        public static bool GlobalEffectsEnabled { get; set; } = true;

        // Reset the shared state when entering play mode with domain reload disabled (Enter Play Mode Options).
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            GlobalEffectsEnabled = true;
            LitCopies.Clear();
        }

        /// <summary>Per-character switch for the optional effects.</summary>
        public bool SpawnEffects
        {
            get => spawnEffects;
            set => spawnEffects = value;
        }

        bool EffectsActive => spawnEffects && GlobalEffectsEnabled;

        /// <summary>The animation that is currently playing.</summary>
        public NoraAction CurrentAction => current;

        /// <summary>The active lighting mode.</summary>
        public NoraLighting Lighting => lighting;

        /// <summary>Optional target for projectiles; when null they fly straight ahead.</summary>
        public Transform Target
        {
            get => target;
            set => target = value;
        }

        /// <summary>True when attacking spawns an effect or fires a projectile (and effects are enabled).</summary>
        public bool HasHitEffect => EffectsActive && (hitEffect != null || projectile != null);

        /// <summary>True when this character has a skill animation.</summary>
        public bool HasSkill => hasSkill;

        /// <summary>True when this character has a move animation.</summary>
        public bool HasMove => hasMove;

        public Animator Animator => animator != null ? animator : animator = GetComponent<Animator>();

        void Awake()
        {
            animator = GetComponent<Animator>();
            animator.applyRootMotion = false;
            if (lighting != NoraLighting.Unlit)
                SetLighting(lighting);
        }

        void OnEnable()
        {
            SetAnimationSpeed(animationSpeed);
            Play(playOnStart, 0f);
        }

        void Update()
        {
            if (loopActions || (current != NoraAction.Attack && current != NoraAction.Skill))
                return;

            // One-shot actions fall back to Idle once they have finished.
            var info = Animator.GetCurrentAnimatorStateInfo(0);
            if (!Animator.IsInTransition(0) && info.normalizedTime >= 1f && info.shortNameHash == StateHash(current))
                Play(NoraAction.Idle);
        }

        public void PlayIdle() => Play(NoraAction.Idle);
        public void PlayMove() => Play(NoraAction.Move);
        public void PlayAttack() => Play(NoraAction.Attack);
        public void PlaySkill() => Play(NoraAction.Skill);

        /// <summary>Cross-fades to <paramref name="action"/>. Actions the character lacks play Idle instead.</summary>
        public void Play(NoraAction action) => Play(action, transitionDuration);

        void Play(NoraAction action, float fade)
        {
            if ((action == NoraAction.Move && !hasMove) || (action == NoraAction.Skill && !hasSkill))
                action = NoraAction.Idle;
            var hash = StateHash(action);

            current = action;
            // Restart the state even if it is already playing so repeated attacks always play from frame 0.
            Animator.CrossFadeInFixedTime(hash, fade, 0, 0f);
        }

        /// <summary>Sets the playback speed multiplier for all animations.</summary>
        public void SetAnimationSpeed(float speed)
        {
            animationSpeed = Mathf.Max(0f, speed);
            Animator.speed = animationSpeed;
        }

        /// <summary>
        /// Switches the character between <see cref="NoraLighting.Unlit"/> (as painted) and <see cref="NoraLighting.Lit"/>
        /// (reacts to scene lights). Uses shared material copies, so it is cheap to call on many characters.
        /// </summary>
        public void SetLighting(NoraLighting mode)
        {
            lighting = mode;
            if (renderers == null)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
                originalMaterials = new Material[renderers.Length][];
                for (var i = 0; i < renderers.Length; i++)
                    originalMaterials[i] = renderers[i].sharedMaterials;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                var source = originalMaterials[i];
                var target = new Material[source.Length];
                for (var m = 0; m < source.Length; m++)
                    target[m] = mode == NoraLighting.Lit ? LitCopy(source[m]) : source[m];
                renderers[i].sharedMaterials = target;
            }
        }

        static Material LitCopy(Material source)
        {
            if (source == null || !source.HasProperty(LightingId) || source.GetFloat(LightingId) >= 0.5f)
                return source;
            if (!LitCopies.TryGetValue(source, out var copy) || copy == null)
            {
                copy = new Material(source) { name = source.name + " (Lit)", hideFlags = HideFlags.DontSave };
                copy.SetFloat(LightingId, 1f);
                LitCopies[source] = copy;
            }
            return copy;
        }

        // ---- Animation event receivers (called from the clips' import settings) ----

        void OnHit()
        {
            if (EffectsActive)
            {
                if (projectile != null)
                    Fire();
                else
                    Spawn(hitEffect, hitPoint);
            }
            onHit.Invoke();
        }

        void Fire()
        {
            var from = hitPoint != null ? hitPoint.position : transform.position + Vector3.up;
            var to = target != null
                ? target.position + Vector3.up * (from.y - transform.position.y) * 0.5f
                : transform.position + transform.forward * projectileRange + Vector3.up * (from.y - transform.position.y) * 0.5f;
            NoraProjectile.Launch(projectile, from, to);
        }

        void OnSkill()
        {
            if (EffectsActive)
                Spawn(skillEffect, skillPoint);
            onSkill.Invoke();
        }

        // Grukal's leap has a take-off event before the landing; nothing to do by default.
        void OnSkillStart() { }

        void Spawn(GameObject effect, Transform point)
        {
            if (effect == null)
                return;
            var at = point != null ? point : transform;
            var instance = Instantiate(effect, at.position, at.rotation);
            Destroy(instance, effectLifetime);
        }

        static int StateHash(NoraAction action) => action switch
        {
            NoraAction.Move => MoveState,
            NoraAction.Attack => AttackState,
            NoraAction.Skill => SkillState,
            _ => IdleState
        };

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying && animator != null)
                SetAnimationSpeed(animationSpeed);
        }

        public void EditorSetActions(bool move, bool skill)
        {
            hasMove = move;
            hasSkill = skill;
        }

        public void EditorSetEffects(GameObject hit, Transform hitAt, GameObject skill, Transform skillAt, NoraProjectile shot)
        {
            projectile = shot;
            hitEffect = hit;
            hitPoint = hitAt;
            skillEffect = skill;
            skillPoint = skillAt;
        }
#endif
    }
}
