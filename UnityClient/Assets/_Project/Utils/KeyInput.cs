// 키보드 입력 공용 래퍼 (Input System 기준).
// 에디터 대체 입력(§6.6) · 봇 수동 조종(§20.3) · 서버 테스트 시나리오에서 사용한다.
// Keyboard.current가 없는 기기(Quest)에서는 항상 false를 반환한다.

using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Utils
{
    public static class KeyInput
    {
        public static bool Down(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        public static bool Held(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        public static bool AltHeld()
        {
            var kb = Keyboard.current;
            return kb != null && (kb.leftAltKey.isPressed || kb.rightAltKey.isPressed);
        }

        public static bool MouseHeld(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            switch (button)
            {
                case 0: return m.leftButton.isPressed;
                case 1: return m.rightButton.isPressed;
                default: return m.middleButton.isPressed;
            }
        }

        public static bool MouseDown(int button)
        {
            var m = Mouse.current;
            if (m == null) return false;
            switch (button)
            {
                case 0: return m.leftButton.wasPressedThisFrame;
                case 1: return m.rightButton.wasPressedThisFrame;
                default: return m.middleButton.wasPressedThisFrame;
            }
        }

        public static Vector2 MousePosition()
        {
            var m = Mouse.current;
            return m != null ? m.position.ReadValue() : Vector2.zero;
        }

        public static float MouseScroll()
        {
            var m = Mouse.current;
            return m != null ? m.scroll.ReadValue().y : 0f;
        }

        /// <summary>숫자 키 1~8 → 슬롯 0~7, 없으면 -1</summary>
        public static int SlotKeyDown()
        {
            if (Down(Key.Digit1)) return 0;
            if (Down(Key.Digit2)) return 1;
            if (Down(Key.Digit3)) return 2;
            if (Down(Key.Digit4)) return 3;
            if (Down(Key.Digit5)) return 4;
            if (Down(Key.Digit6)) return 5;
            if (Down(Key.Digit7)) return 6;
            if (Down(Key.Digit8)) return 7;
            return -1;
        }
    }
}
