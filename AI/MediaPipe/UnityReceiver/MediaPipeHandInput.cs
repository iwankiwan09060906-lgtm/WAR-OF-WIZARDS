// War of Wizards — MediaPipe 손 추적기 수신기 (6단계 AI 손동작 인식)
//
// AI/MediaPipe/wow_hand_tracker.py 가 UDP(JSON)로 보내는 손 이벤트를 받아
// 계약서 IPlayerInput(§6.6) 이벤트로 바꿔 준다. KeyboardInput 대신 이 컴포넌트를 쓰면
// 시전 상태 머신(§6.2)은 그대로 둔 채 웹캠 손동작으로 플레이할 수 있다.
//
// 넣는 곳(예): Assets/_Project/Input/MediaPipe/MediaPipeHandInput.cs
// 사용법   : 빈 GameObject 에 붙이고, 상태 머신이 IPlayerInput 으로 참조한다.
//            쿨타임이 바뀔 때마다 SendCandidates(사용 가능한 룬 코드들)를 호출하면
//            추적기가 그 룬들 중에서만 인식한다(§9 "사용 가능한 슬롯만 후보").
// 메시지 형식: Docs/MEDIAPIPE_HAND_INPUT.md

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Input
{
    public class MediaPipeHandInput : MonoBehaviour, IPlayerInput
    {
        [Header("UDP")]
        [SerializeField] private int listenPort = 5065;
        [SerializeField] private string trackerHost = "127.0.0.1";
        [SerializeField] private int trackerPort = 5066;

        [Tooltip("이 시간(초) 동안 추적기 메시지가 없으면 양손 추적이 끊긴 것으로 본다")]
        [SerializeField] private float trackerTimeout = 1.0f;

        [SerializeField] private bool logEvents = true;

        // ── IPlayerInput (계약서) ───────────────────────────────────────────
        public event Action OnPinchStarted;
        public event Action<Vector2[]> OnPinchEnded;
        public event Action OnFistDetected;
        public event Action OnPointDetected;
        public event Action OnPalmOpenDetected;
        public event Action<sbyte> OnMoveGesture;

        public bool IsRightHandTracked { get; private set; }
        public bool IsLeftHandTracked { get; private set; }

        // ── 계약서 밖 추가 정보 (조준 · HUD 표시용) ───────────────────────────
        /// <summary>pinch_end 와 함께 추적기가 인식한 룬. OnPinchEnded 보다 먼저 호출된다.</summary>
        public event Action<RuneResult> OnRuneRecognized;
        public RuneResult LastRune { get; private set; }
        public Column AimColumn { get; private set; } = Column.Center;
        public float AimDepth { get; private set; } = 0.5f;
        /// <summary>오른손 핀치 지점(그리는 중) 또는 검지 끝. 0~1, y 위쪽 +</summary>
        public Vector2 RightCursor { get; private set; }
        public bool IsDrawing { get; private set; }
        public string RightPose { get; private set; } = "none";
        public string LeftPose { get; private set; } = "none";
        /// <summary>21점 (x 0~1 오른쪽, y 0~1 위쪽, z 상대 깊이). 손이 안 보이면 이전 값 유지</summary>
        public Vector3[] RightLandmarks { get; } = new Vector3[21];
        public Vector3[] LeftLandmarks { get; } = new Vector3[21];
        public bool IsTrackerConnected => Time.unscaledTime - lastMessageTime < trackerTimeout;

        private UdpClient receiver;
        private UdpClient sender;
        private Thread thread;
        private volatile bool running;
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private float lastMessageTime = -999f;
        private float nextHello;
        private string candidatesJson;

        private void OnEnable()
        {
            try
            {
                receiver = new UdpClient(listenPort);
                sender = new UdpClient();
            }
            catch (SocketException e)
            {
                Debug.LogError($"[MediaPipe] UDP {listenPort} 포트를 열 수 없습니다 (이미 사용 중?): {e.Message}");
                enabled = false;
                return;
            }
            running = true;
            thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "MediaPipeUdp" };
            thread.Start();
        }

        private void OnDisable()
        {
            running = false;
            receiver?.Close();
            sender?.Close();
            receiver = null;
            sender = null;
            IsRightHandTracked = IsLeftHandTracked = false;
        }

        private void ReceiveLoop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    byte[] data = receiver.Receive(ref any);
                    inbox.Enqueue(Encoding.UTF8.GetString(data));
                }
                catch (SocketException)
                {
                    if (!running) break;     // 닫힘 → 종료, 그 외(연결 재설정 등)는 무시하고 계속
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
            }
        }

        private void Update()
        {
            while (inbox.TryDequeue(out string json))
            {
                lastMessageTime = Time.unscaledTime;
                Handle(json);
            }

            if (!IsTrackerConnected && (IsRightHandTracked || IsLeftHandTracked))
            {
                IsRightHandTracked = IsLeftHandTracked = false;   // 추적기가 꺼짐 → 손 추적 끊김(§6.4 즉시 취소)
                IsDrawing = false;
            }

            if (Time.unscaledTime >= nextHello)          // 추적기 미리보기 창에 "Unity 연결됨" 표시 + 후보 재전송
            {
                nextHello = Time.unscaledTime + 2f;
                Send(candidatesJson ?? "{\"type\":\"hello\"}");
            }
        }

        /// <summary>쿨타임이 끝난(Ready) 슬롯의 룬 코드 목록을 추적기에 알린다. 예: "S03", "H02"</summary>
        public void SendCandidates(IEnumerable<string> spellCodes)
        {
            var sb = new StringBuilder("{\"type\":\"candidates\",\"spells\":[");
            bool first = true;
            foreach (string code in spellCodes)
            {
                if (!first) sb.Append(',');
                sb.Append('"').Append(code).Append('"');
                first = false;
            }
            candidatesJson = sb.Append("]}").ToString();
            Send(candidatesJson);
        }

        /// <summary>SpellCastRequest.SpellId(1~13, 101~106) → 룬 코드("S01"~"H06")</summary>
        public static string SpellCode(int spellId) =>
            spellId >= 100 ? $"H{spellId - 100:00}" : $"S{spellId:00}";

        /// <summary>룬 코드("S03") → SpellCastRequest.SpellId(3)</summary>
        public static int SpellNumber(string code) =>
            string.IsNullOrEmpty(code) ? 0 : int.Parse(code.Substring(1)) + (code[0] == 'H' ? 100 : 0);

        private void Send(string json)
        {
            if (sender == null) return;
            try
            {
                byte[] data = Encoding.UTF8.GetBytes(json);
                sender.Send(data, data.Length, trackerHost, trackerPort);
            }
            catch (SocketException) { }   // 추적기가 아직 안 켜져 있어도 괜찮다
        }

        private void Handle(string json)
        {
            Header head = JsonUtility.FromJson<Header>(json);
            if (head == null) return;
            if (head.type == "frame") HandleFrame(JsonUtility.FromJson<FrameMsg>(json));
            else if (head.type == "event") HandleEvent(JsonUtility.FromJson<EventMsg>(json));
        }

        private void HandleFrame(FrameMsg f)
        {
            if (f.right != null)
            {
                IsRightHandTracked = f.right.tracked;
                RightPose = f.right.pose;
                IsDrawing = f.right.drawing;
                if (f.right.cursor != null && f.right.cursor.Length >= 2) RightCursor = new Vector2(f.right.cursor[0], f.right.cursor[1]);
                if (f.right.pose == "point")
                {
                    AimColumn = (Column)Mathf.Clamp(f.right.column, 0, 2);
                    AimDepth = f.right.depth;
                }
                CopyLandmarks(f.right.landmarks, RightLandmarks);
            }
            if (f.left != null)
            {
                IsLeftHandTracked = f.left.tracked;
                LeftPose = f.left.pose;
                CopyLandmarks(f.left.landmarks, LeftLandmarks);
            }
        }

        private void HandleEvent(EventMsg e)
        {
            bool right = e.hand == "right";
            if (logEvents) Debug.Log($"[MediaPipe] {e.hand} {e.name}" + (e.name == "pinch_end" && e.rune != null ? $" → {e.rune.spellId} ({e.rune.score:0.00}) {(e.rune.recognized ? "성공" : e.rune.reason)}" : ""));

            switch (e.name)
            {
                case "tracking":
                    if (right) IsRightHandTracked = e.tracked; else IsLeftHandTracked = e.tracked;
                    break;
                case "pinch_start":
                    if (right) { IsDrawing = true; OnPinchStarted?.Invoke(); }
                    break;
                case "pinch_end":
                    if (!right) break;
                    IsDrawing = false;
                    LastRune = e.rune;
                    if (e.rune != null) OnRuneRecognized?.Invoke(e.rune);
                    var points = new Vector2[e.points?.Length ?? 0];
                    for (int i = 0; i < points.Length; i++) points[i] = new Vector2(e.points[i].x, e.points[i].y);
                    OnPinchEnded?.Invoke(points);
                    break;
                case "pinch_cancel":
                    IsDrawing = false;   // 손이 사라져 그리기 취소 — 곧 tracking(false) 도 온다
                    break;
                case "fist":
                    if (right) OnFistDetected?.Invoke();
                    break;
                case "point":
                    if (!right) break;
                    AimColumn = (Column)Mathf.Clamp(e.column, 0, 2);
                    AimDepth = e.depth;
                    OnPointDetected?.Invoke();
                    break;
                case "palm_open":
                    if (right) OnPalmOpenDetected?.Invoke();
                    break;
                case "move":
                    if (!right) OnMoveGesture?.Invoke((sbyte)Math.Sign(e.direction));
                    break;
            }
        }

        private static void CopyLandmarks(float[] flat, Vector3[] dst)
        {
            if (flat == null || flat.Length < 63) return;
            for (int i = 0; i < 21; i++) dst[i] = new Vector3(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);
        }

        // ── JSON (JsonUtility 용) ──────────────────────────────────────────
#pragma warning disable 0649   // JsonUtility 가 리플렉션으로 채우는 필드
        [Serializable] private class Header { public string type; }

        [Serializable]
        private class HandDto
        {
            public bool tracked;
            public string pose;
            public bool drawing;
            public float[] cursor;
            public int column;
            public float depth;
            public float pinch;
            public float[] landmarks;
            public float[] palm;
        }

        [Serializable]
        private class FrameMsg
        {
            public int seq;
            public float t;
            public float fps;
            public HandDto right;
            public HandDto left;
        }

        [Serializable] private class PointDto { public float x; public float y; }

        [Serializable] public class RankDto { public string spellId; public float score; }

        [Serializable]
        public class RuneResult
        {
            public string spellId;       // "S03" (실패 시 가장 비슷한 룬 또는 null)
            public int spellNumber;      // SpellCastRequest.SpellId 값 (3, 102 ...)
            public string name;
            public float score;          // 0~1
            public float gap;            // 1·2순위 점수 차
            public bool recognized;      // 점수 · 점수 차 기준을 넘었는지
            public string reason;        // ok / low_score / ambiguous / too_short / no_candidates
            public RankDto[] ranking;    // 상위 5개
        }

        [Serializable]
        private class EventMsg
        {
            public string hand;
            public string name;
            public float t;
            public int column;
            public float depth;
            public int direction;
            public bool tracked;
            public bool tooShort;
            public PointDto[] points;
            public RuneResult rune;
            public string reason;
        }
#pragma warning restore 0649
    }
}
