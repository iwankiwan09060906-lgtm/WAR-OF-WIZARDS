// 아레나 아트 적용 (KayKit Medieval Hexagon · Dungeon Pack, CC0) — Spellbound > 6. Apply KayKit Arena Art
//   ① 넥서스 · 타워: Body 삭제 → building_barracks_* / building_tower_B_* (blue = Home, red = Away), 피벗 = 바닥
//      넥서스는 플레이어 발판 바로 앞(깊이 3m)으로 옮기고 ArenaLayout.nexusDepth도 갱신 (게임 로직 위치가 바뀜)
//   ② Commander 발판: L · C · R 모두 scaffold_medium (떨어진 발판 사이를 순간이동하는 느낌, 플레이어 2배에 맞춰 폭 3.6m). 팀 색 없음
//      발판 윗면이 바뀌므로 ArenaLayout.commanderPlatformHeight도 갱신. 플레이어 눈높이(OVRCameraRig)는 10m
//   ③ 레인: tileBrickB_largeCrackedA/B를 작은 타일로 빈틈없이 깔기 (레인 폭 = 열 간격, 연한 색 머티리얼), 기존 레인 판은 렌더러만 끈다
//      전장 길이 41m (양 타워 사이 15m, 넥서스 ↔ 타워 10m). 타워 · Away 발판 · 넥서스도 같이 옮기고 ArenaLayout.fieldLength · towerDepth 갱신
//   ④ 바닥(Ground): 같은 타일(조금 덜 연하게, 한 장 5m로 크게)을 위에서 찍어 구운 텍스처를 400m 바닥 전체에 반복
//      (타일 하나가 삼각형 678~2454개라 바닥 전체를 실제 타일로 깔면 Quest에서 감당할 수 없다)
//   ⑤ 하늘 · 조명 · 안개: 18시 무렵 해질녘
// 여러 번 실행해도 같은 결과 (Art_ 접두 오브젝트를 지우고 다시 만든다). 씬은 저장하지 않는다 — 확인 후 Ctrl+S.
// 플레이스홀더로 되돌리려면 Art_* 오브젝트를 지우고 Spellbound > 5. Build Placeholder Arena.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using SpellboundVR.Arena;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundArenaArtBuilder
    {
        private const string Hexagon = "Assets/KayKit_Medieval_Hexagon_Pack_1.0_FREE/Assets/fbx(unity)/buildings/";
        private const string Dungeon = "Assets/KayKit Dungeon Pack 1.0/Models/fbx/";
        private const string MaterialFolder = "Assets/Art/Materials/Arena";
        private const string PrefabFolder = "Assets/Prefabs/Arena";
        private const string ArtPrefix = "Art_";

        // 구조물 키 (Away도 같은 크기). 넥서스 2.4m = 처음 1.6m의 1.5배, 타워 4.8m = 처음 2.4m의 2배
        private const float NexusHeight = 2.4f;
        private const float TowerHeight = 4.8f;
        /// <summary>넥서스 깊이 (자기 Commander 발판 기준, m) — 발판(폭 3.6m) 바로 앞, 겹치지 않게</summary>
        private const float NexusDepth = 3f;
        /// <summary>타워 깊이 (m). 넥서스(3m)와 타워 사이 10m</summary>
        private const float TowerDepth = 13f;
        /// <summary>전장 길이 (m). 양 타워 사이 = 41 - 13 × 2 = 15m</summary>
        private const float FieldLength = 41f;

        // 플레이어 크기 2배: 눈높이 5m → 10m, 발판 폭 1.8m → 3.6m (발판 간격 5m라 서로 떨어져 있다)
        private const float EyeHeight = 10f;
        private const float PlatformWidth = 3.6f;

        // 레인 타일: 레인 하나에 가로 4장 (5m → 1.25m), 세로는 전장 길이에 맞춰 정사각형에 가깝게. 윗면은 지면 위 2cm (몸통은 지면 아래로 묻힘)
        private const int TileColumns = 4;
        private const float TileTop = 0.02f;
        private const float CrackedBChance = 0.3f;
        private const float LaneLighten = 0.35f;
        /// <summary>바닥 타일은 원본보다 연하게, 레인보다는 덜 연하게 (전장과 구분)</summary>
        private const float GroundLighten = 0.15f;

        private const float GroundSize = 400f;
        /// <summary>바닥 타일 한 장 크기 (m) — 레인(1.25m)의 4배로 크게</summary>
        private const float GroundTileSize = 5f;
        /// <summary>바닥 텍스처 한 장 = 타일 4 × 4 (20m)</summary>
        private const int GroundBlockTiles = 4;
        private const int GroundTextureSize = 1024;

        [MenuItem("Spellbound/6. Apply KayKit Arena Art (structures · platforms · lanes · sky)", priority = 6)]
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
            SpellboundSetupWizard.EnsureFolder(PrefabFolder);

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
            BuildStructure(arena, "Home_Nexus", Hexagon + "blue/building_barracks_blue.fbx", NexusHeight);
            BuildStructure(arena, "Home_Tower", Hexagon + "blue/building_tower_B_blue.fbx", TowerHeight);
            BuildStructure(arena, "Away_Nexus", Hexagon + "red/building_barracks_red.fbx", NexusHeight);
            BuildStructure(arena, "Away_Tower", Hexagon + "red/building_tower_B_red.fbx", TowerHeight);

            float deckTop = 0f;
            foreach (var side in new[] { "Home", "Away" })
                foreach (var slot in new[] { "L", "C", "R" })
                    deckTop = Mathf.Max(deckTop, BuildPlatform(arena, side + "_Commander_" + slot, "scaffold_medium"));
            if (layout != null)
            {
                Undo.RecordObject(layout, "Arena Art");
                layout.commanderPlatformHeight = deckTop;
                layout.nexusDepth = NexusDepth;
                EditorUtility.SetDirty(layout);
            }

            float spacing = layout != null ? layout.columnSpacing : 5f;
            BuildLanes(arena, spacing);
            BuildGround(arena, spacing);
            ApplyDuskEnvironment();

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

        private static void BuildStructure(Transform arena, string anchorName, string fbxPath, float height)
        {
            var anchor = FindAnywhere(arena, anchorName);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            if (anchor == null || asset == null)
            {
                Debug.LogError($"[Arena Art] {anchorName} 또는 {fbxPath} 없음");
                return;
            }
            var body = anchor.Find("Body");
            if (body != null) Undo.DestroyObjectImmediate(body.gameObject);
            ClearArt(anchor);

            var model = Spawn(asset, anchor, asset.name);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var b = SpellboundMinionModelBuilder.RendererBounds(model);
            float s = height / b.size.y;
            model.transform.localScale = Vector3.one * s;
            b = SpellboundMinionModelBuilder.RendererBounds(model);
            model.transform.position -= new Vector3(b.center.x - anchor.position.x, b.min.y - anchor.position.y, b.center.z - anchor.position.z);
        }

        // ── ② Commander 발판 ─────────────────────────────────

        /// <summary>발판 모델을 놓고 윗면 높이(m)를 돌려준다</summary>
        private static float BuildPlatform(Transform arena, string commanderName, string model)
        {
            var commander = FindAnywhere(arena, commanderName);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Dungeon + model + ".fbx");
            if (commander == null || asset == null)
            {
                Debug.LogError($"[Arena Art] {commanderName} 또는 {model} 없음");
                return 0f;
            }
            HideRenderer(commander);
            ClearArt(commander);

            var go = Spawn(asset, commander, model);
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
            SetStatic(go);
            return SpellboundMinionModelBuilder.RendererBounds(go).max.y;
        }

        // ── ③ 레인 타일 ──────────────────────────────────────

        private static void BuildLanes(Transform arena, float laneWidth)
        {
            var lanes = arena.Find("Lanes");
            if (lanes == null) return;
            ClearArt(lanes);
            var tileA = TilePrefab("tileBrickB_largeCrackedA", "Lane", LaneLighten);
            var tileB = TilePrefab("tileBrickB_largeCrackedB", "Lane", LaneLighten);
            if (tileA == null || tileB == null) return;
            var nb = TileBounds(tileA);

            var rng = new System.Random(20);
            foreach (var laneName in new[] { "Lane_Left", "Lane_Mid", "Lane_Right" })
            {
                var lane = FindAnywhere(arena, laneName);
                if (lane == null) continue;
                HideRenderer(lane);
                // 레인 사이 빈틈 없이: 레인 폭 = 열 간격
                Undo.RecordObject(lane, "Arena Art");
                var ls = lane.localScale;
                lane.localScale = new Vector3(laneWidth / (lane.lossyScale.x / ls.x), ls.y, FieldLength / (lane.lossyScale.z / ls.z));
                lane.position = new Vector3(lane.position.x, lane.position.y, FieldLength * 0.5f);

                var root = new GameObject(ArtPrefix + laneName + "_Tiles").transform;
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Arena Art");
                root.SetParent(lanes, false);
                SetStatic(root.gameObject);

                Vector3 size = lane.lossyScale;
                float w = size.x / TileColumns;
                int rows = Mathf.Max(1, Mathf.RoundToInt(size.z / w));
                float l = size.z / rows;
                var scale = new Vector3(w / nb.size.x, (w + l) * 0.5f / nb.size.x, l / nb.size.z);
                Vector3 origin = lane.position - new Vector3(size.x, 0f, size.z) * 0.5f;
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < TileColumns; c++)
                    {
                        var prefab = rng.NextDouble() < CrackedBChance ? tileB : tileA;
                        var tile = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
                        var t = tile.transform;
                        t.localScale = scale;
                        t.rotation = Quaternion.Euler(0f, rng.Next(2) * 180f, 0f);
                        // 회전해도 같은 자리에 오도록 bounds 중심 기준으로 배치
                        Vector3 center = origin + new Vector3((c + 0.5f) * w, 0f, (r + 0.5f) * l);
                        Vector3 pivotOffset = t.rotation * Vector3.Scale(nb.center, scale);
                        t.position = new Vector3(center.x - pivotOffset.x, TileTop - nb.max.y * scale.y, center.z - pivotOffset.z);
                        SetStatic(tile);
                    }
                }
            }
        }

        /// <summary>타일 프리팹 (PF_{use}Tile_*): 원본 메시 + 연한 머티리얼(MAT_{use}_*) + 그림자 끔 (인스턴스는 위치만 다르게)</summary>
        private static GameObject TilePrefab(string model, string use, float lighten)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Dungeon + model + ".fbx");
            if (asset == null)
            {
                Debug.LogError("[Arena Art] 타일 FBX 없음: " + model);
                return null;
            }
            var go = Object.Instantiate(asset);
            go.name = $"PF_{use}Tile_{model}";
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = LightMaterial(mats[i], use, lighten);
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{go.name}.prefab");
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>타일 원본 크기 (A 기준: 윗면 = bounds.max.y, B는 위에 잔해가 조금 솟음)</summary>
        private static Bounds TileBounds(GameObject tilePrefab)
        {
            var probe = (GameObject)PrefabUtility.InstantiatePrefab(tilePrefab);
            var nb = SpellboundMinionModelBuilder.RendererBounds(probe);
            Object.DestroyImmediate(probe);
            return nb;
        }

        private static Material LightMaterial(Material src, string use, float lighten)
        {
            if (src == null) return null;
            string path = $"{MaterialFolder}/MAT_{use}_{src.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(src);
                AssetDatabase.CreateAsset(m, path);
            }
            m.CopyPropertiesFromMaterial(src);
            m.color = Color.Lerp(src.color, Color.white, lighten);
            // 무광: 작은 홈마다 하이라이트가 반짝이고(레인), 구운 바닥 텍스처에 반사 얼룩이 남는 것을 막는다
            m.SetFloat("_Glossiness", 0f);
            m.SetFloat("_SpecularHighlights", 0f);
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.SetFloat("_GlossyReflections", 0f);
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ── ④ 바닥 ───────────────────────────────────────────

        private static void BuildGround(Transform arena, float spacing)
        {
            var ground = FindAnywhere(arena, "Ground");
            if (ground == null) return;
            var tileA = TilePrefab("tileBrickB_largeCrackedA", "Ground", GroundLighten);
            var tileB = TilePrefab("tileBrickB_largeCrackedB", "Ground", GroundLighten);
            if (tileA == null || tileB == null) return;

            float tile = GroundTileSize;
            float block = tile * GroundBlockTiles;
            var texture = BakeGroundTexture(tileA, tileB, tile);

            Undo.RecordObject(ground, "Arena Art");
            ground.SetPositionAndRotation(new Vector3(0f, 0f, FieldLength * 0.5f), Quaternion.identity);
            ground.localScale = Vector3.one;
            // 타일 줄눈이 레인 타일과 맞도록 레인 왼쪽 끝(x = -1.5 × 간격) · Home 끝(z = 0)을 텍스처 원점으로
            var mesh = GroundMesh(ground.position, new Vector2(-1.5f * spacing, 0f), block);
            var filter = ground.GetComponent<MeshFilter>();
            if (filter != null)
            {
                Undo.RecordObject(filter, "Arena Art");
                filter.sharedMesh = mesh;
            }
            var collider = ground.GetComponent<MeshCollider>();
            if (collider != null)
            {
                Undo.RecordObject(collider, "Arena Art");
                collider.sharedMesh = mesh;
            }
            var r = ground.GetComponent<Renderer>();
            if (r != null)
            {
                Undo.RecordObject(r, "Arena Art");
                var m = ColorMaterial("MAT_Arena_GroundTiles", Color.white);
                m.mainTexture = texture;
                r.sharedMaterial = m;
            }
            SetStatic(ground.gameObject);
        }

        /// <summary>바닥 사각형 (GroundSize). UV = (월드 xz - 텍스처 원점) / 블록 크기 → 반복 1회 = 타일 4 × 4</summary>
        private static Mesh GroundMesh(Vector3 center, Vector2 uvOrigin, float block)
        {
            const string path = "Assets/Art/Models/Arena/MESH_Arena_Ground.asset";
            SpellboundSetupWizard.EnsureFolder("Assets/Art/Models/Arena");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh { name = "MESH_Arena_Ground" };
                AssetDatabase.CreateAsset(mesh, path);
            }
            float h = GroundSize * 0.5f;
            var verts = new[] { new Vector3(-h, 0f, -h), new Vector3(-h, 0f, h), new Vector3(h, 0f, h), new Vector3(h, 0f, -h) };
            var uvs = new Vector2[4];
            for (int i = 0; i < 4; i++)
                uvs[i] = new Vector2((center.x + verts[i].x - uvOrigin.x) / block, (center.z + verts[i].z - uvOrigin.y) / block);
            mesh.Clear();
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>타일 4 × 4를 프리뷰 씬에 깔고 위에서 직교 카메라로 찍어 반복 텍스처로 저장</summary>
        private static Texture2D BakeGroundTexture(GameObject tileA, GameObject tileB, float tile)
        {
            const string path = "Assets/Art/Textures/Arena/TEX_Ground_Tiles.png";
            SpellboundSetupWizard.EnsureFolder("Assets/Art/Textures/Arena");
            var nb = TileBounds(tileA);
            float block = tile * GroundBlockTiles;
            var scale = new Vector3(tile / nb.size.x, tile / nb.size.x, tile / nb.size.z);

            var preview = EditorSceneManager.NewPreviewScene();
            var rt = RenderTexture.GetTemporary(GroundTextureSize, GroundTextureSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var tex = new Texture2D(GroundTextureSize, GroundTextureSize, TextureFormat.RGB24, false);
            try
            {
                var rng = new System.Random(7);
                for (int r = 0; r < GroundBlockTiles; r++)
                {
                    for (int c = 0; c < GroundBlockTiles; c++)
                    {
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(rng.NextDouble() < CrackedBChance ? tileB : tileA, preview);
                        var t = go.transform;
                        t.localScale = scale;
                        t.rotation = Quaternion.Euler(0f, rng.Next(2) * 180f, 0f);
                        Vector3 pivotOffset = t.rotation * Vector3.Scale(nb.center, scale);
                        t.position = new Vector3((c + 0.5f) * tile - pivotOffset.x, -nb.max.y * scale.y, (r + 0.5f) * tile - pivotOffset.z);
                    }
                }

                var lightGo = new GameObject("BakeLight");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, preview);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = Color.white;
                light.intensity = 1.05f;
                light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(65f, 30f, 0f);

                var camGo = new GameObject("BakeCamera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, preview);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = preview;
                cam.orthographic = true;
                cam.orthographicSize = block * 0.5f;
                cam.aspect = 1f;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 10f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.4f, 0.4f, 0.4f);
                camGo.transform.SetPositionAndRotation(new Vector3(block * 0.5f, 3f, block * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                cam.targetTexture = rt;
                cam.Render();
                cam.targetTexture = null;

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, GroundTextureSize, GroundTextureSize), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.ReleaseTemporary(rt);
                Object.DestroyImmediate(tex);
                EditorSceneManager.ClosePreviewScene(preview);
            }

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = 4;
            importer.maxTextureSize = GroundTextureSize;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ── ⑤ 해질녘 하늘 · 조명 · 안개 ──────────────────────

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
            // 먼 바닥 끝을 지평선 색으로 흐리게 (20m 앞 상대 진영은 거의 그대로)
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.36f, 0.27f, 0.26f);
        }

        // 아래(v = 0) → 지평선(v = 0.5) → 천정(v = 1). 해 진 직후: 지평선 주황 · 분홍, 위로 갈수록 남보라
        private static readonly (float v, Color c)[] DuskStops =
        {
            (0.00f, new Color(0.08f, 0.07f, 0.08f)),
            (0.47f, new Color(0.17f, 0.13f, 0.13f)),
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

        private static GameObject Spawn(GameObject asset, Transform parent, string name)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.name = ArtPrefix + name;
            Undo.RegisterCreatedObjectUndo(go, "Arena Art");
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.receiveShadows = true;
            return go;
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

        private static Material ColorMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            m.SetFloat("_Glossiness", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Transform FindAnywhere(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == name) return t;
            return null;
        }
    }
}
