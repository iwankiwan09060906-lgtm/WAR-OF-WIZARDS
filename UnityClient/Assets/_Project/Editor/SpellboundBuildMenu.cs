// 빌드 메뉴 (§2.3 테스트 환경: Quest 3 + PC 서버)
//   Spellbound > Build > ★ Quest APK — VR 전체 흐름 → Builds/Quest/WarOfWizards_VR.apk  (추천: 로그인 → 덱 → 경기 → 결과 저장 → 로비/종료)
//     경기는 헤드셋 안에서 봇과 (PC 게임 서버 불필요). 로그인 · 덱 · 전적은 이 PC의 웹 백엔드(StartServer.bat)에 저장된다.
//   Spellbound > Build > Quest APK (Fusion Client)  → Builds/Quest/Spellbound.apk
//   Spellbound > Build > Quest APK (Offline vs Bot) → Builds/Quest/Spellbound_Offline.apk
//   Spellbound > Build > Quest APK (Offline vs Bot, Clean Build) → 같은 파일, 빌드 캐시를 비우고 처음부터 (기기에서 시작 직후 꺼질 때)
//   Spellbound > Build > PC Server (Windows)         → Builds/Server/SpellboundServer.exe + RunServer.bat
// 빌드 직전에 GameBootstrap.bootMode를 바꾸고, 빌드 후 원래 값으로 되돌린다.
// Quest 빌드는 이 PC의 LAN 주소(http://IP:8080)를 Resources/ServerConfig/server_url.txt 에 넣어, 헤드셋에서 주소를 칠 필요가 없게 한다.

using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SpellboundVR.Core;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundBuildMenu
    {
        private static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));

        private const string BakedUrlPath = "Assets/_Project/Backend/Resources/ServerConfig/server_url.txt";

        [MenuItem("Spellbound/Build/★ Quest APK — VR 전체 흐름 (로그인 → 덱 → 경기 → 결과 저장)", priority = 39)]
        public static void BuildQuestFullFlow()
        {
            BuildQuest(BootMode.LocalVsBot, "WarOfWizards_VR.apk");
        }

        [MenuItem("Spellbound/Build/Write Quest Server Address (this PC)", priority = 44)]
        public static void WriteServerAddressMenu()
        {
            string url = WriteServerAddress();
            EditorUtility.DisplayDialog("Quest 서버 주소", url != null
                ? $"Quest 빌드에 넣을 백엔드 주소:\n{url}\n\n헤드셋과 이 PC가 같은 Wi-Fi여야 하고, Backend/open-firewall.bat 으로 8080 포트를 열어야 합니다."
                : "이 PC의 LAN IPv4 주소를 찾지 못했습니다. 헤드셋의 C01 '주소 변경'에서 직접 입력하세요.", "확인");
        }

        /// <summary>이 PC의 LAN 주소를 빌드용 리소스에 쓴다 (찾지 못하면 null)</summary>
        public static string WriteServerAddress()
        {
            string ip = FindLanIPv4();
            if (ip == null) return null;
            string url = "http://" + ip + ":8080";
            string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BakedUrlPath));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, "# Quest 빌드에 들어가는 웹 백엔드 주소 (Spellbound > Build 가 자동으로 씀, git 제외)\n" + url + "\n");
            AssetDatabase.ImportAsset(BakedUrlPath);
            Debug.Log("[Spellbound Build] Quest 백엔드 주소: " + url);
            return url;
        }

        /// <summary>게이트웨이가 있는 실제 네트워크의 IPv4 (가상 어댑터 · 루프백 제외, 사설 대역 우선)</summary>
        private static string FindLanIPv4()
        {
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(n => new { Props = n.GetIPProperties(), Wifi = n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 })
                .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .SelectMany(x => x.Props.UnicastAddresses
                    .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                    .Select(a => new { Ip = a.Address.ToString(), x.Wifi }))
                .OrderByDescending(c => IsPrivate(c.Ip))
                .ThenByDescending(c => c.Wifi)
                .ToList();
            return candidates.Count > 0 ? candidates[0].Ip : null;
        }

        private static bool IsPrivate(string ip)
        {
            if (ip.StartsWith("192.168.") || ip.StartsWith("10.")) return true;
            var p = ip.Split('.');
            return p.Length == 4 && p[0] == "172" && int.TryParse(p[1], out int b) && b >= 16 && b <= 31;
        }

        [MenuItem("Spellbound/Build/Quest APK (Fusion Client → PC server)", priority = 40)]
        public static void BuildQuestClient()
        {
            BuildQuest(BootMode.FusionClient, "Spellbound.apk");
        }

        /// <summary>빌드를 중간에 취소한 뒤엔 이걸로 — 일반 빌드는 반쯤 쓰인 에셋 데이터를 그대로 재사용해 Quest 에서 검은 화면이 된다</summary>
        [MenuItem("Spellbound/Build/Quest APK (Fusion Client → PC server, Clean Build)", priority = 40)]
        public static void BuildQuestClientClean()
        {
            BuildQuest(BootMode.FusionClient, "Spellbound.apk", clean: true);
        }

        /// <summary>Meta 개발자 대시보드 업로드용 — Development 끔, Version Code 자동 +1, PC 서버 접속(FusionClient), 서버 주소 굽기</summary>
        [MenuItem("Spellbound/Build/Quest APK (Release, 스토어 업로드용)", priority = 38)]
        public static void BuildQuestRelease()
        {
            PlayerSettings.Android.bundleVersionCode++; // 대시보드는 같은 Version Code 를 두 번 받지 않는다
            AssetDatabase.SaveAssets();
            Debug.Log("[Spellbound Build] Release Version Code = " + PlayerSettings.Android.bundleVersionCode + " (패키지 " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android) + ")");
            BuildQuest(BootMode.FusionClient, "Spellbound_Release.apk", release: true);
        }

        [MenuItem("Spellbound/Build/Quest APK (Offline vs Bot)", priority = 41)]
        public static void BuildQuestOffline()
        {
            BuildQuest(BootMode.LocalVsBot, "Spellbound_Offline.apk");
        }

        [MenuItem("Spellbound/Build/Quest APK (Offline vs Bot, Clean Build)", priority = 43)]
        public static void BuildQuestOfflineClean()
        {
            BuildQuest(BootMode.LocalVsBot, "Spellbound_Offline.apk", clean: true);
        }

        [MenuItem("Spellbound/Build/PC Server (Windows)", priority = 42)]
        public static void BuildServer()
        {
            string dir = Path.Combine(RepoRoot, "Builds", "Server");
            Directory.CreateDirectory(dir);
            string exe = Path.Combine(dir, "SpellboundServer.exe");
            string building = Path.Combine(dir, ".building");
            StopRunningServers(building);
            bool built;
            try { built = Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, exe, BootMode.DedicatedServer); }
            finally { File.Delete(building); }
            if (built)
            {
                // 서버 하나가 한 종류만 연다 — 4인 개인전(기본) · 1:1 · 다인전은 세션 이름이 다르다 (GameBootstrap.SessionFor)
                File.WriteAllText(Path.Combine(dir, "RunServer.bat"),
                    "@echo off\r\nstart \"Spellbound Server\" \"%~dp0SpellboundServer.exe\" -server -ffa -nodemo -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"%~dp0server.log\"\r\n");
                File.WriteAllText(Path.Combine(dir, "RunServer1v1.bat"),
                    "@echo off\r\nstart \"Spellbound Server 1v1\" \"%~dp0SpellboundServer.exe\" -server -duel -nodemo -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"%~dp0server_1v1.log\"\r\n");
                File.WriteAllText(Path.Combine(dir, "RunServerPolygon.bat"),
                    "@echo off\r\nstart \"Spellbound Server Polygon\" \"%~dp0SpellboundServer.exe\" -server -polygon -nodemo -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile \"%~dp0server_poly.log\"\r\n");
                // 전시 시연: 6인 고정 · 끝나지 않는 경기 · 옵저버 창 (StartServer.bat 은 꺼지면 다시 켠다)
                File.WriteAllText(Path.Combine(dir, "RunServerDemo.bat"),
                    "@echo off\r\nstart \"Spellbound Demo Server\" \"%~dp0SpellboundServer.exe\" -server -demo -screen-fullscreen 1 -logFile \"%~dp0server_demo.log\"\r\n");
                Debug.Log("[Spellbound Build] 서버 빌드 완료: " + exe + " (RunServerDemo.bat = 전시 시연, RunServer.bat = 4인 개인전, RunServer1v1.bat = 1:1, RunServerPolygon.bat = 다인전)");
            }
        }

        /// <summary>StartServer.bat 이 켠 게임 서버가 exe 를 잡고 있으면 빌드가 실패한다 — 끄고, 빌드가 끝날 때까지 다시 켜지 않게 표시한다</summary>
        private static void StopRunningServers(string buildingMarker)
        {
            File.WriteAllText(buildingMarker, "");
            var info = new System.Diagnostics.ProcessStartInfo("taskkill", "/F /IM SpellboundServer.exe") { CreateNoWindow = true, UseShellExecute = false };
            using (var process = System.Diagnostics.Process.Start(info)) process.WaitForExit(5000);
        }

        private static void BuildQuest(BootMode mode, string fileName, bool clean = false, bool release = false)
        {
            string dir = Path.Combine(RepoRoot, "Builds", "Quest");
            Directory.CreateDirectory(dir);
            string url = WriteServerAddress();
            if (url == null) Debug.LogWarning("[Spellbound Build] 이 PC의 LAN 주소를 찾지 못해 Quest에서 C01 '주소 변경'으로 직접 넣어야 합니다.");
            if (Build(BuildTarget.Android, BuildTargetGroup.Android, Path.Combine(dir, fileName), mode, clean, release))
                Debug.Log("[Spellbound Build] Quest APK 빌드 완료: " + Path.Combine(dir, fileName) + (url != null ? "  (백엔드 " + url + ")" : ""));
        }

        private static bool Build(BuildTarget target, BuildTargetGroup group, string output, BootMode mode, bool clean = false, bool release = false)
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
                    options = (release ? BuildOptions.None : BuildOptions.Development) | (clean ? BuildOptions.CleanBuildCache : BuildOptions.None),
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
