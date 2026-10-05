// KayKit Adventurers 2.0 (CC0) 캐릭터 → 미니언 모델 프리팹 생성 (가이드: Docs/Art/AssetIntegrationGuide.md §3.2)
//   Spellbound > Assets > Build KayKit Minion Models
//     MDL_Minion_Melee  ← Knight (한손검 + 방패), 키 1.2m
//     MDL_Minion_Ranged ← Rogue_Hooded (활), 키 1.0m
//     MDL_Minion_Brute  ← Barbarian (양손도끼), 키 2.0m (크기 확대)
//   · Animator 컨트롤러에 int 파라미터 State (0 이동 · 1 공격 · 2 돌파 · 3 빙결) — Rig_Medium 공용 애니메이션 사용
//   · 프리팹 루트 = 발바닥 중앙, 앞 = +Z. 다시 실행하면 프리팹 · 컨트롤러를 덮어쓴다.
//   · Free 팩에는 공격 클립이 없어 Throw로 대체한다. 공격 애니메이션 팩을 넣으면 AttackClip만 바꾸면 된다.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundMinionModelBuilder
    {
        private const string KayKit = "Assets/KayKit_Adventurers_2.0_FREE";
        private const string CharacterFbx = KayKit + "/Characters/fbx/";
        private const string WeaponFbx = KayKit + "/Assets/fbx(unity)/";
        private const string GeneralAnim = KayKit + "/Animations/fbx/Rig_Medium/Rig_Medium_General.fbx";
        private const string MovementAnim = KayKit + "/Animations/fbx/Rig_Medium/Rig_Medium_MovementBasic.fbx";
        private const string PrefabFolder = "Assets/Prefabs/Minion";
        private const string ControllerFolder = "Assets/Art/Models/Minion";

        private const string WalkClip = "Walking_A";
        private const string RunClip = "Running_A";
        private const string IdleClip = "Idle_A";

        private sealed class Spec
        {
            public string Name;
            public string Character;
            public float Height;
            public string AttackClip;
            /// <summary>공격 간격(초) — 공격 클립 1회 재생 시간을 여기에 맞춘다</summary>
            public float AttackInterval;
            public (string fbx, string bone)[] Weapons;
        }

        private static readonly Spec[] Specs =
        {
            new Spec { Name = "MDL_Minion_Melee", Character = "Knight", Height = 1.2f, AttackClip = "Throw", AttackInterval = 1.0f,
                       Weapons = new[] { ("sword_1handed", "handslot.r"), ("shield_badge_color", "handslot.l") } },
            new Spec { Name = "MDL_Minion_Ranged", Character = "Rogue_Hooded", Height = 1.0f, AttackClip = "Throw", AttackInterval = 1.2f,
                       Weapons = new[] { ("bow_withString", "handslot.l") } },
            new Spec { Name = "MDL_Minion_Brute", Character = "Barbarian", Height = 2.0f, AttackClip = "Throw", AttackInterval = 1.4f,
                       Weapons = new[] { ("axe_2handed", "handslot.r") } },
        };

        [MenuItem("Spellbound/Assets/Build KayKit Minion Models (MDL_Minion_*)", priority = 121)]
        public static void BuildAll()
        {
            SpellboundSetupWizard.EnsureFolder(PrefabFolder);
            SpellboundSetupWizard.EnsureFolder(ControllerFolder);
            EnsureLoop(GeneralAnim, IdleClip, "Throw");
            EnsureLoop(MovementAnim, WalkClip, RunClip);

            var clips = LoadClips(GeneralAnim).Concat(LoadClips(MovementAnim)).ToList();
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                foreach (var spec in Specs) Build(spec, clips, preview);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }

            AssetDatabase.SaveAssets();
            SpellboundSetupWizard.RefreshVfxCatalogMenu();
            Debug.Log("[Minion Models] MDL_Minion_Melee / Ranged / Brute 생성 완료 → " + PrefabFolder);
        }

        private static void Build(Spec spec, List<AnimationClip> clips, UnityEngine.SceneManagement.Scene preview)
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharacterFbx + spec.Character + ".fbx");
            if (modelAsset == null)
            {
                Debug.LogError("[Minion Models] 캐릭터 FBX 없음: " + CharacterFbx + spec.Character + ".fbx");
                return;
            }

            var root = new GameObject(spec.Name);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, preview);
            model.transform.SetParent(root.transform, false);

            // 앞(+Z) 맞추기: 발 → 발끝 방향이 캐릭터의 앞
            var foot = FindDeep(model.transform, "foot.l");
            var toes = FindDeep(model.transform, "toes.l");
            if (foot != null && toes != null)
            {
                Vector3 fwd = toes.position - foot.position;
                fwd.y = 0f;
                if (fwd.sqrMagnitude > 1e-8f) model.transform.rotation = Quaternion.FromToRotation(fwd.normalized, Vector3.forward) * model.transform.rotation;
            }

            // 크기: 무기 붙이기 전 몸 높이로 목표 키에 맞춤, 발바닥을 y = 0에
            var b = RendererBounds(model);
            float scale = b.size.y > 1e-4f ? spec.Height / b.size.y : 1f;
            model.transform.localScale *= scale;
            b = RendererBounds(model);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);

            foreach (var (fbx, bone) in spec.Weapons)
            {
                var weaponAsset = AssetDatabase.LoadAssetAtPath<GameObject>(WeaponFbx + fbx + ".fbx");
                var slot = FindDeep(model.transform, bone);
                if (weaponAsset == null || slot == null)
                {
                    Debug.LogWarning($"[Minion Models] {spec.Name}: 무기 {fbx} 또는 본 {bone} 없음 — 건너뜀");
                    continue;
                }
                var weapon = (GameObject)PrefabUtility.InstantiatePrefab(weaponAsset, preview);
                weapon.transform.SetParent(slot, false);
            }

            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = BuildController(spec, clips);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{spec.Name}.prefab");
            Object.DestroyImmediate(root);
            Debug.Log($"[Minion Models] {spec.Name} ← {spec.Character} (키 {spec.Height}m, ×{scale:0.###})");
        }

        private static AnimatorController BuildController(Spec spec, List<AnimationClip> clips)
        {
            string path = $"{ControllerFolder}/AC_{spec.Name.Substring(4)}.controller";
            AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("State", AnimatorControllerParameterType.Int);
            var sm = ac.layers[0].stateMachine;

            var attack = FindClip(clips, spec.AttackClip);
            var states = new[]
            {
                AddState(sm, "Walk", FindClip(clips, WalkClip), 1f),
                AddState(sm, "Attack", attack, attack != null ? attack.length / spec.AttackInterval : 1f),
                AddState(sm, "Breakthrough", FindClip(clips, RunClip), 1f),
                AddState(sm, "Frozen", FindClip(clips, IdleClip), 0f),
            };
            sm.defaultState = states[0];

            for (int i = 0; i < states.Length; i++)
            {
                var t = sm.AddAnyStateTransition(states[i]);
                t.AddCondition(AnimatorConditionMode.Equals, i, "State");
                t.hasExitTime = false;
                t.duration = i == 3 ? 0f : 0.1f;
                t.canTransitionToSelf = false;
            }
            return ac;
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, AnimationClip clip, float speed)
        {
            var state = sm.AddState(name);
            state.motion = clip;
            state.speed = speed;
            if (clip == null) Debug.LogWarning($"[Minion Models] {name} 상태에 쓸 클립을 찾지 못함");
            return state;
        }

        /// <summary>지정 클립을 Loop Time으로 (FBX 임포트 설정 변경 후 재임포트)</summary>
        private static void EnsureLoop(string fbxPath, params string[] names)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;
            var list = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            bool changed = importer.clipAnimations.Length == 0;
            foreach (var c in list)
            {
                if (!names.Any(n => ClipMatches(c.name, n)) || c.loopTime) continue;
                c.loopTime = true;
                changed = true;
            }
            if (!changed) return;
            importer.clipAnimations = list;
            importer.SaveAndReimport();
        }

        private static List<AnimationClip> LoadClips(string fbxPath) =>
            AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();

        private static AnimationClip FindClip(List<AnimationClip> clips, string name) => clips.FirstOrDefault(c => ClipMatches(c.name, name));

        // FBX 테이크 이름이 "Rig_Medium|Walking_A" 형태일 수 있음
        private static bool ClipMatches(string clipName, string name) => clipName == name || clipName.EndsWith("|" + name);

        /// <summary>실제 정점 기준 월드 바운드 (SkinnedMeshRenderer.bounds는 여유가 붙어 키 · 발 위치가 어긋남). 에디터에선 바인드 포즈 그대로</summary>
        internal static Bounds RendererBounds(GameObject go)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindDeep(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
