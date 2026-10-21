// 소환수 · 설치물 모델 (§25.4 MDL_{Category}_{Name}, 루트 = 발바닥 중앙, 앞 = +Z)
//   MDL_Summon_AxeOrc     ← 3D Stylized Orc (Humanoid) — 걷기 · 도끼 공격 · 달리기 · 대기
//   MDL_Summon_Dragon     ← FourEvilDragonsPBR DragonUsurperMesh (Red) — 날기 · 비행 화염 · 공중 대기
//   MDL_Summon_Skeleton   ← Mini Simple Characters Skeleton (Humanoid) + 나무 칼 · 방패, 공격은 오크 도끼질을 리타깃
//   MDL_Structure_Turret  ← StoneKeep Tower_c (탑 하나) + 꼭대기 Nora Squad Archer (대기 · 활쏘기)
//   MDL_Structure_SummonHut ← ZNS3D 나무 벽 · 지붕으로 지은 오두막 (공격 타워 높이의 80%) + 문 앞 소환 포털
// Animator int 파라미터 State: 0 이동(설치물은 대기) · 1 공격 · 2 돌파 · 3 빙결 · 4 회오리 (UnitVisualState)

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static partial class SpellboundSkillAssetBuilder
    {
        private const string OrcDir = "Assets/3D Stylized Orc - animations+PBR(13.6k faces)/";
        private const string DragonDir = "Assets/FourEvilDragonsPBR/";
        private const string SkelDir = "Assets/Mini Simple Characters Skeleton Demo/";
        private const string ArcherDir = "Assets/Nora Squad/Characters/Archer/";
        private const string StoneKeep = "Assets/StoneKeep/Prefabs/";

        /// <summary>아군 공격 타워 아트 높이 (ArenaStateView HP바 기준 4.8m) — 소환탑은 그 80%</summary>
        private const float AttackTowerHeight = 4.8f;

        private static void BuildSummonModels()
        {
            var orcAttack = LoadClip(OrcDir + "orc/Atack1atack1.fbx", "atack1");
            BuildOrc(orcAttack);
            BuildDragon();
            BuildSkeleton(LoadClip(OrcDir + "orc/Atack2atack2.fbx", "atack2"));
            BuildTurret();
            BuildHut();
        }

        // ── 공용 ─────────────────────────────────────────────

        private static AnimationClip LoadClip(string fbxPath, string name)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            var clip = clips.FirstOrDefault(c => c.name == name || c.name.EndsWith("|" + name)) ?? clips.FirstOrDefault();
            if (clip == null) Debug.LogWarning($"[Skill Builder] 클립 없음: {fbxPath} ({name})");
            return clip;
        }

        /// <summary>클립을 Loop Time으로 (임포트 설정 변경 후 재임포트)</summary>
        private static void EnsureLoop(string fbxPath, params string[] names)
        {
            var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
            if (importer == null) return;
            var list = importer.clipAnimations.Length > 0 ? importer.clipAnimations : importer.defaultClipAnimations;
            bool changed = importer.clipAnimations.Length == 0;
            foreach (var c in list)
            {
                if (names.Length > 0 && !names.Any(n => c.name == n || c.name.EndsWith("|" + n))) continue;
                if (c.loopTime) continue;
                c.loopTime = true;
                changed = true;
            }
            if (!changed) return;
            importer.clipAnimations = list;
            importer.SaveAndReimport();
        }

        /// <summary>키 height로 맞추고 발바닥 중앙을 원점에</summary>
        private static float FitHeight(GameObject model, float height)
        {
            var b = SpellboundMinionModelBuilder.RendererBounds(model);
            float scale = b.size.y > 1e-4f ? height / b.size.y : 1f;
            model.transform.localScale *= scale;
            b = SpellboundMinionModelBuilder.RendererBounds(model);
            model.transform.position -= new Vector3(b.center.x, b.min.y, b.center.z);
            return scale;
        }

        private struct StateSpec
        {
            public string Name;
            public AnimationClip Clip;
            public float Speed;
        }

        private static AnimatorController BuildStateController(string name, StateSpec[] states)
        {
            string path = $"{CtrlOut}/AC_{name}.controller";
            AssetDatabase.DeleteAsset(path);
            var ac = AnimatorController.CreateAnimatorControllerAtPath(path);
            ac.AddParameter("State", AnimatorControllerParameterType.Int);
            var sm = ac.layers[0].stateMachine;
            var created = new AnimatorState[states.Length];
            for (int i = 0; i < states.Length; i++)
            {
                var st = sm.AddState(states[i].Name);
                st.motion = states[i].Clip;
                st.speed = states[i].Speed;
                created[i] = st;
                if (states[i].Clip == null) Debug.LogWarning($"[Skill Builder] {name}.{states[i].Name} 클립 없음");
            }
            sm.defaultState = created[0];
            for (int i = 0; i < created.Length; i++)
            {
                var t = sm.AddAnyStateTransition(created[i]);
                t.AddCondition(AnimatorConditionMode.Equals, i, "State");
                t.hasExitTime = false;
                t.duration = i == 3 ? 0f : 0.12f;
                t.canTransitionToSelf = false;
            }
            return ac;
        }

        private static void SetupAnimator(GameObject model, RuntimeAnimatorController controller)
        {
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            // 클립 속 애니메이션 이벤트(OnHit 등) 수신기 — 없으면 오류 로그 → Error Pause
            if (animator.GetComponent<SpellboundVR.Presentation.AnimationEventSink>() == null)
                animator.gameObject.AddComponent<SpellboundVR.Presentation.AnimationEventSink>();
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }

        // ── S11 도끼 전사 (오크) ──────────────────────────────

        private static void BuildOrc(AnimationClip attack)
        {
            var root = NewRoot("MDL_Summon_AxeOrc");
            var model = Add(root, OrcDir + "orc.prefab", 1f, name: "Orc");
            if (model == null) return;
            FitHeight(model, 2.2f);
            var ac = BuildStateController("Summon_AxeOrc", new[]
            {
                new StateSpec { Name = "Walk", Clip = LoadClip(OrcDir + "orc/WalkWalk.fbx", "Walk"), Speed = 1f },
                new StateSpec { Name = "Attack", Clip = attack, Speed = attack != null ? attack.length / 1.6f : 1f }, // 공격 간격 1.6초에 한 번 휘두름
                new StateSpec { Name = "Breakthrough", Clip = LoadClip(OrcDir + "orc/RunningRunning.fbx", "Running"), Speed = 1f },
                new StateSpec { Name = "Frozen", Clip = LoadClip(OrcDir + "orc/IdleIdle.fbx", "Idle"), Speed = 0f },
                new StateSpec { Name = "Stunned", Clip = LoadClip(OrcDir + "orc/IdleIdle.fbx", "Idle"), Speed = 2f },
            });
            SetupAnimator(model, ac);
            Save(root, ModelOut);
        }

        // ── S12 드래곤 (마법사 대신) ──────────────────────────

        private static void BuildDragon()
        {
            var root = NewRoot("MDL_Summon_Dragon");
            var model = Add(root, DragonDir + "Prefab/DragonUsurper/Red.prefab", 1f, name: "DragonUsurper");
            if (model == null) return;
            // 날개 폭 · 길이 약 6m
            var b = SpellboundMinionModelBuilder.RendererBounds(model);
            float scale = 6f / Mathf.Max(0.01f, Mathf.Max(b.size.z, b.size.x));
            model.transform.localScale *= scale;
            b = SpellboundMinionModelBuilder.RendererBounds(model);
            model.transform.position -= new Vector3(b.center.x, b.center.y, b.center.z); // 몸 중심 = 비행 높이
            string anim = DragonDir + "Animations/DragonUsurper/";
            var flame = LoadClip(anim + "FlyFlame.fbx", "Fly Flame Attack");
            var ac = BuildStateController("Summon_Dragon", new[]
            {
                new StateSpec { Name = "FlyForward", Clip = LoadClip(anim + "FlyForward.fbx", "Fly Forward"), Speed = 1f },
                new StateSpec { Name = "FlyFlame", Clip = flame, Speed = flame != null ? flame.length / 2f : 1f }, // 공격 간격 2초
                new StateSpec { Name = "FlyFast", Clip = LoadClip(anim + "FlyForward.fbx", "Fly Forward"), Speed = 1.4f },
                new StateSpec { Name = "Frozen", Clip = LoadClip(anim + "FlyIdle.fbx", "Fly Float"), Speed = 0f },
                new StateSpec { Name = "Stunned", Clip = LoadClip(anim + "getHit.fbx", ""), Speed = 1f },
            });
            SetupAnimator(model, ac);
            Save(root, ModelOut);
        }

        // ── H06 해골 (소환탑이 내보내는 작은 근접) ─────────────────

        private static void BuildSkeleton(AnimationClip attack)
        {
            string fbx = SkelDir + "Models/Animations/mini simple characters animations.fbx";
            var root = NewRoot("MDL_Summon_Skeleton");
            var model = Add(root, SkelDir + "Prefabs/Characters/mini simple skeleton demo.prefab", 1f, name: "Skeleton");
            if (model == null) return;
            FitHeight(model, 1.1f);

            var animator = model.GetComponentInChildren<Animator>();
            if (animator != null && animator.isHuman)
            {
                var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                var off = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                if (hand != null) AttachProp(hand, SkelDir + "Prefabs/Props/sword_wood.prefab");
                if (off != null) AttachProp(off, SkelDir + "Prefabs/Props/shield_wood.prefab");
            }

            var ac = BuildStateController("Summon_Skeleton", new[]
            {
                new StateSpec { Name = "Walk", Clip = LoadClip(fbx, "Walk"), Speed = 1.2f },
                new StateSpec { Name = "Attack", Clip = attack, Speed = attack != null ? attack.length / 1f : 1f },
                new StateSpec { Name = "Run", Clip = LoadClip(fbx, "Run"), Speed = 1f },
                new StateSpec { Name = "Frozen", Clip = LoadClip(fbx, "Idle"), Speed = 0f },
                new StateSpec { Name = "Stunned", Clip = LoadClip(fbx, "Idle"), Speed = 2f },
            });
            SetupAnimator(model, ac);
            Save(root, ModelOut);
        }

        private static void AttachProp(Transform bone, string prefabPath)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (src == null) return;
            var prop = (GameObject)PrefabUtility.InstantiatePrefab(src, s_scene);
            PrefabUtility.UnpackPrefabInstance(prop, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            StripScripts(prop);
            prop.transform.SetParent(bone, false);
            prop.transform.localPosition = Vector3.zero;
            prop.transform.localRotation = Quaternion.identity;
            // 원래 크기(월드) 유지 — 뼈 스케일을 상쇄
            var lossy = bone.lossyScale;
            prop.transform.localScale = new Vector3(src.transform.localScale.x / Mathf.Max(1e-4f, lossy.x),
                                                    src.transform.localScale.y / Mathf.Max(1e-4f, lossy.y),
                                                    src.transform.localScale.z / Mathf.Max(1e-4f, lossy.z)) * 0.6f;
        }

        // ── S13 터렛 (StoneKeep 탑 + Nora Squad 궁수) ─────────────

        private static void BuildTurret()
        {
            var root = NewRoot("MDL_Structure_Turret");
            var tower = Add(root, StoneKeep + "Structures/Tower_c.prefab", 1f, name: "StoneTower");
            if (tower == null) return;
            foreach (var a in tower.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a); // 깃발 뼈대 Animator 제거 (궁수 Animator만 남김)

            // Tower_c 원본 단위: 1층 바닥 y = 2, 꼭대기 몸통(Tower_d) 윗면 y = 17, 원뿔 지붕(Top_a) 17~32, 성가퀴(Top_b) 22~27
            // → 원뿔 지붕을 걷어내고 성가퀴를 17로 내려 그 위에 바닥을 깔아 궁수가 서는 옥상을 만든다
            const float groundY = 2f, roofY = 17f, crenelTop = 22f;
            foreach (var n in new[] { "Top_a", "Window_roof", "Rubble", "Rubble (1)" })
            {
                var t = tower.transform.Find(n);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
            var crenel = tower.transform.Find("Top_b");
            if (crenel != null) crenel.localPosition = new Vector3(crenel.localPosition.x, roofY, crenel.localPosition.z);
            var floor = Add(tower, StoneKeep + "Floor_a.prefab", 1f, new Vector3(0f, roofY, 0f), name: "RoofFloor");
            if (floor != null) floor.transform.localScale = Vector3.one;

            // 지면 위 높이 4.6m (성가퀴 윗면 기준), 탑 중심 = 원점
            float scale = 4.6f / (crenelTop - groundY);
            tower.transform.localScale = Vector3.one * scale;
            tower.transform.localPosition = new Vector3(0f, -groundY * scale, 0f);
            float topY = (roofY - groundY) * scale;
            Vector3 topCenter = Vector3.zero;

            string anim = ArcherDir + "Models/";
            EnsureLoop(anim + "NS_Archer@Attack.fbx");
            var archerRoot = Child(root, "ArcherMount", new Vector3(topCenter.x, topY, topCenter.z));
            var archer = Add(archerRoot, ArcherDir + "Prefabs/NS_Archer.prefab", 1f, name: "Archer");
            if (archer != null)
            {
                archer.transform.localPosition = Vector3.zero;
                var b = SpellboundMinionModelBuilder.RendererBounds(archer);
                float s = 1.6f / Mathf.Max(0.01f, b.size.y);
                archer.transform.localScale *= s;
                b = SpellboundMinionModelBuilder.RendererBounds(archer);
                archer.transform.position += new Vector3(archerRoot.transform.position.x - b.center.x, archerRoot.transform.position.y - b.min.y, archerRoot.transform.position.z - b.center.z);
                var attack = LoadClip(anim + "NS_Archer@Attack.fbx", "NS_Archer_Attack");
                var idle = LoadClip(anim + "NS_Archer@Idle.fbx", "NS_Archer_Idle");
                var ac = BuildStateController("Structure_Turret", new[]
                {
                    new StateSpec { Name = "Idle", Clip = idle, Speed = 1f },
                    new StateSpec { Name = "Shoot", Clip = attack, Speed = attack != null ? attack.length / 1f : 1f }, // 공격 간격 1초
                    new StateSpec { Name = "Idle2", Clip = idle, Speed = 1f },
                    new StateSpec { Name = "Frozen", Clip = idle, Speed = 0f },
                    new StateSpec { Name = "Stunned", Clip = idle, Speed = 0f },
                });
                SetupAnimator(archer, ac);
            }
            Save(root, ModelOut);
            Debug.Log($"[Skill Builder] 터렛: 탑 ×{scale:0.###}, 궁수 발판 높이 {topY:0.00}m");
        }

        /// <summary>탑 꼭대기 바닥 높이: 탑 중심 근처(가로 · 세로의 25% 안) 정점 중 가장 높은 곳 — 가장자리 성가퀴는 제외된다</summary>
        private static float TopFloorHeight(GameObject tower, out Vector3 center)
        {
            var b = SpellboundMinionModelBuilder.RendererBounds(tower);
            center = b.center;
            float reach = Mathf.Min(b.size.x, b.size.z) * 0.18f;
            float best = float.MinValue;
            foreach (var r in tower.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (Mathf.Abs(p.x - center.x) > reach || Mathf.Abs(p.z - center.z) > reach) continue;
                    if (p.y > best) best = p.y;
                }
            }
            return best > float.MinValue ? best : b.max.y;
        }

        // ── H06 소환탑 (ZNS3D 오두막) ──────────────────────────

        private static void BuildHut()
        {
            const float side = 2.9f;      // 바닥 한 변
            var root = NewRoot("MDL_Structure_SummonHut");
            var house = Child(root, "House");

            // 벽: Wall_Wood_1 두 장씩 네 면
            for (int face = 0; face < 4; face++)
            {
                float yaw = face * 90f;
                var rot = Quaternion.Euler(0f, yaw, 0f);
                for (int k = 0; k < 2; k++)
                {
                    var wall = Add(house, Zns + (face == 0 ? "Wall_Wood_2.prefab" : "Wall_Wood_1.prefab"), 1f, name: $"Wall_{face}_{k}");
                    if (wall == null) continue;
                    wall.transform.rotation = rot;
                    var wb = SpellboundMinionModelBuilder.RendererBounds(wall);
                    float along = (k == 0 ? -0.25f : 0.25f) * side;
                    Vector3 target = rot * new Vector3(along, 0f, side * 0.5f);
                    wall.transform.position += new Vector3(target.x - wb.center.x, -wb.min.y, target.z - wb.center.z);
                }
            }
            float wallTop = SpellboundMinionModelBuilder.RendererBounds(house).max.y;

            // 지붕: Roof_Wood 반쪽 두 장으로 박공 지붕 (높은 쪽 = 용마루가 가운데)
            for (int half = 0; half < 2; half++)
            {
                var roof = Add(house, Zns + "Roof_Wood.prefab", 1f, name: "Roof_" + half);
                if (roof == null) continue;
                var rb = SpellboundMinionModelBuilder.RendererBounds(roof);
                bool ridgeAtMaxX = RidgeAtMaxX(roof, rb);
                float targetHalfWidth = side * 0.5f + 0.35f;
                float sx = targetHalfWidth / rb.size.x;
                float sz = (side + 0.7f) / rb.size.z;
                float sy = 1.5f / rb.size.y;
                bool mirror = (half == 0) == ridgeAtMaxX; // 왼쪽 반은 용마루가 +X, 오른쪽 반은 -X
                roof.transform.localScale = Vector3.Scale(roof.transform.localScale, new Vector3(mirror ? -sx : sx, sy, sz));
                rb = SpellboundMinionModelBuilder.RendererBounds(roof);
                float cx = half == 0 ? -targetHalfWidth * 0.5f : targetHalfWidth * 0.5f;
                roof.transform.position += new Vector3(cx - rb.center.x, wallTop - 0.05f - rb.min.y, -rb.center.z);
            }

            var door = Add(house, Zns + "Store_Door.prefab", 1f, name: "Door");
            if (door != null)
            {
                var db = SpellboundMinionModelBuilder.RendererBounds(door);
                door.transform.position += new Vector3(-db.center.x, -db.min.y, side * 0.5f + 0.12f - db.center.z);
            }
            var chimney = Add(house, Zns + "Chimney.prefab", 0.55f, name: "Chimney");
            if (chimney != null)
            {
                var cb = SpellboundMinionModelBuilder.RendererBounds(chimney);
                chimney.transform.position += new Vector3(side * 0.22f - cb.center.x, wallTop + 0.3f - cb.min.y, -side * 0.2f - cb.center.z);
            }
            PlaceProp(house, "Barrel.prefab", new Vector3(-side * 0.5f - 0.35f, 0f, side * 0.4f), 0.9f);
            PlaceProp(house, "Crate.prefab", new Vector3(side * 0.5f + 0.4f, 0f, side * 0.35f), 0.8f);
            PlaceProp(house, "Barrel_2.prefab", new Vector3(side * 0.5f + 0.3f, 0f, -side * 0.3f), 0.8f);

            // 전체 높이 = 공격 타워의 80%
            var hb = SpellboundMinionModelBuilder.RendererBounds(house);
            float scale = AttackTowerHeight * 0.8f / Mathf.Max(0.01f, hb.size.y);
            house.transform.localScale *= scale;
            hb = SpellboundMinionModelBuilder.RendererBounds(house);
            house.transform.position -= new Vector3(hb.center.x, hb.min.y, hb.center.z);

            // 문 앞 소환 포털 (해골이 나오는 곳)
            var portal = Add(root, P52 + "Portal.prefab", 0.5f, new Vector3(0f, 1.1f, side * 0.5f * scale + 0.6f), new Vector3(0f, 0f, 0f), "SpawnPortal");
            if (portal != null)
            {
                SetLoop(portal, true, 1f);
                Recolor(portal, new Color(0.7f, 0.45f, 1f));
            }
            Save(root, ModelOut);
            Debug.Log($"[Skill Builder] 소환탑: 높이 {AttackTowerHeight * 0.8f:0.00}m (×{scale:0.###})");
        }

        private static void PlaceProp(GameObject parent, string prefab, Vector3 groundPos, float scale)
        {
            var go = Add(parent, Zns + prefab, scale, name: System.IO.Path.GetFileNameWithoutExtension(prefab));
            if (go == null) return;
            var b = SpellboundMinionModelBuilder.RendererBounds(go);
            go.transform.position += new Vector3(groundPos.x - b.center.x, groundPos.y - b.min.y, groundPos.z - b.center.z);
        }

        /// <summary>지붕 반쪽의 가장 높은 정점이 +X 쪽에 있는가 (용마루 방향)</summary>
        private static bool RidgeAtMaxX(GameObject roof, Bounds b)
        {
            float bestY = float.MinValue, bestX = 0f;
            foreach (var r in roof.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = r.sharedMesh;
                if (mesh == null) continue;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    if (p.y > bestY) { bestY = p.y; bestX = p.x; }
                }
            }
            return bestX > b.center.x;
        }
    }
}
