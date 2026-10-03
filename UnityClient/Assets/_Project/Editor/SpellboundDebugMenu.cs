// 플레이 모드 디버그 메뉴 — Game 뷰 포커스 없이도 시전 흐름 전체를 재현한다 (§37 "봇 수동 조종으로 재현").
//   Draw Rune S03/S07/S08 : 템플릿 모양을 화면에 그린 것처럼 입력 → $P+ 인식 → RuneReady
//   Fist / Point / Palm   : 오른손 자세 이벤트
//   Cast Slot 1~3         : 키보드 대체 입력(1~8)과 동일
//   Aim Left/Center/Right : 조준 화면 좌표 고정 (해제: Aim Mouse)

using SpellboundVR.Gesture;
using SpellboundVR.Input;
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

        [MenuItem("Spellbound/Debug/Draw Rune S03 Fireball (circle)", priority = 60)]
        public static void DrawFireball() => Draw(RuneTemplateLibrary.Ellipse(1f, 1f, 90f, -360f, 48));

        [MenuItem("Spellbound/Debug/Draw Rune S07 Gatling (Z)", priority = 61)]
        public static void DrawGatling() => Draw(RuneTemplateLibrary.Polyline(new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f) }, 16));

        [MenuItem("Spellbound/Debug/Draw Rune S08 Shield (triangle)", priority = 62)]
        public static void DrawShield() => Draw(RuneTemplateLibrary.Polyline(new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0f), new Vector2(0f, 1f) }, 16));

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

        private static void Draw(Vector2[] shape)
        {
            var k = Keyboard;
            if (k == null) return;
            var pts = new Vector2[shape.Length];
            for (int i = 0; i < shape.Length; i++) pts[i] = new Vector2(500f + shape[i].x * 180f, 300f + shape[i].y * 180f);
            k.SimulateStroke(pts);
        }
    }
}
