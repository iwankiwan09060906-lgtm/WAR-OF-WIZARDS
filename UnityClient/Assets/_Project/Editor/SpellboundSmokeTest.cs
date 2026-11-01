// 전투 스모크 테스트 — 헤드셋 없이 04_Battle 을 플레이 모드로 열어 실제 봇전을 돌리고, 오류 수 · 화면을 남긴다.
//
//   Unity.exe -batchmode -projectPath UnityClient -executeMethod SpellboundVR.EditorTools.SpellboundSmokeTest.Run
//             -smokePlayers 4 -smokeSeconds 40 -smokeOut <폴더> -logFile <로그>
//   (메뉴: Spellbound/Debug/Smoke Test 4P)
//
// 결과: <폴더>/smoke_<인원>_*.png (위에서 본 전장 · 내 시점), smoke_<인원>.txt (오류 수 · 경기 상태). 오류가 있으면 종료 코드 1.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using SpellboundVR.Backend;
using SpellboundVR.Core;
using SpellboundVR.Input;
using SpellboundVR.Network;
using SpellboundVR.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    [InitializeOnLoad]
    public static class SpellboundSmokeTest
    {
        private const string ActiveKey = "Spellbound.Smoke.Active";

        static SpellboundSmokeTest()
        {
            EditorApplication.playModeStateChanged += HandlePlayMode;
        }

        public static void Run()
        {
            SessionState.SetBool(ActiveKey, true);
            SessionState.SetInt("Spellbound.Smoke.Players", IntArg("-smokePlayers", 4));
            SessionState.SetInt("Spellbound.Smoke.Seconds", IntArg("-smokeSeconds", 40));
            SessionState.SetString("Spellbound.Smoke.Out", StringArg("-smokeOut", Path.GetFullPath("SmokeTest")));
            EditorSceneManager.OpenScene(SpellboundSetupWizard.BattleScenePath);
            EditorApplication.isPlaying = true;
        }

        [MenuItem("Spellbound/Debug/Smoke Test 4P")]
        private static void RunFromMenu() => Run();

        private static void HandlePlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(ActiveKey, false)) return;
            SessionState.SetBool(ActiveKey, false);
            var go = new GameObject("[Smoke Test]");
            Object.DontDestroyOnLoad(go);
            var driver = go.AddComponent<SmokeDriver>();
            driver.Players = SessionState.GetInt("Spellbound.Smoke.Players", 4);
            driver.Seconds = SessionState.GetInt("Spellbound.Smoke.Seconds", 40);
            driver.OutDir = SessionState.GetString("Spellbound.Smoke.Out", "SmokeTest");
        }

        private static string StringArg(string name, string fallback)
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        private static int IntArg(string name, int fallback) => int.TryParse(StringArg(name, ""), out int v) ? v : fallback;
    }

    public sealed class SmokeDriver : MonoBehaviour
    {
        public int Players = 4;
        public int Seconds = 40;
        public string OutDir;

        private readonly List<string> _errors = new List<string>();
        private readonly HashSet<string> _seen = new HashSet<string>();

        private void OnEnable() => Application.logMessageReceived += HandleLog;

        private void OnDisable() => Application.logMessageReceived -= HandleLog;

        private void HandleLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            string first = message.Split('\n')[0];
            if (_seen.Add(first)) _errors.Add(first + "\n" + stack);
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(OutDir);
            float limit = Time.realtimeSinceStartup + 30f;
            while (MetaFlow.Instance == null && Time.realtimeSinceStartup < limit) yield return null;
            if (MetaFlow.Instance == null)
            {
                Finish("MetaFlow 가 뜨지 않음");
                yield break;
            }
            yield return new WaitForSecondsRealtime(1f);
            MetaFlow.Instance.StartOffline();
            if (MetaState.Deck.Count == 0) MetaState.Deck = new List<string> { "S03", "S07", "S08", "S01", "S11", "S13", "H01", "H04" };
            MetaFlow.Instance.StartMatch(new MatchContext
            {
                Mode = MatchResultService.ModeBot, OpponentName = "Bot", OpponentBot = true, Players = Players,
            });

            var boot = FindAnyObjectByType<GameBootstrap>();
            float start = Time.realtimeSinceStartup;
            int shot = 0;
            int casts = 0;
            while (Time.realtimeSinceStartup - start < Seconds)
            {
                yield return new WaitForSecondsRealtime(1f);
                // 사람 대신 슬롯을 차례로 써서 시전 경로 · VFX 를 지나가게 한다
                var input = FindAnyObjectByType<KeyboardInput>();
                if (input != null)
                {
                    input.AimScreenOverride = new Vector2(Screen.width * (0.2f + 0.3f * (casts % 3)), Screen.height * 0.45f);
                    input.SimulateSlotCast(casts++ % 8);
                }
                int t = Mathf.FloorToInt(Time.realtimeSinceStartup - start);
                if (t >= 8 && shot == 0 || t >= 22 && shot == 1 || t >= Seconds - 2 && shot == 2)
                {
                    shot++;
                    Capture(boot, "t" + t);
                }
            }
            Finish(null);
        }

        private void Capture(GameBootstrap boot, string label)
        {
            var main = Camera.main;
            if (main != null) Save(main, "view_" + label);
            var go = new GameObject("SmokeTopCamera");
            var top = go.AddComponent<Camera>();
            var arena = boot.Session != null ? boot.Session.Arena : null;
            Vector3 center = arena != null ? arena.ToWorld(arena.Center) : Vector3.zero;
            go.transform.SetPositionAndRotation(center + Vector3.up * 46f, Quaternion.Euler(90f, 0f, 0f));
            top.farClipPlane = 200f;
            top.clearFlags = CameraClearFlags.SolidColor;
            top.backgroundColor = new Color(0.05f, 0.05f, 0.1f);
            Save(top, "top_" + label);
            Destroy(go);
        }

        private void Save(Camera cam, string name)
        {
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            var old = cam.targetTexture;
            var active = RenderTexture.active;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, $"smoke_{Players}_{name}.png"), tex.EncodeToPNG());
            cam.targetTexture = old;
            RenderTexture.active = active;
            Destroy(rt);
            Destroy(tex);
        }

        private void Finish(string failure)
        {
            var boot = FindAnyObjectByType<GameBootstrap>();
            var report = new List<string>();
            if (failure != null) report.Add("실패: " + failure);
            var s = boot != null && boot.Session != null ? boot.Session.Events.Latest : null;
            if (s != null)
            {
                report.Add($"phase {(MatchPhase)s.Header.Phase} · 경기 {s.Header.MatchTime:0.0}초 · 유닛 {s.UnitCount} · 투사체 {s.ProjectileCount}");
                for (int t = 0; t < boot.Session.Arena.TeamCount; t++)
                {
                    var p = s.Players[t];
                    report.Add($"  {(Contracts.Team)t}: present {p.Present} bot {p.IsBot} hp {p.Hp} place {p.Place}");
                }
            }
            if (s != null)
            {
                foreach (var st in s.Structures)
                    report.Add($"  구조물 {st.Id} team {st.Team} nexus {st.IsNexus} alive {st.Alive} hp {st.Hp}/{st.MaxHp} uv ({st.U:0.0},{st.V:0.0})");
                foreach (var art in FindObjectsByType<Arena.TeamArt>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var r = art.GetComponentsInChildren<Renderer>(false);
                    report.Add($"  아트 {art.name} team {art.team} active {art.gameObject.activeInHierarchy} pos {art.transform.position} 켜진 렌더러 {r.Length}" +
                               (r.Length > 0 ? $" bounds {r[0].bounds.center}" : ""));
                }
            }
            report.Add($"오류 {_errors.Count}건");
            report.AddRange(_errors);
            string text = string.Join("\n", report);
            File.WriteAllText(Path.Combine(OutDir, $"smoke_{Players}.txt"), text);
            Debug.Log("[SmokeTest]\n" + text);
            EditorApplication.Exit(failure == null && _errors.Count == 0 ? 0 : 1);
        }
    }
}
