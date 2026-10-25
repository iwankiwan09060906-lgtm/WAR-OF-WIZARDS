// 웹 백엔드(http://PC-IP:8080)는 https 가 아니므로 Player Settings > Other > "Allow downloads over HTTP" 가
// "Always allowed" 여야 Quest 에서 로그인 · 덱 · 전적이 된다. 이 값이 꺼지면 앱이 "HTTP 서버 주소로의 연결이 앱 설정에서 막혀 있습니다"를 띄운다.
// ProjectSettings.asset 파일만 고치면 열려 있는 에디터가 메모리 값(차단)으로 다시 덮어쓰므로,
// 에디터가 열릴 때 · 빌드 직전에 에디터 안에서 직접 값을 맞춘다.

using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace SpellboundVR.EditorTools
{
    [InitializeOnLoad]
    public sealed class SpellboundHttpPolicy : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        static SpellboundHttpPolicy()
        {
            Enforce();
        }

        public void OnPreprocessBuild(BuildReport report) => Enforce();

        [MenuItem("Spellbound/Build/Allow HTTP Backend (fix 'HTTP blocked' error)", priority = 45)]
        public static void EnforceMenu()
        {
            Enforce();
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[Spellbound] Allow downloads over HTTP = Always allowed");
        }

        private static void Enforce()
        {
            if (PlayerSettings.insecureHttpOption == InsecureHttpOption.AlwaysAllowed) return;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log("[Spellbound] Allow downloads over HTTP 를 'Always allowed' 로 맞췄습니다 (웹 백엔드 접속용).");
        }
    }
}
