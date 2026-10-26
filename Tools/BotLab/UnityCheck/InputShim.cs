// 컴파일 확인용 대역: Input System 패키지 · Photon Fusion (Unity 프로젝트 밖에서는 없음)
namespace Fusion { public interface INetworkStruct { } }
namespace UnityEngine.InputSystem { public enum Key { None, Space, Q, W, E, LeftArrow, RightArrow, UpArrow, DownArrow } }
namespace SpellboundVR.Utils
{
    public static class KeyInput
    {
        public static bool Down(UnityEngine.InputSystem.Key key) => false;
        public static bool AltHeld() => false;
        public static int SlotKeyDown() => -1;
    }
}
