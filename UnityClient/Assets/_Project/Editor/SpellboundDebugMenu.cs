// 플레이 모드 디버그 메뉴 — Game 뷰 포커스 없이도 시전 흐름 전체를 재현한다 (§37 "봇 수동 조종으로 재현").
//   Draw Rune S03/S07/S08 : 디자인 도형(여러 획)을 화면에 그린 것처럼 입력 (Drawing 유지 — 다음에 Fist로 인식)
//   Test All 19 Runes     : 19종 도형을 흔들어 그리고 → 주먹 → 인식 결과를 [RuneTest] 요약으로 출력 (GameBootstrap > Recognize All Runes 필요)
//   Fist / Point / Palm   : 오른손 자세 이벤트 (그리는 중 Fist = 그리기 끝 → $P+ 인식 → Armed)
//   Cast Slot 1~3         : 키보드 대체 입력(1~8)과 동일
//   Aim Left/Center/Right : 조준 화면 좌표 고정 (해제: Aim Mouse)

using SpellboundVR.Gesture;
using SpellboundVR.Input;
using SpellboundVR.Spells;
using UnityEditor;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundDebugMenu
    {
        private static KeyboardInput Keyboard
        {
            get
            {
                if (!Application.isPlaying)
                {
                    Debug.LogWarning("[Spellbound Debug] 플레이 모드에서만 동작합니다.");
                    return null;
                }
                var k = Object.FindFirstObjectByType<KeyboardInput>();
                if (k == null) Debug.LogWarning("[Spellbound Debug] KeyboardInput이 없습니다 (핸드트래킹 모드).");
                return k;
            }
        }

        [MenuItem("Spellbound/Debug/Draw Rune S03 Fireball (crown)", priority = 60)]
        public static void DrawFireball() => DrawDesign(SpellIds.S03_Fireball);

        [MenuItem("Spellbound/Debug/Draw Rune S07 Gatling (5 lines)", priority = 61)]
        public static void DrawGatling() => DrawDesign(SpellIds.S07_Gatling);

        [MenuItem("Spellbound/Debug/Draw Rune S08 Shield (shield)", priority = 62)]
        public static void DrawShield() => DrawDesign(SpellIds.S08_Shield);

        /// <summary>19종을 차례로: 취소 → 조금 흔들어 그리기 → 주먹 → 인식 결과 확인 → 취소</summary>
        [MenuItem("Spellbound/Debug/Test All 19 Runes", priority = 63)]
        public static void TestAllRunes()
        {
            var k = Keyboard;
            var cast = Object.FindFirstObjectByType<CastStateMachine>();
            if (k == null || cast == null) return;
            if (!cast.recognizeAllRunes) Debug.LogWarning("[RuneTest] GameBootstrap > Recognize All Runes가 꺼져 있어 덱 밖 룬은 실패합니다.");
            var rng = new System.Random(5);
            int ok = 0, got = 0;
            System.Action<SpellDefinition, int> onRecognized = (def, slot) => got = def.SpellId;
            cast.OnRuneRecognized += onRecognized; // 즉시 시전 스킬(덱 안 S08)은 주먹 직후 선택이 비워지므로 이벤트로 받는다
            var failed = new System.Text.StringBuilder();
            foreach (var d in RuneDesigns.All)
            {
                cast.Cancel(CastCancelReason.PalmOpen);
                float shear = ((float)rng.NextDouble() - 0.5f) * 0.2f, sx = 0.9f + (float)rng.NextDouble() * 0.2f;
                foreach (var stroke in d.Strokes)
                {
                    var pts = new Vector2[stroke.Length];
                    for (int i = 0; i < stroke.Length; i++)
                    {
                        var p = stroke[i];
                        pts[i] = new Vector2(500f + (p.x + shear * p.y) * sx * 180f + Jitter(rng), 300f + p.y * 180f + Jitter(rng));
                    }
                    k.SimulateStroke(pts);
                }
                got = 0;
                k.SimulateFist();
                if (got == d.SpellId) ok++;
                else failed.Append($" {SpellIds.Code(d.SpellId)}→{SpellIds.Code(got)}");
                cast.Cancel(CastCancelReason.PalmOpen);
            }
            cast.OnRuneRecognized -= onRecognized;
            Debug.Log($"[RuneTest] 19종 자동 테스트: {ok}/{RuneDesigns.All.Count} 정답" + (failed.Length > 0 ? " · 실패:" + failed : ""));
        }

        private static float Jitter(System.Random rng) => ((float)rng.NextDouble() - 0.5f) * 3f;

        [MenuItem("Spellbound/Debug/Right Hand Fist", priority = 70)]
        public static void Fist() => Keyboard?.SimulateFist();

        [MenuItem("Spellbound/Debug/Right Hand Point", priority = 71)]
        public static void Point() => Keyboard?.SimulatePoint();

        [MenuItem("Spellbound/Debug/Right Hand Palm Open (cancel)", priority = 72)]
        public static void Palm() => Keyboard?.SimulatePalmOpen();

        [MenuItem("Spellbound/Debug/Cast Slot 1", priority = 80)]
        public static void Slot1() => Keyboard?.SimulateSlotCast(0);

        [MenuItem("Spellbound/Debug/Cast Slot 2", priority = 81)]
        public static void Slot2() => Keyboard?.SimulateSlotCast(1);

        [MenuItem("Spellbound/Debug/Cast Slot 3", priority = 82)]
        public static void Slot3() => Keyboard?.SimulateSlotCast(2);

        [MenuItem("Spellbound/Debug/Move Left", priority = 90)]
        public static void MoveLeft() => Keyboard?.SimulateMove(-1);

        [MenuItem("Spellbound/Debug/Move Right", priority = 91)]
        public static void MoveRight() => Keyboard?.SimulateMove(1);

        [MenuItem("Spellbound/Debug/Aim Left Lane Far", priority = 100)]
        public static void AimLeft() => SetAim(0.3f, 0.55f);

        [MenuItem("Spellbound/Debug/Aim Center Lane Far", priority = 101)]
        public static void AimCenter() => SetAim(0.5f, 0.55f);

        [MenuItem("Spellbound/Debug/Aim Right Lane Far", priority = 102)]
        public static void AimRight() => SetAim(0.7f, 0.55f);

        [MenuItem("Spellbound/Debug/Aim Mouse", priority = 103)]
        public static void AimMouse()
        {
            var k = Keyboard;
            if (k != null) k.AimScreenOverride = null;
        }

        private static void SetAim(float x01, float y01)
        {
            var k = Keyboard;
            if (k == null) return;
            var cam = k.viewCamera != null ? k.viewCamera : Camera.main;
            float w = cam != null ? cam.pixelWidth : Screen.width;
            float h = cam != null ? cam.pixelHeight : Screen.height;
            k.AimScreenOverride = new Vector2(w * x01, h * y01);
            Debug.Log("[Spellbound Debug] 조준 고정: " + k.AimScreenOverride);
        }

        /// <summary>디자인 도형의 획을 하나씩 그린 것처럼 입력 (주먹은 따로 — Right Hand Fist)</summary>
        private static void DrawDesign(int spellId)
        {
            var k = Keyboard;
            if (k == null || !RuneDesigns.TryGet(spellId, out var d)) return;
            foreach (var stroke in d.Strokes)
            {
                var pts = new Vector2[stroke.Length];
                for (int i = 0; i < stroke.Length; i++) pts[i] = new Vector2(500f + stroke[i].x * 180f, 300f + stroke[i].y * 180f);
                k.SimulateStroke(pts);
            }
        }
    }
}
