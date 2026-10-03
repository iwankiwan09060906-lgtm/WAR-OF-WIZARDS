// 에디터 자동 셋업 도구 — 사람이 손으로 할 작업을 메뉴 한 번으로 처리한다.
//   Spellbound > 1. Setup Battle Scene
//     ① 데이터 에셋 생성 (이미 있으면 건드리지 않음)
//        MatchRuleConfig / BotConfig / Minion_* / Spell_S03 · S07 · S08 / SpellCatalog / Rune_* / RuneTemplateLibrary / VFXCatalog
//     ② 네트워크 프리팹 생성: Prefabs/Network/NetworkMatchState, NetworkPlayer (+ Fusion 프리팹 테이블 갱신)
//     ③ 04_Battle 씬 연결: [Spellbound](GameBootstrap) · ArenaLayout · OVRHandPrefab(좌/우) · 참조 할당 → 저장
//   Spellbound > Mode > ... : GameBootstrap 실행 모드 전환
//   Spellbound > Build > ... : Quest APK / PC 서버 빌드

using System.Collections.Generic;
using System.IO;
using Fusion;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Core;
using SpellboundVR.Gesture;
using SpellboundVR.Network;
using SpellboundVR.Presentation;
using SpellboundVR.Spells;
using SpellboundVR.XR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundSetupWizard
    {
        public const string BattleScenePath = "Assets/Scenes/Client/04_Battle.unity";
        private const string CoreData = "Assets/_Project/Core/Data";
        private const string BotData = "Assets/_Project/Bot/Data";
        private const string MinionData = "Assets/_Project/Combat/Minion/Data";
        private const string SpellData = "Assets/_Project/Spells/Definition/Data";
        private const string RuneData = "Assets/_Project/Gesture/Templates/Data";
        private const string PresentationData = "Assets/_Project/Presentation/Data";
        private const string NetworkPrefabs = "Assets/Prefabs/Network";
        private const string OvrHandPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRHandPrefab.prefab";

        [MenuItem("Spellbound/1. Setup Battle Scene (Assets + Prefabs + Scene)", priority = 1)]
        public static void SetupAll()
        {
            EnsureScriptingDefines();
            var assets = CreateDataAssets();
            var prefabs = CreateNetworkPrefabs();
            if (!SetupBattleScene(assets, prefabs)) return;
            Debug.Log("[Spellbound Setup] 완료 — Play 버튼으로 로컬 봇전을 시작할 수 있습니다.");
        }

        [MenuItem("Spellbound/2. Create Data Assets Only", priority = 2)]
        public static void CreateDataAssetsMenu()
        {
            CreateDataAssets();
        }

        // ── ⓪ 스크립팅 정의 ───────────────────────────────────

        /// <summary>
        /// Addressables 패키지가 (unity-mcp-server 의존성으로) 설치돼 있으면 Fusion이 Addressables 씬 조회를 켜고,
        /// 미설정 Addressables가 런타임 에러를 남긴다 (에디터 Error Pause 시 서버가 멈춤).
        /// 이 프로젝트는 Addressables를 쓰지 않으므로 Fusion 공식 스위치로 끈다.
        /// </summary>
        [MenuItem("Spellbound/4. Apply Scripting Defines (FUSION_DISABLE_ADDRESSABLES)", priority = 4)]
        public static void EnsureScriptingDefines()
        {
            AddDefine(UnityEditor.Build.NamedBuildTarget.Android, "FUSION_DISABLE_ADDRESSABLES");
            AddDefine(UnityEditor.Build.NamedBuildTarget.Standalone, "FUSION_DISABLE_ADDRESSABLES");
            AddDefine(UnityEditor.Build.NamedBuildTarget.Server, "FUSION_DISABLE_ADDRESSABLES");
        }

        private static void AddDefine(UnityEditor.Build.NamedBuildTarget target, string define)
        {
            string current = PlayerSettings.GetScriptingDefineSymbols(target);
            var list = new List<string>(current.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (list.Contains(define)) return;
            list.Add(define);
            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", list));
            Debug.Log($"[Spellbound Setup] {target.TargetName} 스크립팅 정의 추가: {define}");
        }

        // ── ① 데이터 에셋 ─────────────────────────────────────

        public sealed class DataAssets
        {
            public MatchRuleConfig Rules;
            public BotConfig Bot;
            public MinionDefinition Melee, Ranged, Brute;
            public SpellCatalog Catalog;
            public RuneTemplateLibrary Runes;
            public VFXCatalog Vfx;
        }

        public static DataAssets CreateDataAssets()
        {
            EnsureFolder(CoreData);
            EnsureFolder(BotData);
            EnsureFolder(MinionData);
            EnsureFolder(SpellData);
            EnsureFolder(RuneData);
            EnsureFolder(PresentationData);

            var a = new DataAssets
            {
                Rules = LoadOrCreate(CoreData + "/MatchRuleConfig.asset", MatchRuleConfig.CreateDefault),
                Bot = LoadOrCreate(BotData + "/BotConfig.asset", BotConfig.CreateDefault),
                Melee = LoadOrCreate(MinionData + "/Minion_Melee.asset", () => MinionDefinition.CreateDefault(MinionKind.Melee)),
                Ranged = LoadOrCreate(MinionData + "/Minion_Ranged.asset", () => MinionDefinition.CreateDefault(MinionKind.Ranged)),
                Brute = LoadOrCreate(MinionData + "/Minion_Brute.asset", () => MinionDefinition.CreateDefault(MinionKind.Brute)),
            };

            var fireball = LoadOrCreate(SpellData + "/Spell_S03_Fireball.asset", SpellDefinition.CreateFireballDefault);
            var gatling = LoadOrCreate(SpellData + "/Spell_S07_Gatling.asset", SpellDefinition.CreateGatlingDefault);
            var shield = LoadOrCreate(SpellData + "/Spell_S08_Shield.asset", SpellDefinition.CreateShieldDefault);
            a.Catalog = LoadOrCreate(SpellData + "/SpellCatalog.asset", () => ScriptableObject.CreateInstance<SpellCatalog>());
            AddUnique(a.Catalog.Spells, fireball);
            AddUnique(a.Catalog.Spells, gatling);
            AddUnique(a.Catalog.Spells, shield);
            EditorUtility.SetDirty(a.Catalog);

            var runeFire = LoadOrCreate(RuneData + "/Rune_S03_Fireball.asset", RuneTemplateLibrary.CreateFireballTemplate);
            var runeGat = LoadOrCreate(RuneData + "/Rune_S07_Gatling.asset", RuneTemplateLibrary.CreateGatlingTemplate);
            var runeShield = LoadOrCreate(RuneData + "/Rune_S08_Shield.asset", RuneTemplateLibrary.CreateShieldTemplate);
            a.Runes = LoadOrCreate(RuneData + "/RuneTemplateLibrary.asset", () => ScriptableObject.CreateInstance<RuneTemplateLibrary>());
            AddUnique(a.Runes.Templates, runeFire);
            AddUnique(a.Runes.Templates, runeGat);
            AddUnique(a.Runes.Templates, runeShield);
            EditorUtility.SetDirty(a.Runes);

            a.Vfx = LoadOrCreate(PresentationData + "/VFXCatalog.asset", VFXCatalog.CreateDefault);
            RefreshVfxCatalog(a.Vfx);

            AssetDatabase.SaveAssets();
            Debug.Log("[Spellbound Setup] 데이터 에셋 준비 완료");
            return a;
        }

        /// <summary>프로젝트의 VFX_* / MDL_* 프리팹을 카탈로그에 등록 (§25.3 · §25.4 이름 규칙)</summary>
        [MenuItem("Spellbound/3. Refresh VFX Catalog (VFX_* / MDL_* prefabs)", priority = 3)]
        public static void RefreshVfxCatalogMenu()
        {
            var vfx = AssetDatabase.LoadAssetAtPath<VFXCatalog>(PresentationData + "/VFXCatalog.asset");
            if (vfx == null) vfx = CreateDataAssets().Vfx;
            RefreshVfxCatalog(vfx);
            AssetDatabase.SaveAssets();
        }

        private static void RefreshVfxCatalog(VFXCatalog vfx)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            int added = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                if (!name.StartsWith("VFX_") && !name.StartsWith("MDL_")) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && !vfx.Prefabs.Contains(prefab))
                {
                    vfx.Prefabs.Add(prefab);
                    added++;
                }
            }
            vfx.Prefabs.RemoveAll(p => p == null);
            EditorUtility.SetDirty(vfx);
            Debug.Log($"[Spellbound Setup] VFXCatalog: {added}개 추가, 총 {vfx.Prefabs.Count}개");
        }

        // ── ② 네트워크 프리팹 ─────────────────────────────────

        public sealed class NetPrefabs
        {
            public NetworkObject MatchState;
            public NetworkObject Player;
        }

        public static NetPrefabs CreateNetworkPrefabs()
        {
            EnsureFolder(NetworkPrefabs);
            var result = new NetPrefabs
            {
                MatchState = LoadOrCreatePrefab(NetworkPrefabs + "/NetworkMatchState.prefab", "NetworkMatchState", typeof(NetworkMatchState)),
                Player = LoadOrCreatePrefab(NetworkPrefabs + "/NetworkPlayer.prefab", "NetworkPlayer", typeof(NetworkPlayer)),
            };
            RebuildFusionPrefabTable();
            return result;
        }

        private static NetworkObject LoadOrCreatePrefab(string path, string name, System.Type behaviour)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing.GetComponent<NetworkObject>();

            var go = new GameObject(name);
            go.AddComponent<NetworkObject>();
            go.AddComponent(behaviour);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            Debug.Log("[Spellbound Setup] 네트워크 프리팹 생성: " + path);
            return prefab.GetComponent<NetworkObject>();
        }

        private static void RebuildFusionPrefabTable()
        {
            // Fusion 버전에 따라 위치가 달라 리플렉션으로 호출 (없으면 Fusion이 임포트 시 자동 등록)
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("Fusion.Editor.NetworkProjectConfigUtilities");
                if (type == null) continue;
                var m = type.GetMethod("RebuildPrefabTable", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (m != null && m.GetParameters().Length == 0)
                {
                    m.Invoke(null, null);
                    Debug.Log("[Spellbound Setup] Fusion 프리팹 테이블 갱신");
                }
                return;
            }
        }

        // ── ③ 씬 연결 ────────────────────────────────────────

        public static bool SetupBattleScene(DataAssets assets, NetPrefabs prefabs)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != BattleScenePath)
            {
                if (scene.isDirty)
                {
                    Debug.LogError("[Spellbound Setup] 현재 씬에 저장되지 않은 변경이 있습니다. 저장 후 다시 실행하세요.");
                    return false;
                }
                scene = EditorSceneManager.OpenScene(BattleScenePath, OpenSceneMode.Single);
            }

            // ArenaLayout
            var layout = Object.FindFirstObjectByType<ArenaLayout>();
            if (layout == null)
            {
                var go = new GameObject("[ArenaLayout]");
                layout = go.AddComponent<ArenaLayout>();
                Undo.RegisterCreatedObjectUndo(go, "ArenaLayout");
            }
            var center = GameObject.Find("Commander_Slot_2");
            if (layout.homeBaseAnchor == null && center != null) layout.homeBaseAnchor = center.transform;
            // 사람이 배치한 구조물 자리 (Arena/Cube z=5, Arena/Cylinder z=8) → Home 넥서스 · 타워 비주얼
            var arenaRoot = GameObject.Find("Arena");
            if (arenaRoot != null)
            {
                var cube = arenaRoot.transform.Find("Cube");
                var cylinder = arenaRoot.transform.Find("Cylinder");
                if (layout.homeNexusVisual == null && cube != null) layout.homeNexusVisual = cube;
                if (layout.homeTowerVisual == null && cylinder != null) layout.homeTowerVisual = cylinder;
            }
            EditorUtility.SetDirty(layout);

            // OVRCameraRig + 손
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            HandJointProvider leftProvider = null, rightProvider = null;
            if (rig != null)
            {
                rig.EnsureGameObjectIntegrity();
                leftProvider = EnsureHand(rig.leftHandAnchor, false);
                rightProvider = EnsureHand(rig.rightHandAnchor, true);
            }
            else
            {
                Debug.LogWarning("[Spellbound Setup] OVRCameraRig가 없습니다 — 키보드 입력으로만 동작합니다.");
            }

            // [Spellbound] GameBootstrap
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                var go = new GameObject("[Spellbound]");
                bootstrap = go.AddComponent<GameBootstrap>();
                Undo.RegisterCreatedObjectUndo(go, "GameBootstrap");
            }
            bootstrap.rules = assets.Rules;
            bootstrap.spellCatalog = assets.Catalog;
            bootstrap.runeLibrary = assets.Runes;
            bootstrap.botConfig = assets.Bot;
            bootstrap.meleeDefinition = assets.Melee;
            bootstrap.rangedDefinition = assets.Ranged;
            bootstrap.bruteDefinition = assets.Brute;
            bootstrap.vfxCatalog = assets.Vfx;
            bootstrap.arenaLayout = layout;
            bootstrap.matchStatePrefab = prefabs.MatchState;
            bootstrap.playerPrefab = prefabs.Player;
            if (rig != null)
            {
                bootstrap.playerRig = rig.transform;
                bootstrap.head = rig.centerEyeAnchor;
                bootstrap.leftHandAnchor = rig.leftHandAnchor;
                bootstrap.rightHandAnchor = rig.rightHandAnchor;
            }
            EditorUtility.SetDirty(bootstrap);

            EnsureSceneInBuildSettings(BattleScenePath);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[Spellbound Setup] 04_Battle 씬 연결 · 저장 완료" +
                      (leftProvider != null && rightProvider != null ? " (OVRHand 좌/우 포함)" : ""));
            return true;
        }

        private static HandJointProvider EnsureHand(Transform anchor, bool isRight)
        {
            if (anchor == null) return null;
            var provider = anchor.GetComponentInChildren<HandJointProvider>(true);
            if (provider != null) return provider;

            var hand = anchor.GetComponentInChildren<OVRHand>(true);
            if (hand == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(OvrHandPrefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[Spellbound Setup] OVRHandPrefab을 찾지 못함: " + OvrHandPrefabPath + " — 런타임 자동 생성으로 대체됩니다.");
                    return null;
                }
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
                inst.name = isRight ? "OVRHandPrefab_Right" : "OVRHandPrefab_Left";
                Undo.RegisterCreatedObjectUndo(inst, "OVRHand");
                hand = inst.GetComponent<OVRHand>();
            }

            var so = new SerializedObject(hand);
            var handType = so.FindProperty("HandType");
            if (handType != null)
            {
                handType.intValue = (int)(isRight ? OVRHand.Hand.HandRight : OVRHand.Hand.HandLeft);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            hand.OnValidate(); // 스켈레톤 · 메시 타입을 손 방향에 맞춘다

            var skeleton = hand.GetComponent<OVRSkeleton>();
            provider = hand.gameObject.AddComponent<HandJointProvider>();
            provider.isRightHand = isRight;
            provider.hand = hand;
            provider.skeleton = skeleton;
            EditorUtility.SetDirty(hand);
            EditorUtility.SetDirty(provider);
            return provider;
        }

        private static void EnsureSceneInBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes) if (s.path == path) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ── 실행 모드 전환 ────────────────────────────────────

        [MenuItem("Spellbound/Mode/Local vs Bot (editor · Quest offline)", priority = 20)]
        public static void ModeLocal() => SetMode(BootMode.LocalVsBot);

        [MenuItem("Spellbound/Mode/Fusion Client (Quest → PC server)", priority = 21)]
        public static void ModeClient() => SetMode(BootMode.FusionClient);

        [MenuItem("Spellbound/Mode/Dedicated Server (PC)", priority = 22)]
        public static void ModeServer() => SetMode(BootMode.DedicatedServer);

        [MenuItem("Spellbound/Mode/Auto (command line -server / -client)", priority = 23)]
        public static void ModeAuto() => SetMode(BootMode.Auto);

        private static void SetMode(BootMode mode)
        {
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[Spellbound] GameBootstrap이 없습니다. 먼저 'Spellbound > 1. Setup Battle Scene' 실행");
                return;
            }
            bootstrap.bootMode = mode;
            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            EditorSceneManager.SaveScene(bootstrap.gameObject.scene);
            Debug.Log("[Spellbound] 실행 모드 → " + mode);
        }

        // ── 공용 ─────────────────────────────────────────────

        private static T LoadOrCreate<T>(string path, System.Func<T> factory) where T : ScriptableObject
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null) return existing;
            var created = factory();
            created.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(created, path);
            Debug.Log("[Spellbound Setup] 에셋 생성: " + path);
            return created;
        }

        private static void AddUnique<T>(List<T> list, T item) where T : Object
        {
            if (item != null && !list.Contains(item)) list.Add(item);
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
