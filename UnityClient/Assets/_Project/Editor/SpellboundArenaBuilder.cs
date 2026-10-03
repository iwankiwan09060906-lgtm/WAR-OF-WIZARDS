// 아레나 임시(플레이스홀더) 배치 — Spellbound > 5. Build Placeholder Arena
//
// 구조 (모든 로직 앵커는 "빈 부모 = 바닥 피벗", 모양은 Body 자식 → 아트가 오면 Body만 교체)
//   Arena
//   ├─ Ground                       바닥 (24 × 30m)
//   ├─ Lanes/Lane_Left|Mid|Right    레인 표시 (Home 시점 좌 · 중 · 우, 두께 2cm)
//   ├─ Home (z = 0, +Z를 봄)
//   │   ├─ Home_Commander_L|C|R     발판 (x = -5 / 0 / +5)
//   │   ├─ Home_Nexus  (z = 5, 높이 1.6m)  / Body
//   │   └─ Home_Tower  (z = 8, 높이 2.4m)  / Body
//   │   (플레이어 눈높이 5m — 자기 구조물 너머로 상대 진영이 보이도록)
//   └─ Away (z = 20, -Z를 봄 — Away 시점의 L은 월드 +X)
//       ├─ Away_Commander_L|C|R     발판 (x = +5 / 0 / -5)
//       ├─ Away_Nexus  (z = 15) / Body
//       └─ Away_Tower  (z = 12) / Body
// 치수는 대략값이다 (아트 교체 전제). 여러 번 실행해도 같은 결과가 나온다.

using SpellboundVR.Arena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundArenaBuilder
    {
        private const string MaterialFolder = "Assets/_Project/Arena/Placeholder";
        private const float Spacing = 5f;
        private const float FieldLength = 20f;
        private const float NexusDepth = 5f;
        private const float TowerDepth = 8f;
        private const float PlatformHeight = 0.3f;
        /// <summary>플레이어 눈높이 (지면 기준). 자기 타워 · 넥서스 너머로 상대 진영까지 보이도록 높게 잡는다.</summary>
        private const float EyeHeight = 5f;
        private const float TowerHeight = 2.4f;
        private const float NexusHeight = 1.6f;

        [MenuItem("Spellbound/5. Build Placeholder Arena (rough layout)", priority = 5)]
        public static void Build()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != SpellboundSetupWizard.BattleScenePath)
            {
                if (scene.isDirty)
                {
                    Debug.LogError("[Arena Builder] 현재 씬을 저장한 뒤 다시 실행하세요.");
                    return;
                }
                scene = EditorSceneManager.OpenScene(SpellboundSetupWizard.BattleScenePath, OpenSceneMode.Single);
            }

            var matGround = Mat("MAT_PH_Ground", new Color(0.42f, 0.45f, 0.40f));
            var matLane = Mat("MAT_PH_Lane", new Color(0.60f, 0.62f, 0.58f));
            var matHome = Mat("MAT_PH_Home", new Color(0.25f, 0.45f, 0.85f));
            var matAway = Mat("MAT_PH_Away", new Color(0.85f, 0.30f, 0.25f));

            var arena = Root("Arena");
            arena.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            // 바닥
            var ground = FindOrRename(arena, "Ground", "Plane") ?? Prim(arena, "Ground", PrimitiveType.Plane);
            Place(ground, new Vector3(0f, 0f, FieldLength * 0.5f), Quaternion.identity, new Vector3(2.4f, 1f, 3.0f), matGround);

            // 레인 (표시 전용 얇은 판 — 유닛이 묻히지 않게)
            var lanes = Child(arena, "Lanes");
            Place(lanes, Vector3.zero, Quaternion.identity, Vector3.one, null);
            string[] laneNames = { "Lane_Left", "Lane_Mid", "Lane_Right" };
            for (int c = 0; c < 3; c++)
            {
                var lane = FindAnywhere(arena, laneNames[c]) ?? Prim(lanes, laneNames[c], PrimitiveType.Cube);
                lane.SetParent(lanes, true);
                Place(lane, new Vector3((c - 1) * Spacing, 0.01f, FieldLength * 0.5f), Quaternion.identity,
                      new Vector3(Spacing * 0.92f, 0.02f, FieldLength), matLane);
            }

            // Home (기존 Commander_Slot_1=+5(R) / 2=0(C) / 3=-5(L) 이름 정리)
            var home = Child(arena, "Home");
            Place(home, Vector3.zero, Quaternion.identity, Vector3.one, null);
            BuildCommander(arena, home, "Home_Commander_L", "Commander_Slot_3", -Spacing, 0f, matHome);
            BuildCommander(arena, home, "Home_Commander_C", "Commander_Slot_2", 0f, 0f, matHome);
            BuildCommander(arena, home, "Home_Commander_R", "Commander_Slot_1", Spacing, 0f, matHome);
            var homeNexus = BuildStructure(arena, home, "Home_Nexus", "Cube", PrimitiveType.Cube, NexusDepth, true, matHome, Quaternion.identity);
            var homeTower = BuildStructure(arena, home, "Home_Tower", "Cylinder", PrimitiveType.Cylinder, TowerDepth, false, matHome, Quaternion.identity);

            // Away (Home을 마주 봄)
            var awayRot = Quaternion.Euler(0f, 180f, 0f);
            var away = Child(arena, "Away");
            Place(away, Vector3.zero, Quaternion.identity, Vector3.one, null);
            BuildCommander(arena, away, "Away_Commander_L", null, Spacing, FieldLength, matAway);
            BuildCommander(arena, away, "Away_Commander_C", null, 0f, FieldLength, matAway);
            BuildCommander(arena, away, "Away_Commander_R", null, -Spacing, FieldLength, matAway);
            var awayNexus = BuildStructure(arena, away, "Away_Nexus", null, PrimitiveType.Cube, FieldLength - NexusDepth, true, matAway, awayRot);
            var awayTower = BuildStructure(arena, away, "Away_Tower", null, PrimitiveType.Cylinder, FieldLength - TowerDepth, false, matAway, awayRot);

            // 플레이어 리그 눈높이 (EyeLevel 트래킹 → 리그 원점 = 눈). CommanderPlatform이 시작 시 이 높이를 읽는다.
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                Undo.RecordObject(rig.transform, "Arena Builder");
                rig.transform.SetPositionAndRotation(new Vector3(0f, EyeHeight, 0f), Quaternion.identity);
            }

            // ArenaLayout 연결
            var layout = Object.FindFirstObjectByType<ArenaLayout>();
            if (layout == null) layout = new GameObject("[ArenaLayout]").AddComponent<ArenaLayout>();
            layout.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            layout.homeBaseAnchor = home.Find("Home_Commander_C");
            layout.columnSpacing = Spacing;
            layout.fieldLength = FieldLength;
            layout.nexusDepth = NexusDepth;
            layout.towerDepth = TowerDepth;
            layout.commanderPlatformHeight = PlatformHeight;
            layout.homeNexusVisual = homeNexus;
            layout.homeTowerVisual = homeTower;
            layout.awayNexusVisual = awayNexus;
            layout.awayTowerVisual = awayTower;
            EditorUtility.SetDirty(layout);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Arena Builder] 임시 아레나 배치 완료 · 저장 (Home/Away 발판 · 타워 · 넥서스, 레인, 바닥)");
        }

        // ── 구성 요소 ────────────────────────────────────────

        private static void BuildCommander(Transform arena, Transform parent, string name, string legacyName,
                                           float x, float z, Material mat)
        {
            var t = FindAnywhere(arena, name);
            if (t == null && legacyName != null)
            {
                t = FindAnywhere(arena, legacyName);
                if (t != null) t.name = name;
            }
            if (t == null) t = Prim(parent, name, PrimitiveType.Cube);
            t.SetParent(parent, true);
            Place(t, new Vector3(x, PlatformHeight * 0.5f, z), Quaternion.identity, new Vector3(2.2f, PlatformHeight, 2.2f), mat);
        }

        /// <summary>빈 부모(바닥 피벗) + Body(임시 도형). 파괴 연출이 부모 Y 스케일을 줄이므로 피벗이 바닥이어야 한다.</summary>
        private static Transform BuildStructure(Transform arena, Transform parent, string name, string legacyBodyName,
                                                PrimitiveType shape, float z, bool isNexus, Material mat, Quaternion rot)
        {
            var anchor = FindAnywhere(arena, name);
            if (anchor == null)
            {
                anchor = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(anchor.gameObject, name);
            }
            anchor.SetParent(parent, true);
            Place(anchor, new Vector3(0f, 0f, z), rot, Vector3.one, null);

            var body = anchor.Find("Body");
            if (body == null && legacyBodyName != null)
            {
                body = FindAnywhere(arena, legacyBodyName);
                if (body != null) body.name = "Body";
            }
            if (body == null) body = Prim(anchor, "Body", shape);
            body.SetParent(anchor, false);
            // Cube 높이 = scale.y, Cylinder 높이 = scale.y × 2
            if (isNexus) Place(body, anchor.position + Vector3.up * (NexusHeight * 0.5f), anchor.rotation, new Vector3(2f, NexusHeight, 2f), mat);
            else Place(body, anchor.position + Vector3.up * (TowerHeight * 0.5f), anchor.rotation, new Vector3(1.4f, TowerHeight * 0.5f, 1.4f), mat);
            return anchor;
        }

        // ── 공용 ─────────────────────────────────────────────

        private static Transform Root(string name)
        {
            var go = GameObject.Find("/" + name);
            if (go != null) return go.transform;
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            return go.transform;
        }

        private static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t != null) return t;
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform FindAnywhere(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }

        private static Transform FindOrRename(Transform root, string name, string legacyName)
        {
            var t = FindAnywhere(root, name);
            if (t != null) return t;
            t = FindAnywhere(root, legacyName);
            if (t != null) t.name = name;
            return t;
        }

        private static Transform Prim(Transform parent, string name, PrimitiveType type)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static void Place(Transform t, Vector3 worldPos, Quaternion worldRot, Vector3 localScale, Material mat)
        {
            Undo.RecordObject(t, "Arena Builder");
            t.SetPositionAndRotation(worldPos, worldRot);
            t.localScale = localScale;
            if (mat != null)
            {
                var r = t.GetComponent<Renderer>();
                if (r != null)
                {
                    Undo.RecordObject(r, "Arena Builder");
                    r.sharedMaterial = mat;
                }
            }
        }

        private static Material Mat(string name, Color color)
        {
            SpellboundSetupWizard.EnsureFolder(MaterialFolder);
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m != null) return m;
            m = new Material(Shader.Find("Standard")) { color = color, name = name };
            AssetDatabase.CreateAsset(m, path);
            return m;
        }
    }
}
