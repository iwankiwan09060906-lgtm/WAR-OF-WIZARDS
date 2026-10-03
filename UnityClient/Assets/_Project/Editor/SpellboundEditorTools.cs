// 에디터 보조 도구
//   Spellbound > Tools > Restart MCP Server (6400)
//     플랫폼 전환 · 대량 임포트 뒤 AssetImportWorker가 unity-mcp-server 포트(6400)를 먼저 잡아
//     메인 에디터가 명령을 못 받는 경우가 있다. 워커를 정리한 뒤 이 메뉴로 메인 에디터 리스너를 다시 연다.

using System;
using UnityEditor;
using UnityEngine;

namespace SpellboundVR.EditorTools
{
    public static class SpellboundEditorTools
    {
        [MenuItem("Spellbound/Tools/Restart MCP Server (6400)", priority = 200)]
        public static void RestartMcpServer()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("UnityMCPServer.Core.UnityMCPServer");
                var restart = type?.GetMethod("Restart", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (restart == null) continue;
                restart.Invoke(null, null);
                Debug.Log("[Spellbound Tools] MCP 서버 리스너 재시작");
                return;
            }
            Debug.LogWarning("[Spellbound Tools] unity-mcp-server 패키지를 찾지 못했습니다.");
        }
    }
}
