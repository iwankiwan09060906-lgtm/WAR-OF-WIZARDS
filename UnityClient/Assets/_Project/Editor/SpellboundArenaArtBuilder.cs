// 아레나 아트 적용 — Spellbound > 6. Apply Arena Art
//   ① 타워 · 넥서스: Turret / castle 모델 (원본 50만 삼각형 → Mesh LOD로 줄인 메시를 Art/Models/Arena에 저장해 씀)
//      재질은 보는 사람 기준 (TeamArt): 타워 아군 = roughness · 적 = basecolor, 넥서스 본인 = basecolor · 적 = roughness
//      넥서스는 플레이어 발판 바로 앞(깊이 3m) — ArenaLayout.nexusDepth도 갱신 (게임 로직 위치가 바뀜)
//   ② Commander 발판 (L · C · R): Scene Teleportation Kit platform_blue(본인) / platform_red(적), 폭 3.6m
//      발판 윗면 높이로 ArenaLayout.commanderPlatformHeight 갱신. 플레이어 눈높이(OVRCameraRig)는 10m
//   ③ 바닥: Hand Painted Stone Texture의 Stone_normal 재질 하나 (색 = Stone_Ambient_Occlusion) — 레인 3개 + 발판 공간 전체가 절벽 덩어리의 윗면 한 장
//      (타일 모델 없이 삼각형 약 100개). 예전 400m 바닥판은 지우고 레인 판은 렌더러만 끈다
//      전장 길이 41m (양 타워 사이 15m, 넥서스 ↔ 타워 10m). 타워 · Away 발판 · 넥서스도 같이 옮기고 ArenaLayout.fieldLength · towerDepth 갱신
//   ④ 낮은 벽: bricks_01(곧은 벽) · brick_01_Variant(모서리)를 줄여 바닥 둘레를 감싼다
//   ⑤ 낭떠러지: 바닥 아래는 절차 생성 바위 덩어리(AEGIS 바위 재질), 사방은 깊은 심연, 멀리 양옆 · 앞뒤로 AEGIS 바위를 크게 키워 세운 절벽
//      + 벽 안쪽 구석에 작은 AEGIS 바위 장식. 게임 로직은 바닥에 레이캐스트하지 않으므로(조준 = ArenaGeometry 수식) 판정에는 영향 없음
//   ⑥ 타워 총알 VFX: Hovl Magic fire pro blue(아군 타워) / red(적 타워)를 작게 줄인 VFX_Tower_*_Projectile 프리팹 + VFX 카탈로그 갱신
//   ⑦ 하늘 · 조명 · 안개: 18시 무렵 해질녘. 지평선 아래 하늘 = 안개색 → 심연 바닥이 안개 속으로 사라진다
// 여러 번 실행해도 같은 결과 (Art_ 접두 오브젝트를 지우고 다시 만든다). 씬은 저장하지 않는다 — 확인 후 Ctrl+S.
// 플레이스홀더로 되돌리려면 Art_* 오브젝트를 지우고 Spellbound > 5. Build Placeholder Arena.

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundArenaArtBuilder
    {
        private const string DungeonPrefabs = "Assets/Stylized Dungeon - Free Pack/Prefabs/";
        private const string StoneMaterial = "Assets/Hand Painted Stone Texture/Material/Stone_normal.mat";
        private const string StoneColorTexture = "Assets/Hand Painted Stone Texture/Texture/Stone_Ambient_Occlusion.png";
        private const string TeleportPlatforms = "Assets/Scene Teleportation Kit/Prefabs/platforms/";
        private const string RockPrefabs = "Assets/AEGIS - FREE Stylised Rock Pack/Prefabs/";
        private const string RockMaterial = "Assets/AEGIS - FREE Stylised Rock Pack/Materials/Rock mat.mat";
        private const string TurretFbx = "Assets/stone+siege+turret+3d+model/Turret.fbx";
        private const string TurretMaterials = "Assets/stone+siege+turret+3d+model/style/Materials/stone_siege_turret_3d_model_";
        private const string CastleFbx = "Assets/crystal+castle+3d+model/castle.fbx";
        private const string CastleMaterials = "Assets/crystal+castle+3d+model/style/Materials/crystal_castle_3d_model_";
        private const string FirePrefabs = "Assets/Hovl Studio/Procedural fire/Prefabs/Magic fire pro ";
        private const string MaterialFolder = "Assets/Art/Materials/Arena";
        private const string ModelFolder = "Assets/Art/Models/Arena";
        private const string VfxFolder = "Assets/Prefabs/VFX";
        private const string ArtPrefix = "Art_";

        // 구조물 키 (Away도 같은 크기) — 이전 아트와 같은 크기: 넥서스 2.4m · 타워 4.8m
        private const float NexusHeight = 2.4f;
        private const float TowerHeight = 4.8f;
        /// <summary>넥서스 깊이 (자기 Commander 발판 기준, m) — 발판(폭 3.6m) 바로 앞, 겹치지 않게</summary>
        private const float NexusDepth = 3f;
        /// <summary>타워 깊이 (m). 넥서스(3m)와 타워 사이 10m</summary>
        private const float TowerDepth = 13f;
        /// <summary>전장 길이 (m). 양 타워 사이 = 41 - 13 × 2 = 15m</summary>
        private const float FieldLength = 41f;
        /// <summary>원본 50만 삼각형 → Mesh LOD 단계 (DiscardOddLevels 기준 1단계 ≈ 1/4): 3 ≈ 1만 · 4 ≈ 2,700 · 5 ≈ 700 삼각형</summary>
        private static readonly int[] StructureLodLevels = { 3, 4, 5 };
        /// <summary>LODGroup 전환 화면 높이 비율 — 자기 진영 구조물은 1만, 상대 진영(28m 이상)은 2,700 이하로 그린다</summary>
        private static readonly float[] StructureLodHeights = { 0.12f, 0.04f, 0.003f };

        // 플레이어 크기 2배: 눈높이 5m → 10m, 발판 폭 1.8m → 3.6m (발판 간격 5m라 서로 떨어져 있다)
        private const float EyeHeight = 10f;
        private const float PlatformWidth = 3.6f;

        // 바닥: 돌 텍스처 한 장 (FloorUvMeters마다 반복). 윗면 = 지면(0)
        private const float FloorTop = 0f;
        /// <summary>돌 텍스처 한 장이 덮는 길이 (m)</summary>
        private const float FloorUvMeters = 4f;
        /// <summary>레인 바깥 끝 → 바닥 끝 (m). 낮은 벽이 이 안에 선다</summary>
        private const float FloorShoulder = 2f;
        /// <summary>양쪽 Commander 발판 중심 → 바닥 끝 (m). 발판 반폭 1.8m + 여유</summary>
        private const float FloorBackMargin = 5f;

        // 낮은 벽: bricks_01 (길이 13m · 높이 3.2m · 두께 1.8m) → 0.4배 = 길이 5.3m · 높이 1.3m
        private const float WallScale = 0.4f;
        private const float CornerScale = 0.4f;

        // 낭떠러지: 바닥 아래 바위 덩어리 단면 (위 → 아래): 깊이(m) · 안쪽으로 들어간 정도(m, 음수 = 바깥으로 나온 턱) · 들쭉날쭉 폭(m) · 높이 흔들림(m)
        private static readonly (float depth, float inset, float noise, float jitterY)[] CliffRings =
        {
            (0f, 0f, 0.05f, 0f),
            (-1.2f, -0.3f, 0.35f, 0.2f),
            (-3f, 0.4f, 0.6f, 0.5f),
            (-7f, 1.2f, 0.8f, 0.8f),
            (-14f, 2.4f, 1f, 1.2f),
            (-24f, 3.8f, 1.2f, 1.5f),
            (-38f, 5.2f, 1.2f, 2f),
            (-55f, 6.4f, 1f, 2f),
        };
        private const float CliffTipDepth = 20f;
        /// <summary>바닥(= 바위 덩어리 윗면) 모서리 둥글기 — 모서리 벽이 걸치지 않게 작게</summary>
        private const float CliffCornerRadius = 0.6f;
        private const float CliffEdgeStep = 1.5f;
        /// <summary>바위 텍스처 한 장 = 4m</summary>
        private const float CliffUvMeters = 4f;

        // 협곡 벽 (AEGIS 바위를 크게 키워 이어 세움): 다리 양옆으로 두 줄 — 가까운 줄은 다리보다 낮게(내려다보는 절벽),
        // 먼 줄은 더 높게(지평선 쪽 벽). 앞뒤(상대 진영 너머)는 트여 있어 심연이 안개 속으로 이어진다
        private static readonly string[] CliffRockNames = { "Rock7", "Rock8", "Rock9", "Rock12", "Rock1", "Rock2", "Rock3", "Rock11", "Rock5", "Rock15" };
        /// <summary>(다리 가장자리 → 바위 중심 최소 · 최대 거리, 바위 폭 배율 최소 · 최대, 윗면 높이 최소 · 최대, 줄 간격)</summary>
        private static readonly (float gapMin, float gapMax, float scaleMin, float scaleMax, float topMin, float topMax, float step)[] CliffRows =
        {
            (30f, 40f, 20f, 28f, -12f, 0f, 9f),
            (58f, 72f, 32f, 44f, 2f, 16f, 14f),
        };
        /// <summary>협곡 벽이 다리 앞뒤로 더 뻗는 길이 (m)</summary>
        private const float CliffRowOverhang = 45f;
        private static readonly string[] DecorRockNames = { "Rock16", "Rock17", "Rock18", "Rock20", "Rock22", "Rock24", "Small Rock1", "Small Rock4", "Small Rock5", "Small Rock7" };

        // 타워 총알: Hovl 불꽃(지름 약 2m) → 0.8배 (약 1.6m — 눈높이 10m에서 15~25m 앞 교전도 보이게).
        // 검은 연기(Smoke · FireDark)는 끈다 — 날아가는 불꽃을 어둡게 가린다
        private const float TowerShotScale = 0.8f;
        private static readonly string[] TowerShotHiddenParts = { "Smoke", "FireDark" };

        [MenuItem("Spellbound/6. Apply Arena Art (structures · platforms · floor · walls · cliffs · sky)", priority = 6)]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != SpellboundSetupWizard.BattleScenePath)
            {
                Debug.LogError("[Arena Art] 04_Battle 씬을 연 상태에서 실행하세요.");
                return;
            }
            var arenaGo = GameObject.Find("/Arena");
            if (arenaGo == null)
            {
                Debug.LogError("[Arena Art] Arena가 없습니다. 먼저 Spellbound > 5. Build Placeholder Arena를 실행하세요.");
                return;
            }
            var arena = arenaGo.transform;
            SpellboundSetupWizard.EnsureFolder(MaterialFolder);
            SpellboundSetupWizard.EnsureFolder(ModelFolder);
            SpellboundSetupWizard.EnsureFolder(VfxFolder);

            var layout = Object.FindFirstObjectByType<ArenaLayout>();
            if (layout != null)
            {
                Undo.RecordObject(layout, "Arena Art");
                layout.fieldLength = FieldLength;
                layout.towerDepth = TowerDepth;
                EditorUtility.SetDirty(layout);
            }
            MoveAwayAnchors(arena, layout);
            MoveNexusAnchors(arena, layout);
            PlaceRig(layout);

            var tower = ReducedStructureMeshes(TurretFbx, "MESH_Structure_Tower");
            var nexus = ReducedStructureMeshes(CastleFbx, "MESH_Structure_Nexus");
            foreach (var team in new[] { Team.Home, Team.Away })
            {
                string side = team.ToString();
                BuildStructure(arena, side + "_Tower", team, tower, TowerHeight,
                               Load<Material>(TurretMaterials + "roughness.mat"), Load<Material>(TurretMaterials + "basecolor.mat"));
                BuildStructure(arena, side + "_Nexus", team, nexus, NexusHeight,
                               Load<Material>(CastleMaterials + "basecolor.mat"), Load<Material>(CastleMaterials + "roughness.mat"));
            }

            float deckTop = 0f;
            foreach (var team in new[] { Team.Home, Team.Away })
                foreach (var slot in new[] { "L", "C", "R" })
                    deckTop = Mathf.Max(deckTop, BuildPlatform(arena, team + "_Commander_" + slot, team));
            if (layout != null)
            {
                Undo.RecordObject(layout, "Arena Art");
                layout.commanderPlatformHeight = deckTop;
                layout.nexusDepth = NexusDepth;
                EditorUtility.SetDirty(layout);
            }

            float spacing = layout != null ? layout.columnSpacing : 5f;
            HideOldGround(arena);
            var root = RecreateRoot(arena, ArtPrefix + "Bridge");
            var floor = FloorArea(spacing);
            BuildWalls(root, floor);
            BuildCliffs(root, floor);
            BuildDecor(root, floor, spacing);
            BuildTowerShotVfx();
            ApplyDuskEnvironment();
            TeamArt.ApplyAll(Team.Home);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Arena Art] 적용 완료 — 확인 후 씬을 저장하세요 (Ctrl+S)");
        }

        // ── ① 구조물 ─────────────────────────────────────────

        /// <summary>넥서스 앵커(빈 부모)를 발판 바로 앞으로. ArenaLayout이 Home 넥서스 위치에서 깊이를 읽으므로 로직도 같이 바뀐다</summary>
        private static void MoveNexusAnchors(Transform arena, ArenaLayout layout)
        {
            Transform baseAnchor = layout != null ? layout.homeBaseAnchor : null;
            Vector3 basePos = baseAnchor != null ? baseAnchor.position : Vector3.zero;
            Vector3 forward = baseAnchor != null ? baseAnchor.forward : Vector3.forward;
            basePos.y = 0f;
            float fieldLength = layout != null ? layout.fieldLength : 20f;
            MoveAnchor(FindAnywhere(arena, "Home_Nexus"), basePos + forward * NexusDepth);
            MoveAnchor(FindAnywhere(arena, "Away_Nexus"), basePos + forward * (fieldLength - NexusDepth));
        }

        /// <summary>타워 · Away 발판을 전장 길이 · 타워 깊이에 맞춰 옮긴다 (x · 높이는 그대로).
        /// ArenaLayout이 Home 타워 위치에서 깊이를 읽으므로 게임 로직 위치도 같이 바뀐다</summary>
        private static void MoveAwayAnchors(Transform arena, ArenaLayout layout)
        {
            var homeTower = FindAnywhere(arena, "Home_Tower");
            if (homeTower != null) MoveAnchor(homeTower, new Vector3(homeTower.position.x, homeTower.position.y, TowerDepth));
            foreach (var slot in new[] { "L", "C", "R" })
            {
                var t = FindAnywhere(arena, "Away_Commander_" + slot);
                if (t != null) MoveAnchor(t, new Vector3(t.position.x, t.position.y, FieldLength));
            }
            var tower = FindAnywhere(arena, "Away_Tower");
            if (tower != null) MoveAnchor(tower, new Vector3(tower.position.x, tower.position.y, FieldLength - TowerDepth));
        }

        /// <summary>플레이어 리그를 Home 중앙 발판 위 눈높이로 (CommanderPlatform이 시작 시 이 높이를 읽는다)</summary>
        private static void PlaceRig(ArenaLayout layout)
        {
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) return;
            Transform baseAnchor = layout != null ? layout.homeBaseAnchor : null;
            Vector3 basePos = baseAnchor != null ? baseAnchor.position : Vector3.zero;
            MoveAnchor(rig.transform, new Vector3(basePos.x, EyeHeight, basePos.z));
        }

        private static void MoveAnchor(Transform anchor, Vector3 position)
        {
            if (anchor == null) return;
            Undo.RecordObject(anchor, "Arena Art");
            anchor.position = position;
        }

        /// <summary>구조물 앵커 아래에 같은 메시 · 다른 재질로 Art_Ally / Art_Enemy를 만들고 TeamArt로 묶는다 (피벗 = 바닥 중앙)</summary>
        private static void BuildStructure(Transform arena, string anchorName, Team team, Mesh[] lods, float height, Material ally, Material enemy)
        {
            var anchor = FindAnywhere(arena, anchorName);
            if (anchor == null || lods == null || lods.Length == 0)
            {
                Debug.LogError($"[Arena Art] {anchorName} 또는 메시 없음");
                return;
            }
            var body = anchor.Find("Body");
            if (body != null) Undo.DestroyObjectImmediate(body.gameObject);
            ClearArt(anchor);

            float scale = height / lods[0].bounds.size.y;
            var allyGo = StructureModel(anchor, "Ally", lods, ally, scale);
            var enemyGo = StructureModel(anchor, "Enemy", lods, enemy, scale);
            SetTeamArt(anchor, team, allyGo, enemyGo);
        }

        /// <summary>LODGroup: 가까이 = 약 1만 · 중간 = 약 2,700 · 멀리 = 약 700 삼각형 (그림자도 같은 단계로 그린다)</summary>
        private static GameObject StructureModel(Transform anchor, string role, Mesh[] lods, Material material, float scale)
        {
            var go = new GameObject(ArtPrefix + role);
            Undo.RegisterCreatedObjectUndo(go, "Arena Art");
            go.transform.SetParent(anchor, false);
            // 앵커 스케일과 상관없이 월드 크기를 맞춘다
            var ls = anchor.lossyScale;
            go.transform.localScale = new Vector3(scale / ls.x, scale / ls.y, scale / ls.z);
            SetStatic(go);

            var levels = new LOD[lods.Length];
            for (int i = 0; i < lods.Length; i++)
            {
                var child = new GameObject("LOD" + i, typeof(MeshFilter), typeof(MeshRenderer));
                child.transform.SetParent(go.transform, false);
                child.GetComponent<MeshFilter>().sharedMesh = lods[i];
                var r = child.GetComponent<MeshRenderer>();
                r.sharedMaterial = material;
                SetStatic(child);
                levels[i] = new LOD(i < StructureLodHeights.Length ? StructureLodHeights[i] : 0.002f, new Renderer[] { r });
            }
            var group = go.AddComponent<LODGroup>();
            group.SetLODs(levels);
            group.RecalculateBounds();
            return go;
        }

        private static void SetTeamArt(Transform anchor, Team team, GameObject ally, GameObject enemy)
        {
            var art = anchor.GetComponent<TeamArt>();
            if (art == null) art = Undo.AddComponent<TeamArt>(anchor.gameObject);
            Undo.RecordObject(art, "Arena Art");
            art.team = team;
            art.allyVisual = ally;
            art.enemyVisual = enemy;
            EditorUtility.SetDirty(art);
        }

        /// <summary>원본(약 50만 삼각형, Z가 위)을 Mesh LOD로 줄이고 StructureLodLevels 단계만 떼어 각각 새 메시로 저장한다.
        /// Y가 위 · 피벗 = 바닥 중앙으로 굽고, 메시 압축 · 정점 순서 최적화. 원본 FBX는 씬에서 참조하지 않으므로 빌드에 들어가지 않는다.
        /// 같은 이름 · 단계가 모두 있으면 다시 만들지 않는다 (생성에 약 40초)</summary>
        private static Mesh[] ReducedStructureMeshes(string fbxPath, string name)
        {
            var paths = StructureLodLevels.Select(l => $"{ModelFolder}/{name}_LOD{l}.asset").ToArray();
            var cached = paths.Select(AssetDatabase.LoadAssetAtPath<Mesh>).ToArray();
            if (cached.All(m => m != null)) return cached;

            var src = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Mesh>().FirstOrDefault();
            if (src == null)
            {
                Debug.LogError("[Arena Art] 메시 없음: " + fbxPath);
                return null;
            }
            EditorUtility.DisplayProgressBar("Arena Art", $"{name}: Mesh LOD 생성 중 (약 40초)", 0.5f);
            try
            {
                var work = Object.Instantiate(src);
                MeshLodUtility.GenerateMeshLods(work, MeshLodUtility.LodGenerationFlags.DiscardOddLevels, StructureLodLevels.Max() + 1);
                var srcVerts = work.vertices;
                var srcNormals = work.normals;
                var srcUv = work.uv;
                // 모든 단계가 같은 피벗을 쓰도록 원본 전체 bounds 기준으로 바닥 중앙을 맞춘다 (Z-up → Y-up)
                var rot = Quaternion.Euler(-90f, 0f, 0f);
                var rotated = srcVerts.Select(v => rot * v).ToArray();
                var full = GeometryUtility.CalculateBounds(rotated, Matrix4x4.identity);
                var offset = new Vector3(-full.center.x, -full.min.y, -full.center.z);

                var result = new Mesh[StructureLodLevels.Length];
                for (int li = 0; li < StructureLodLevels.Length; li++)
                {
                    int level = Mathf.Min(StructureLodLevels[li], work.lodCount - 1);
                    int[] indices = work.GetIndices(0, level, true);
                    // 쓰는 정점만 남긴다
                    var remap = new Dictionary<int, int>(indices.Length);
                    var verts = new List<Vector3>();
                    var normals = new List<Vector3>();
                    var uvs = new List<Vector2>();
                    for (int i = 0; i < indices.Length; i++)
                    {
                        int v = indices[i];
                        if (!remap.TryGetValue(v, out int nv))
                        {
                            nv = verts.Count;
                            remap.Add(v, nv);
                            verts.Add(rotated[v] + offset);
                            normals.Add(srcNormals.Length > v ? rot * srcNormals[v] : Vector3.up);
                            uvs.Add(srcUv.Length > v ? srcUv[v] : Vector2.zero);
                        }
                        indices[i] = nv;
                    }
                    var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(paths[li]);
                    if (mesh == null)
                    {
                        mesh = new Mesh { name = $"{name}_LOD{StructureLodLevels[li]}" };
                        AssetDatabase.CreateAsset(mesh, paths[li]);
                    }
                    mesh.Clear();
                    mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    mesh.SetVertices(verts);
                    mesh.SetNormals(normals);
                    mesh.SetUVs(0, uvs);
                    mesh.SetTriangles(indices, 0);
                    mesh.RecalculateBounds();
                    MeshUtility.Optimize(mesh);
                    MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
                    EditorUtility.SetDirty(mesh);
                    result[li] = mesh;
                    Debug.Log($"[Arena Art] {mesh.name}: {src.triangles.Length / 3} → {indices.Length / 3} 삼각형");
                }
                Object.DestroyImmediate(work);
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        // ── ② Commander 발판 ─────────────────────────────────

        /// <summary>platform_blue(본인) / platform_red(적)를 폭 3.6m로 놓고 발판 디스크 윗면 높이(m)를 돌려준다</summary>
        private static float BuildPlatform(Transform arena, string commanderName, Team team)
        {
            var commander = FindAnywhere(arena, commanderName);
            if (commander == null)
            {
                Debug.LogError($"[Arena Art] {commanderName} 없음");
                return 0f;
            }
            HideRenderer(commander);
            ClearArt(commander);

            var ally = PlatformModel(commander, "Ally", TeleportPlatforms + "platform_blue.prefab", out float topAlly);
            var enemy = PlatformModel(commander, "Enemy", TeleportPlatforms + "platform_red.prefab", out float topEnemy);
            SetTeamArt(commander, team, ally, enemy);
            return Mathf.Max(topAlly, topEnemy);
        }

        private static GameObject PlatformModel(Transform commander, string role, string prefabPath, out float deckTop)
        {
            deckTop = 0f;
            var asset = Load<GameObject>(prefabPath);
            if (asset == null) return null;
            var go = Spawn(asset, commander, role);
            // 발판 오브젝트는 (2.2, 0.3, 2.2)로 늘려져 있으므로 자식 스케일로 상쇄
            var t = go.transform;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            var b = SpellboundMinionModelBuilder.RendererBounds(go);
            var ps = commander.lossyScale;
            float s = PlatformWidth * ps.x / b.size.x;
            t.localScale = new Vector3(s / ps.x, s / ps.y, s / ps.z);
            b = SpellboundMinionModelBuilder.RendererBounds(go);
            t.position -= new Vector3(b.center.x - commander.position.x, b.min.y, b.center.z - commander.position.z);
            StripColliders(go);
            SetStatic(go);

            // 서는 면 = 가운데 디스크 윗면 (둘레 장식 바위 · 기둥은 더 높다)
            var disc = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.name == "disc_lo");
            deckTop = disc != null ? disc.bounds.max.y : SpellboundMinionModelBuilder.RendererBounds(go).max.y;
            return go;
        }

        // ── ③ 바닥 ───────────────────────────────────────────

        /// <summary>바닥이 실제로 덮은 사각형 (xz, 월드) — 벽 · 절벽 · 장식이 이 범위를 기준으로 선다</summary>
        private struct FloorRect
        {
            public float MinX, MaxX, MinZ, MaxZ;
        }

        /// <summary>예전 바닥판(Ground — 게임 로직은 쓰지 않음)은 지우고, 레인 판은 렌더러만 끈다 (레인 앵커는 남김)</summary>
        private static void HideOldGround(Transform arena)
        {
            var ground = FindAnywhere(arena, "Ground");
            if (ground != null) Undo.DestroyObjectImmediate(ground.gameObject);
            var lanes = arena.Find("Lanes");
            if (lanes == null) return;
            ClearArt(lanes);
            foreach (var laneName in new[] { "Lane_Left", "Lane_Mid", "Lane_Right" })
            {
                var lane = FindAnywhere(arena, laneName);
                if (lane != null) HideRenderer(lane);
            }
        }

        private static Transform RecreateRoot(Transform arena, string name)
        {
            foreach (var old in new[] { name, ArtPrefix + "Cliffs" })
            {
                var t = arena.Find(old);
                if (t != null) Undo.DestroyObjectImmediate(t.gameObject);
            }
            var root = new GameObject(name).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Arena Art");
            root.SetParent(arena, true); // 월드 원점 유지 — 자식 좌표 = 월드 좌표
            SetStatic(root.gameObject);
            return root;
        }

        /// <summary>바닥 범위: 레인 3개 + 양옆 어깨(낮은 벽 자리) + 양쪽 발판 뒤 여유</summary>
        private static FloorRect FloorArea(float spacing)
        {
            float halfWidth = 1.5f * spacing + FloorShoulder;
            return new FloorRect { MinX = -halfWidth, MaxX = halfWidth, MinZ = -FloorBackMargin, MaxZ = FieldLength + FloorBackMargin };
        }

        /// <summary>Stone_normal 재질을 복사해 색(_MainTex)만 Stone_Ambient_Occlusion으로 바꾼 바닥 재질</summary>
        private static Material FloorMaterial()
        {
            var src = Load<Material>(StoneMaterial);
            var color = Load<Texture2D>(StoneColorTexture);
            if (src == null) return null;
            string path = MaterialFolder + "/MAT_Arena_Floor.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(src);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(src);
            if (color != null) m.mainTexture = color;
            m.mainTextureScale = Vector2.one; // 반복은 메시 UV(FloorUvMeters)로
            EditorUtility.SetDirty(m);
            return m;
        }

        // ── ④ 낮은 벽 ────────────────────────────────────────

        /// <summary>바닥 둘레: 네 변은 bricks_01을 이어 세우고, 네 모서리는 brick_01_Variant</summary>
        private static void BuildWalls(Transform root, FloorRect f)
        {
            var straight = Load<GameObject>(DungeonPrefabs + "bricks_01.prefab");
            var corner = Load<GameObject>(DungeonPrefabs + "brick_01_Variant.prefab");
            if (straight == null) return;
            var parent = new GameObject(ArtPrefix + "Walls").transform;
            parent.SetParent(root, false);
            SetStatic(parent.gameObject);

            // 한 토막 크기 (길이 = 로컬 x)
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(straight);
            probe.transform.localScale = Vector3.one * WallScale;
            var sb = SpellboundMinionModelBuilder.RendererBounds(probe);
            Object.DestroyImmediate(probe);
            float segment = sb.size.x;
            float half = sb.size.z * 0.5f;
            float cornerSize = 0f;
            if (corner != null)
            {
                probe = (GameObject)PrefabUtility.InstantiatePrefab(corner);
                probe.transform.localScale = Vector3.one * CornerScale;
                var cb = SpellboundMinionModelBuilder.RendererBounds(probe);
                Object.DestroyImmediate(probe);
                cornerSize = Mathf.Max(cb.size.x, cb.size.z);
            }

            // 벽 중심선 = 바닥 끝에서 반 두께 안쪽
            float x0 = f.MinX + half, x1 = f.MaxX - half, z0 = f.MinZ + half, z1 = f.MaxZ - half;
            float inset = cornerSize * 0.5f;
            WallRun(parent, straight, new Vector3(x0, 0f, z0 + inset), new Vector3(x0, 0f, z1 - inset), segment); // 왼쪽 (Home 시점)
            WallRun(parent, straight, new Vector3(x1, 0f, z0 + inset), new Vector3(x1, 0f, z1 - inset), segment); // 오른쪽
            WallRun(parent, straight, new Vector3(x0 + inset, 0f, z0), new Vector3(x1 - inset, 0f, z0), segment); // Home 뒤
            WallRun(parent, straight, new Vector3(x0 + inset, 0f, z1), new Vector3(x1 - inset, 0f, z1), segment); // Away 뒤

            if (corner == null) return;
            var corners = new[] { new Vector3(x0, 0f, z0), new Vector3(x1, 0f, z0), new Vector3(x1, 0f, z1), new Vector3(x0, 0f, z1) };
            for (int i = 0; i < 4; i++)
            {
                var go = Spawn(corner, parent, "Corner_" + i);
                go.transform.rotation = Quaternion.Euler(0f, 90f * i, 0f);
                go.transform.localScale = Vector3.one * CornerScale;
                PlaceOnFloor(go, corners[i]);
                StripColliders(go);
                SetStatic(go);
            }
        }

        /// <summary>a → b 직선을 토막 수로 나눠 빈틈없이 (토막이 살짝 겹치게) 세운다</summary>
        private static void WallRun(Transform parent, GameObject prefab, Vector3 a, Vector3 b, float segment)
        {
            float length = Vector3.Distance(a, b);
            if (length < 0.5f) return;
            int count = Mathf.Max(1, Mathf.CeilToInt(length / (segment * 0.95f)));
            Vector3 dir = (b - a).normalized;
            float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg - 90f; // 로컬 x(길이)를 진행 방향으로
            for (int i = 0; i < count; i++)
            {
                var go = Spawn(prefab, parent, "Wall");
                go.transform.rotation = Quaternion.Euler(0f, yaw + (i % 2 == 1 ? 180f : 0f), 0f);
                go.transform.localScale = Vector3.one * WallScale;
                PlaceOnFloor(go, Vector3.Lerp(a, b, (i + 0.5f) / count));
                StripColliders(go);
                SetStatic(go);
            }
        }

        /// <summary>렌더러 bounds 중심을 xz에, 바닥을 바닥 타일 윗면에 맞춘다</summary>
        private static void PlaceOnFloor(GameObject go, Vector3 xz)
        {
            var b = SpellboundMinionModelBuilder.RendererBounds(go);
            go.transform.position += new Vector3(xz.x - b.center.x, FloorTop - 0.05f - b.min.y, xz.z - b.center.z);
        }

        // ── ⑤ 낭떠러지 ───────────────────────────────────────

        private static void BuildCliffs(Transform root, FloorRect f)
        {
            var rockMat = Load<Material>(RockMaterial);

            // 전장 덩어리: 윗면 = 돌 바닥(서브메시 1), 옆면 = 아래로 갈수록 좁아지다 뾰족하게 끝나는 절벽(서브메시 0)
            var rng = new System.Random(41);
            var body = new RockMesh(CliffUvMeters, FloorUvMeters);
            var outline = RoundedRectOutline(f.MinX, f.MaxX, f.MinZ, f.MaxZ, CliffCornerRadius, CliffEdgeStep);
            AddRockColumn(body, outline, FloorTop, (f.MaxX - f.MinX) * 0.5f / 9f, rng);
            var go = new GameObject(ArtPrefix + "Ground", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(root, false);
            go.GetComponent<MeshFilter>().sharedMesh = body.Save("MESH_Arena_CliffBody");
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = new[] { rockMat, FloorMaterial() };
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
            SetStatic(go);

            // 멀리 둘러싼 절벽: AEGIS 바위를 크게 (세로로 더 늘려 심연 아래까지)
            var parent = new GameObject(ArtPrefix + "FarCliffs").transform;
            parent.SetParent(root, false);
            SetStatic(parent.gameObject);
            var rocks = CliffRockNames.Select(n => Load<GameObject>(RockPrefabs + n + ".prefab")).Where(p => p != null).ToArray();
            if (rocks.Length == 0) return;
            float cx = (f.MinX + f.MaxX) * 0.5f;
            float hx = (f.MaxX - f.MinX) * 0.5f;
            foreach (var row in CliffRows)
                for (int side = -1; side <= 1; side += 2)
                    for (float z = f.MinZ - CliffRowOverhang; z <= f.MaxZ + CliffRowOverhang; z += row.step)
                        FarCliffRock(parent, rocks, rng, new Vector3(cx + side * (hx + Lerp(rng, row.gapMin, row.gapMax)), 0f, z + Lerp(rng, -2f, 2f)),
                                     Lerp(rng, row.scaleMin, row.scaleMax), Lerp(rng, row.topMin, row.topMax));
        }

        /// <summary>바위 하나를 크게 · 세로로 더 길게 세운다 (아래쪽은 안개 속 심연으로 사라진다)</summary>
        private static void FarCliffRock(Transform parent, GameObject[] rocks, System.Random rng, Vector3 at, float scale, float top)
        {
            var go = Spawn(rocks[rng.Next(rocks.Length)], parent, "Cliff");
            var t = go.transform;
            t.rotation = Quaternion.Euler(0f, Lerp(rng, 0f, 360f), 0f);
            t.localScale = new Vector3(scale, scale * Lerp(rng, 1.8f, 2.4f), scale);
            var b = SpellboundMinionModelBuilder.RendererBounds(go);
            t.position += new Vector3(at.x - b.center.x, top - b.max.y, at.z - b.center.z);
            StripColliders(go);
            foreach (var rr in go.GetComponentsInChildren<Renderer>()) rr.shadowCastingMode = ShadowCastingMode.Off;
            SetStatic(go);
        }

        /// <summary>벽 안쪽 어깨(레인 밖)와 발판 뒤 구석에 작은 바위 무더기</summary>
        private static void BuildDecor(Transform root, FloorRect f, float spacing)
        {
            var rocks = DecorRockNames.Select(n => Load<GameObject>(RockPrefabs + n + ".prefab")).Where(p => p != null).ToArray();
            if (rocks.Length == 0) return;
            var parent = new GameObject(ArtPrefix + "Decor").transform;
            parent.SetParent(root, false);
            SetStatic(parent.gameObject);
            var rng = new System.Random(77);
            float laneEdge = 1.5f * spacing;
            float shoulder = Mathf.Max(0.3f, (f.MaxX - laneEdge) - 1f); // 레인 끝 → 벽 안쪽
            // 어깨를 따라 듬성듬성 (레인 위에는 두지 않는다)
            for (float z = f.MinZ + 2f; z < f.MaxZ - 2f; z += Lerp(rng, 4f, 8f))
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    if (rng.NextDouble() < 0.4) continue;
                    float x = side * (laneEdge + 0.2f + (float)rng.NextDouble() * shoulder * 0.6f);
                    DecorRock(parent, rocks, rng, new Vector3(x, 0f, z + Lerp(rng, -1f, 1f)));
                }
            }
        }

        private static void DecorRock(Transform parent, GameObject[] rocks, System.Random rng, Vector3 at)
        {
            var go = Spawn(rocks[rng.Next(rocks.Length)], parent, "Rock");
            var t = go.transform;
            t.rotation = Quaternion.Euler(0f, Lerp(rng, 0f, 360f), 0f);
            t.localScale = Vector3.one * Lerp(rng, 1.2f, 2.2f);
            PlaceOnFloor(go, at);
            StripColliders(go);
            SetStatic(go);
        }

        private static float Lerp(System.Random rng, float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());

        /// <summary>둥근 모서리 직사각형 외곽선 (xz) + 바깥 방향</summary>
        private static List<(Vector2 p, Vector2 n)> RoundedRectOutline(float minX, float maxX, float minZ, float maxZ, float radius, float step)
        {
            var pts = new List<(Vector2, Vector2)>();
            var centers = new[]
            {
                new Vector2(maxX - radius, minZ + radius), new Vector2(maxX - radius, maxZ - radius),
                new Vector2(minX + radius, maxZ - radius), new Vector2(minX + radius, minZ + radius),
            };
            const int arcSegments = 4;
            for (int c = 0; c < 4; c++)
            {
                float startDeg = -90f + 90f * c;
                for (int i = 0; i <= arcSegments; i++)
                {
                    var n = Dir(startDeg + 90f * i / arcSegments);
                    pts.Add((centers[c] + n * radius, n));
                }
                // 다음 모서리까지 곧은 변
                var edgeN = Dir(startDeg + 90f);
                Vector2 from = centers[c] + edgeN * radius;
                Vector2 to = centers[(c + 1) % 4] + edgeN * radius;
                int segments = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(from, to) / step));
                for (int i = 1; i < segments; i++) pts.Add((Vector2.Lerp(from, to, (float)i / segments), edgeN));
            }
            return pts;
        }

        /// <summary>바위 덩어리: 윗면 외곽선에서 CliffRings 단면을 따라 내려가며 좁아지고, 맨 아래는 뾰족한 끝.
        /// 윗면은 외곽선 무게중심에서 부채꼴 (외곽선이 중심에서 다 보이는 모양이어야 한다)</summary>
        private static void AddRockColumn(RockMesh mesh, List<(Vector2 p, Vector2 n)> outline, float topY, float insetScale, System.Random rng)
        {
            int count = outline.Count;
            var rings = new Vector3[CliffRings.Length][];
            for (int k = 0; k < CliffRings.Length; k++)
            {
                var (depth, inset, noise, jitterY) = CliffRings[k];
                rings[k] = new Vector3[count];
                for (int i = 0; i < count; i++)
                {
                    var (p, n) = outline[i];
                    Vector2 q = p + n * (-inset * insetScale + Signed(rng) * noise);
                    rings[k][i] = new Vector3(q.x, topY + depth + Signed(rng) * jitterY, q.y);
                }
            }

            for (int k = 0; k < rings.Length - 1; k++)
            {
                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    Vector2 outward = outline[i].n + outline[j].n;
                    mesh.Tri(rings[k][i], rings[k][j], rings[k + 1][i], outward);
                    mesh.Tri(rings[k][j], rings[k + 1][j], rings[k + 1][i], outward);
                }
            }

            var last = rings[rings.Length - 1];
            Vector3 tip = Centroid(last);
            tip.y -= CliffTipDepth;
            Vector3 topCenter = Centroid(rings[0]);
            topCenter.y = topY;
            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                mesh.Tri(last[i], last[j], tip, outline[i].n + outline[j].n);
                mesh.Top(topCenter, rings[0][i], rings[0][j]);
            }
        }

        private static Vector2 Dir(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }

        private static float Signed(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        private static Vector3 Centroid(Vector3[] pts)
        {
            Vector3 c = Vector3.zero;
            foreach (var p in pts) c += p;
            return c / pts.Length;
        }

        /// <summary>평면 셰이딩 로우폴리 바위 메시 (삼각형마다 정점 3개). 서브메시 0 = 옆면 (UV = 면 방향별 상자 투영, sideUvMeters마다 한 장),
        /// 1 = 윗면 (UV = 월드 xz, topUvMeters마다 한 장)</summary>
        private sealed class RockMesh
        {
            private readonly List<Vector3> _verts = new List<Vector3>();
            private readonly List<Vector3> _normals = new List<Vector3>();
            private readonly List<Vector2> _uvs = new List<Vector2>();
            private readonly List<int> _side = new List<int>();
            private readonly List<int> _top = new List<int>();
            private readonly float _sideUvMeters;
            private readonly float _topUvMeters;

            public RockMesh(float sideUvMeters, float topUvMeters)
            {
                _sideUvMeters = sideUvMeters;
                _topUvMeters = topUvMeters;
            }

            /// <summary>옆면 삼각형 — 감기 방향은 바깥(xz)을 보도록 맞춘다</summary>
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 outward)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.x * outward.x + n.z * outward.y < 0f)
                {
                    (b, c) = (c, b);
                    n = -n;
                }
                Add(_side, a, b, c, n.normalized, _sideUvMeters);
            }

            /// <summary>윗면 삼각형 — 위를 보도록 맞춘다</summary>
            public void Top(Vector3 a, Vector3 b, Vector3 c)
            {
                if (Vector3.Cross(b - a, c - a).y < 0f) (b, c) = (c, b);
                Add(_top, a, b, c, Vector3.up, _topUvMeters);
            }

            private void Add(List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 n, float uvMeters)
            {
                foreach (var v in new[] { a, b, c })
                {
                    tris.Add(_verts.Count);
                    _verts.Add(v);
                    _normals.Add(n);
                    Vector2 uv;
                    if (Mathf.Abs(n.y) > 0.7f) uv = new Vector2(v.x, v.z);
                    else if (Mathf.Abs(n.x) > Mathf.Abs(n.z)) uv = new Vector2(v.z, v.y);
                    else uv = new Vector2(v.x, v.y);
                    _uvs.Add(uv / uvMeters);
                }
            }

            public Mesh Save(string name)
            {
                string path = $"{ModelFolder}/{name}.asset";
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null)
                {
                    mesh = new Mesh { name = name };
                    AssetDatabase.CreateAsset(mesh, path);
                }
                mesh.Clear();
                mesh.indexFormat = _verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                mesh.SetVertices(_verts);
                mesh.SetNormals(_normals);
                mesh.SetUVs(0, _uvs);
                mesh.subMeshCount = 2;
                mesh.SetTriangles(_side, 0);
                mesh.SetTriangles(_top, 1);
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                EditorUtility.SetDirty(mesh);
                return mesh;
            }
        }

        // ── ⑥ 타워 총알 VFX ──────────────────────────────────

        /// <summary>Hovl 마법 불꽃을 작게: 모든 파티클 시스템 크기 모드 = Hierarchy (루트 스케일로 줄인다)</summary>
        private static void BuildTowerShotVfx()
        {
            ShotVariant(FirePrefabs + "blue.prefab", "VFX_Tower_Ally_Projectile");
            ShotVariant(FirePrefabs + "red.prefab", "VFX_Tower_Enemy_Projectile");
            SpellboundSetupWizard.RefreshVfxCatalogMenu();
        }

        private static void ShotVariant(string sourcePath, string name)
        {
            var asset = Load<GameObject>(sourcePath);
            if (asset == null) return;
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            go.name = name;
            go.transform.localScale = Vector3.one * TowerShotScale;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (ps.gameObject != go && TowerShotHiddenParts.Contains(ps.gameObject.name)) ps.gameObject.SetActive(false);
            }
            PrefabUtility.SaveAsPrefabAsset(go, $"{VfxFolder}/{name}.prefab");
            Object.DestroyImmediate(go);
        }

        // ── ⑦ 해질녘 하늘 · 조명 · 안개 ──────────────────────

        private static void ApplyDuskEnvironment()
        {
            // Procedural 스카이박스는 해가 낮으면 지평선이 노랗게만 나와서, 색을 직접 정한 그라디언트(Panoramic)를 쓴다
            string path = MaterialFolder + "/MAT_Sky_Dusk.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(Shader.Find("Skybox/Panoramic"));
                AssetDatabase.CreateAsset(sky, path);
            }
            sky.shader = Shader.Find("Skybox/Panoramic");
            sky.SetTexture("_MainTex", DuskGradientTexture());
            sky.SetFloat("_Mapping", 1f); // Latitude-Longitude
            sky.SetFloat("_ImageType", 0f); // 360°
            sky.SetFloat("_Exposure", 1f);
            sky.SetFloat("_Rotation", 0f);
            EditorUtility.SetDirty(sky);

            // 해는 서쪽(-X) 지평선 위 8° — Home · Away 양쪽에서 옆으로 보이게
            Light sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                Undo.RecordObject(sun, "Arena Art");
                Undo.RecordObject(sun.transform, "Arena Art");
                sun.transform.rotation = Quaternion.Euler(8f, 90f, 0f);
                sun.color = new Color(1f, 0.63f, 0.4f);
                sun.intensity = 0.9f;
            }

            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.40f, 0.38f, 0.48f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.30f, 0.28f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.11f, 0.11f);
            // 먼 절벽을 지평선 색으로 흐리게 (20m 앞 상대 진영은 거의 그대로)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.36f, 0.27f, 0.26f);
        }

        // 아래(v = 0) → 지평선(v = 0.5) → 천정(v = 1). 해 진 직후: 지평선 주황 · 분홍, 위로 갈수록 남보라
        // 지평선 아래 = 안개색 (RenderSettings.fogColor) → 심연을 내려다보면 절벽이 안개 속으로 이어지듯 사라진다
        private static readonly (float v, Color c)[] DuskStops =
        {
            (0.00f, new Color(0.22f, 0.16f, 0.16f)),
            (0.47f, new Color(0.36f, 0.27f, 0.26f)),
            (0.50f, new Color(0.86f, 0.50f, 0.34f)),
            (0.56f, new Color(0.68f, 0.43f, 0.42f)),
            (0.68f, new Color(0.40f, 0.34f, 0.48f)),
            (0.85f, new Color(0.20f, 0.22f, 0.38f)),
            (1.00f, new Color(0.11f, 0.13f, 0.26f)),
        };

        private static Texture2D DuskGradientTexture()
        {
            const int w = 8, h = 256;
            string path = "Assets/Art/Textures/Sky/TEX_Sky_Dusk.png";
            SpellboundSetupWizard.EnsureFolder("Assets/Art/Textures/Sky");
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            for (int y = 0; y < h; y++)
            {
                float v = y / (h - 1f);
                int i = 1;
                while (i < DuskStops.Length - 1 && DuskStops[i].v < v) i++;
                var (v0, c0) = DuskStops[i - 1];
                var (v1, c1) = DuskStops[i];
                var c = Color.Lerp(c0, c1, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(v0, v1, v)));
                for (int x = 0; x < w; x++) tex.SetPixel(x, y, c);
            }
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.mipmapEnabled = false;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ── 공용 ─────────────────────────────────────────────

        private static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) Debug.LogError("[Arena Art] 에셋 없음: " + path);
            return asset;
        }

        private static GameObject Spawn(GameObject asset, Transform parent, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.name = ArtPrefix + name;
            Undo.RegisterCreatedObjectUndo(go, "Arena Art");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.receiveShadows = true;
            return go;
        }

        /// <summary>아트는 보기 전용 — 게임 로직은 물리를 쓰지 않으므로 메시 콜라이더를 뺀다 (Quest 메모리 · 로딩)</summary>
        private static void StripColliders(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        }

        private static void ClearArt(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name.StartsWith(ArtPrefix)) Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static void HideRenderer(Transform t)
        {
            var r = t.GetComponent<Renderer>();
            if (r == null || !r.enabled) return;
            Undo.RecordObject(r, "Arena Art");
            r.enabled = false;
        }

        private static void SetStatic(GameObject go) =>
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);

        private static Transform FindAnywhere(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
