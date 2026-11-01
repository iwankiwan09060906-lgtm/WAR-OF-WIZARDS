// 이 PC 의 서버가 꺼져 있으면 게임이 켠다 — PC 에서 로그인 화면이 서버를 못 찾을 때 StartServer.bat 을 최소화된 창으로 실행한다.
// 서버 주소가 이 PC(localhost)일 때만, 앱을 켠 뒤 한 번만 시도한다. Quest 는 서버 PC 에서 켜 둬야 한다 (ServerAutostart.bat).

using SpellboundVR.Core;

namespace SpellboundVR.Backend
{
    public static class LocalServer
    {
        private const string Starter = "StartServer.bat";

        private static bool s_tried;

        /// <summary>서버를 켜기 시작했으면 true</summary>
        public static bool TryStart(string serverUrl)
        {
            if (s_tried || string.IsNullOrEmpty(serverUrl)) return false;
            if (!serverUrl.Contains("localhost") && !serverUrl.Contains("127.0.0.1")) return false;
            string bat = LocalTools.FindFile(Starter);
            if (bat == null) return false;
            s_tried = true;
            LocalTools.RunMinimized("War of Wizards - 서버", bat);
            return true;
        }
    }
}
