// 손 추적기(GestureTools/MediaPipe) 프로세스 — 전투를 시작하면 게임이 창 없이 켜고, 끝나면 끈다.
//   처음 한 번은 가상환경을 만들고 패키지를 받느라 1~3분 걸린다 (WebcamHandPanel 이 안내한다).
//   게임이 비정상으로 꺼져도 추적기는 --exit-idle 시간 뒤 스스로 꺼져 카메라를 놓는다.

using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Input
{
    public static class WebcamTracker
    {
        private const string Script = "GestureTools/MediaPipe/run_hand_tracker.bat";
        private const float ExitIdleSeconds = 30f;

        private static Process s_process;

        /// <summary>이 PC 에 손 추적기가 있는지 (Quest · 도구 없이 복사한 빌드는 false)</summary>
        public static bool Available => LocalTools.FindFile(Script) != null;

        public static bool IsRunning => s_process != null && !s_process.HasExited;

        /// <summary>가상환경을 아직 만들지 않았다 — 첫 실행 설치 중</summary>
        public static bool IsFirstRun
        {
            get
            {
                string bat = LocalTools.FindFile(Script);
                return bat != null && !File.Exists(Path.Combine(Path.GetDirectoryName(bat), ".venv", "installed.ok"));
            }
        }

        public static string LogFile => Path.Combine(Application.temporaryCachePath, "hand_tracker.log");

        public static void EnsureRunning(bool drawRunes, int holdFrames)
        {
            if (IsRunning) return;
            string bat = LocalTools.FindFile(Script);
            if (bat == null) return;
            string args = "--no-preview --exit-idle " + ExitIdleSeconds.ToString("0") + (drawRunes ? " --input rune" : " --hold-frames " + holdFrames);
            s_process = LocalTools.RunHidden(bat, args, LogFile);
        }

        /// <summary>추적기에 꺼지라고 알린다 (카메라를 바로 놓는다)</summary>
        public static void Quit(string host, int port)
        {
            s_process = null;
            try
            {
                using (var socket = new UdpClient())
                {
                    byte[] data = Encoding.UTF8.GetBytes("{\"type\":\"quit\"}");
                    socket.Send(data, data.Length, host, port);
                }
            }
            catch (SocketException) { } // 이미 꺼졌으면 할 일 없음
        }
    }
}
