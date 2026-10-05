// unity-mcp-server 포트(6400) 지킴이
//   대량 임포트 때 뜨는 AssetImportWorker도 패키지의 [InitializeOnLoad]를 실행해 포트를 먼저 잡아 버린다.
//   → 메인 에디터가 명령을 못 받는다 (Spellbound > Tools > Restart MCP Server 참고).
//   · 임포트 워커: 리스너를 바로 끈다 (워커는 명령을 받을 일이 없다)
//   · 메인 에디터: 리스너가 오류 상태이고 포트가 비면 5초마다 다시 연다 (포트가 막혀 있으면 조용히 대기)

using System;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace SpellboundVR.EditorTools
{
    [InitializeOnLoad]
    internal static class McpListenerGuard
    {
        private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        private const double CheckInterval = 5.0;
        private static readonly Type s_server = FindServerType();
        private static double s_nextCheck;

        static McpListenerGuard()
        {
            if (s_server == null) return;
            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                RuntimeHelpers.RunClassConstructor(s_server.TypeHandle);
                s_server.GetMethod("StopTcpListener", AnyStatic)?.Invoke(null, null);
                return;
            }
            EditorApplication.update += Watch;
        }

        private static void Watch()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < s_nextCheck) return;
            s_nextCheck = now + CheckInterval;

            object status = s_server.GetProperty("Status", AnyStatic)?.GetValue(null);
            if (status == null || status.ToString() != "Error") return;
            object port = s_server.GetField("currentPort", AnyStatic)?.GetValue(null);
            if (!(port is int p) || !IsPortFree(p)) return;
            s_server.GetMethod("Restart", AnyStatic)?.Invoke(null, null);
        }

        private static bool IsPortFree(int port)
        {
            try
            {
                var probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                probe.Stop();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        private static Type FindServerType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = asm.GetType("UnityMCPServer.Core.UnityMCPServer");
                if (type != null) return type;
            }
            return null;
        }
    }
}
