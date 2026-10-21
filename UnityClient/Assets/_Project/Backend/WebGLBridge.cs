// WebGL 빌드에서 브라우저 기능 사용 (Backend/Plugins/WebGL/WowBridge.jslib).
// - 페이지 주소(Origin) → 같은 서버의 API 주소로 사용
// - localStorage → 웹 UI 에서 로그인한 토큰 공유 (BackendClient.WebTokenKey)
// - 페이지로 이벤트 보내기 → 웹 전투 화면이 window.addEventListener("wow:match-end", ...) 로 받는다 (Docs/Backend/WebUiServer.md §4-4)
// 에디터 / PC / Quest 에서는 아무 일도 하지 않는다.

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace SpellboundVR.Backend
{
    public static class WebGLBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] private static extern string WowBridge_GetPageOrigin();
        [DllImport("__Internal")] private static extern string WowBridge_GetLocalStorage(string key);
        [DllImport("__Internal")] private static extern void WowBridge_SetLocalStorage(string key, string value);
        [DllImport("__Internal")] private static extern void WowBridge_RemoveLocalStorage(string key);
        [DllImport("__Internal")] private static extern void WowBridge_SendToPage(string eventName, string json);

        public static bool IsWebGL => true;
        public static string GetPageOrigin() => WowBridge_GetPageOrigin();
        public static string GetLocalStorage(string key) => WowBridge_GetLocalStorage(key);
        public static void SetLocalStorage(string key, string value) => WowBridge_SetLocalStorage(key, value);
        public static void RemoveLocalStorage(string key) => WowBridge_RemoveLocalStorage(key);
        public static void SendToPage(string eventName, string json) => WowBridge_SendToPage(eventName, json);
#else
        public static bool IsWebGL => false;
        public static string GetPageOrigin() => null;
        public static string GetLocalStorage(string key) => null;
        public static void SetLocalStorage(string key, string value) { }
        public static void RemoveLocalStorage(string key) { }
        public static void SendToPage(string eventName, string json) { }
#endif
    }
}
