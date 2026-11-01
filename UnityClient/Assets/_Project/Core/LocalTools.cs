// PC 에서 저장소 안의 도구(손 추적기 · 서버)를 게임이 직접 켠다 — 에디터 · Windows 빌드 전용. Quest 에서는 아무것도 하지 않는다.

using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SpellboundVR.Core
{
    public static class LocalTools
    {
        /// <summary>게임 폴더부터 위로 올라가며 저장소 기준 경로의 파일을 찾는다 (없으면 null)</summary>
        public static string FindFile(string relativePath)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            var dir = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string path = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(path)) return path;
            }
#endif
            return null;
        }

        /// <summary>배치 파일을 창 없이 실행한다 — 출력은 logFile 로 (읽지 않는 파이프가 차서 멈추지 않게)</summary>
        public static Process RunHidden(string bat, string args, string logFile)
        {
            return Run("/c \"\"" + bat + "\" " + args + " > \"" + logFile + "\" 2>&1\"", Path.GetDirectoryName(bat));
        }

        /// <summary>배치 파일을 최소화된 새 창으로 띄운다 — 게임을 꺼도 그 창은 계속 돈다</summary>
        public static void RunMinimized(string title, string bat)
        {
            Run("/c start \"" + title + "\" /min \"" + bat + "\"", Path.GetDirectoryName(bat));
        }

        private static Process Run(string cmdArgs, string workingDirectory)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                var info = new ProcessStartInfo("cmd.exe", cmdArgs)
                {
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                info.EnvironmentVariables["WOW_NO_PAUSE"] = "1"; // 창이 없으니 pause 에서 영원히 멈추지 않게
                return Process.Start(info);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning("[LocalTools] 실행 실패: " + e.Message);
            }
#endif
            return null;
        }
    }
}
