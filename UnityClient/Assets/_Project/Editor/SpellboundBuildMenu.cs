// 빌드 메뉴 (§2.3 테스트 환경: Quest 3 + PC 서버)
//   Spellbound > Build > Quest APK (Fusion Client)  → Builds/Quest/Spellbound.apk
//   Spellbound > Build > Quest APK (Offline vs Bot) → Builds/Quest/Spellbound_Offline.apk
//   Spellbound > Build > PC Server (Windows)         → Builds/Server/SpellboundServer.exe + RunServer.bat
// 빌드 직전에 GameBootstrap.bootMode를 바꾸고, 빌드 후 원래 값으로 되돌린다.

using System.IO;
using SpellboundVR.Core;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundBuildMenu
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        [MenuItem("Spellbound/Build/Quest APK (Fusion Client → PC server)", priority = 40)]
        public static void BuildQuestClient()
        {
            BuildQuest(BootMode.FusionClient, "Spellbound.apk");
        }

        [MenuItem("Spellbound/Build/Quest APK (Offline vs Bot)", priority = 41)]
        public static void BuildQuestOffline()
        {
            BuildQuest(BootMode.LocalVsBot, "Spellbound_Offline.apk");
        }

        [MenuItem("Spellbound/Build/PC Server (Windows)", priority = 42)]
        public static void BuildServer()
        {
            string dir = Path.Combine(RepoRoot, "Builds", "Server");
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "SpellboundServer.exe");
            if (Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, exe, BootMode.DedicatedServer))
            {
                File.WriteAllText(Path.Combine(dir, "RunServer.bat"),
                    "@echo off\r\nstart \"Spellbound Server\" \"%~dp0SpellboundServer.exe\" -server -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"%~dp0server.log\"\r\n");
                Debug.Log("[Spellbound Build] 서버 빌드 완료: " + exe + " (RunServer.bat로 실행)");
            }
        }

        private static void BuildQuest(BootMode mode, string fileName)
        {
            string dir = Path.Combine(RepoRoot, "Builds", "Quest");
            Directory.CreateDirectory(dir);
            if (Build(BuildTarget.Android, BuildTargetGroup.Android, Path.Combine(dir, fileName), mode))
                Debug.Log("[Spellbound Build] Quest APK 빌드 완료: " + Path.Combine(dir, fileName));
        }

        private static bool Build(BuildTarget target, BuildTargetGroup group, string output, BootMode mode)
        {
            var scene = EditorSceneManager.OpenScene(SpellboundSetupWizard.BattleScenePath, OpenSceneMode.Single);
            var bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
            if (bootstrap == null)
            {
                Debug.LogError("[Spellbound Build] GameBootstrap이 없습니다. 'Spellbound > 1. Setup Battle Scene'을 먼저 실행하세요.");
                return false;
            }

            var previous = bootstrap.bootMode;
            bootstrap.bootMode = mode;
            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.SaveScene(scene);

            try
            {
                if (EditorUserBuildSettings.activeBuildTarget != target)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { SpellboundSetupWizard.BattleScenePath },
                    locationPathName = output,
                    target = target,
                    targetGroup = group,
                    options = BuildOptions.Development,
                };
                var report = BuildPipeline.BuildPlayer(options);
                bool ok = report.summary.result == BuildResult.Succeeded;
                if (!ok) Debug.LogError("[Spellbound Build] 실패: " + report.summary.result + " (errors " + report.summary.totalErrors + ")");
                return ok;
            }
            finally
            {
                scene = EditorSceneManager.OpenScene(SpellboundSetupWizard.BattleScenePath, OpenSceneMode.Single);
                bootstrap = Object.FindFirstObjectByType<GameBootstrap>();
                if (bootstrap != null)
                {
                    bootstrap.bootMode = previous;
                    EditorUtility.SetDirty(bootstrap);
                    EditorSceneManager.SaveScene(scene);
                }
            }
        }
    }
}
