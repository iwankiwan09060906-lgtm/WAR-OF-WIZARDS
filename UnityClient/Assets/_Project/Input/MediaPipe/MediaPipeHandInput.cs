// War of Wizards — MediaPipe 손 추적기 수신기 (6단계 AI 손동작 인식)
//
// GestureTools/MediaPipe/wow_hand_tracker.py 가 UDP(JSON)로 보내는 손 이벤트를 받아
// 계약서 IPlayerInput(§6.6) 이벤트로 바꿔 준다. KeyboardInput 대신 이 컴포넌트를 쓰면
// 시전 상태 머신(§6.2)은 그대로 둔 채 웹캠 손동작으로 플레이할 수 있다.
//
// 쓰는 법  : 헤드셋 없는 PC 의 기본 입력이다 (환경설정 > "웹캠 손 입력"). GameBootstrap.CreateInput 이 키보드 대신 이 입력을 만들고,
//            전투가 시작되면 추적기를 창 없이 직접 켠다 (WebcamTracker). 내 모습은 화면 오른쪽 아래에 나온다 (WebcamHandPanel).
//            기본 입력 = 룬 그리기: 엄지 · 검지를 붙인 채 허공에 룬을 그리고 떼면 인식된다 (환경설정 "웹캠으로 룬 그리기").
//            끄면 손 모양: 오른손 모양을 유지하면 그 스킬을 고른다 (CastStateMachine.SelectSpell). 왼손 휘두르기 = 이동.
//            조준은 오른손 커서(0~1)를 화면 좌표로 본 레이다 — 마우스와 같은 경로로 열 · 깊이 · 개인전 레인이 정해진다.
//            왼손잡이 모드(환경설정)는 추적기가 두 손을 맞바꿔 보낸다 — 이 파일의 "오른손"은 언제나 스킬 손, "왼손"은 이동 손이다.
//            추적기가 없거나 카메라가 안 열리면 1~8 · A/D · 마우스 조준으로 계속 플레이할 수 있다.
// 메시지 형식: Docs/Input/MediaPipeHandInput.md

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;
using SpellboundVR.Gesture;
using SpellboundVR.Spells;
using SpellboundVR.UI;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Input
{
    public class MediaPipeHandInput : MonoBehaviour, ISpellInputSource
    {
        private const int MaxDrawPoints = 512;
        private const float DrawDistance = 0.8f;

        [Tooltip("조준 · 그리기 기준 카메라 (비우면 Camera.main)")]
        public Camera viewCamera;

        private readonly Vector3[] _drawPoints = new Vector3[MaxDrawPoints];
        private int _drawCount;
        private BattleHudView _hud;
        private CastStateMachine _cast;
        private bool _shownConnected;
        private bool _drawRunes;
        private int _holdFrames;
        private bool _bound;
        private string _handsJson;

        [Header("UDP")]
        [SerializeField] private int listenPort = 5065;
        [SerializeField] private string trackerHost = "127.0.0.1";
        [SerializeField] private int trackerPort = 5066;
        [Tooltip("내 모습(JPEG)을 받는 포트")]
        [SerializeField] private int previewPort = 5068;

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
        public event Action<int> OnDirectSlotCast;

        public Vector3[] DrawPoints => _drawPoints;
        public int DrawPointCount => _drawCount;
        /// <summary>손 궤적은 0~1 화면 비율 — 이보다 작은 획은 룬으로 치지 않는다</summary>
        public float MinStrokeSize => 0.05f;

        public void EndRune() => _drawCount = 0;

        private Camera Cam => viewCamera != null ? viewCamera : Camera.main;

        public bool TryGetAimRay(out Ray ray)
        {
            var cam = Cam;
            if (cam == null || !IsRightHandTracked)
            {
                ray = default;
                return false;
            }
            if (IsTrackerConnected) ray = cam.ViewportPointToRay(new Vector3(RightCursor.x, RightCursor.y, 0f));
            else ray = cam.ScreenPointToRay(KeyInput.MousePosition()); // 추적기가 없으면 마우스로 조준
            return true;
        }

        public bool TryGetRightHandPosition(out Vector3 position)
        {
            if (!TryGetAimRay(out var ray))
            {
                position = default;
                return false;
            }
            position = ray.origin + ray.direction * (DrawDistance / Mathf.Max(0.2f, Vector3.Dot(ray.direction, Cam.transform.forward)));
            return true;
        }

        /// <summary>전투 시작: 추적기를 켜고 덱의 룬을 후보로 보낸다. holdFrames = 손 모양을 유지할 프레임 수</summary>
        public void Bind(BattleHudView hud, EightSlotDeckManager slots, int[] deck, CastStateMachine cast, bool drawRunes, int holdFrames, RuneTemplateLibrary runes)
        {
            _hud = hud;
            _cast = cast;
            _drawRunes = drawRunes;
            _holdFrames = holdFrames;
            _bound = true;
            _handsJson = "{\"type\":\"hands\",\"swap\":" + (MetaState.Settings.leftHanded ? "true" : "false") +
                         ",\"input\":\"" + (drawRunes ? "rune" : "shape") + "\"}";
            var codes = new List<string>(deck.Length);
            for (int i = 0; i < deck.Length; i++)
                if (deck[i] > 0) codes.Add(SpellCode(deck[i]));
            SendCandidates(codes);
            SendHandedness();
            WebcamTracker.EnsureRunning(drawRunes, holdFrames, MetaState.Settings.leftHanded);
            gameObject.AddComponent<WebcamHandPanel>().Initialize(this, slots, deck, cast, drawRunes, runes);
        }

        private void TrackDrawing()
        {
            if (!IsDrawing || _drawCount >= MaxDrawPoints) return;
            if (TryGetRightHandPosition(out var p) && (_drawCount == 0 || (p - _drawPoints[_drawCount - 1]).sqrMagnitude > 1e-5f))
                _drawPoints[_drawCount++] = p;
        }

        private void TrackConnection()
        {
            if (_hud == null || _shownConnected == IsTrackerConnected) return;
            _shownConnected = IsTrackerConnected;
            _hud.ShowInputStatus(_shownConnected ? "웹캠 손 추적 연결됨" : "손 추적기를 기다리는 중…");
        }

        /// <summary>추적기가 없으면 손이 있는 것으로 친다 — 키보드 · 마우스로 시전하다 "손을 놓쳤다"고 취소되지 않게</summary>
        public bool IsRightHandTracked => _rightTracked || !IsTrackerConnected;
        public bool IsLeftHandTracked => _leftTracked || !IsTrackerConnected;

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

        /// <summary>손 모양 표: "01110" → "S03" (추적기가 보내 준다)</summary>
        public Dictionary<string, string> ShapeSpells { get; } = new Dictionary<string, string>();
        /// <summary>지금 오른손 모양 (패턴 · 그 스킬 · 고르기까지 진행률 0~1)</summary>
        public string ShapePattern { get; private set; } = "";
        public string ShapeSpell { get; private set; } = "";
        public float ShapeProgress { get; private set; }
        /// <summary>추적기가 켜졌지만 카메라를 열지 못했다</summary>
        public bool CameraFailed { get; private set; }

        /// <summary>새로 도착한 내 모습 JPEG (없으면 null, 한 번 꺼내면 비워진다)</summary>
        public byte[] TakePreview() => Interlocked.Exchange(ref preview, null);

        private bool _rightTracked;
        private bool _leftTracked;
        private UdpClient receiver;
        private UdpClient previewReceiver;
        private UdpClient sender;
        private volatile bool running;
        private byte[] preview;
        private readonly ConcurrentQueue<string> inbox = new ConcurrentQueue<string>();
        private float lastMessageTime = -999f;
        private float nextHello;
        private string candidatesJson;

        private void OnEnable()
        {
            try
            {
                receiver = new UdpClient(listenPort);
                previewReceiver = new UdpClient(previewPort);
                sender = new UdpClient();
            }
            catch (SocketException e)
            {
                Debug.LogError($"[MediaPipe] UDP {listenPort} 포트를 열 수 없습니다 (이미 사용 중?): {e.Message}");
                enabled = false;
                return;
            }
            running = true;
            StartReceiver(receiver, "MediaPipeUdp", data => inbox.Enqueue(Encoding.UTF8.GetString(data)));
            StartReceiver(previewReceiver, "MediaPipePreview", data => Interlocked.Exchange(ref preview, data)); // 항상 최신 장만
            if (_bound) WebcamTracker.EnsureRunning(_drawRunes, _holdFrames, MetaState.Settings.leftHanded); // 메뉴가 길어 추적기가 스스로 꺼졌을 수 있다
        }

        private void OnDisable()
        {
            running = false;
            receiver?.Close();
            previewReceiver?.Close();
            sender?.Close();
            receiver = null;
            previewReceiver = null;
            sender = null;
            _rightTracked = _leftTracked = false;
        }

        private void OnDestroy() => WebcamTracker.Quit(trackerHost, trackerPort);

        private void StartReceiver(UdpClient socket, string threadName, Action<byte[]> onData)
        {
            new Thread(() => ReceiveLoop(socket, onData)) { IsBackground = true, Name = threadName }.Start();
        }

        private void ReceiveLoop(UdpClient socket, Action<byte[]> onData)
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    onData(socket.Receive(ref any));
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

            if (!IsTrackerConnected && (_rightTracked || _leftTracked))
            {
                _rightTracked = _leftTracked = false;   // 추적기가 꺼짐 → 손 추적 끊김(§6.4 즉시 취소)
                IsDrawing = false;
            }
            TrackDrawing();
            TrackConnection();
            ReadKeys();

            if (Time.unscaledTime >= nextHello)          // 추적기 미리보기 창에 "Unity 연결됨" 표시 + 후보 재전송
            {
                nextHello = Time.unscaledTime + 2f;
                Send(candidatesJson ?? "{\"type\":\"hello\"}");
                SendHandedness();
            }
        }

        /// <summary>왼손잡이 모드 · 입력 방식을 알린다 — 지난 전투부터 켜져 있던 추적기에도 적용되도록 주기적으로 다시 보낸다</summary>
        private void SendHandedness()
        {
            if (_handsJson != null) Send(_handsJson);
        }

        /// <summary>손이 안 닿을 때를 위한 키 — 1~8 슬롯 · A/D 이동 (Alt 조합은 봇 수동 조종 전용)</summary>
        private void ReadKeys()
        {
            if (KeyInput.AltHeld()) return;
            int slot = KeyInput.SlotKeyDown();
            if (slot >= 0) OnDirectSlotCast?.Invoke(slot);
            if (KeyInput.Down(Key.A)) OnMoveGesture?.Invoke(-1);
            if (KeyInput.Down(Key.D)) OnMoveGesture?.Invoke(1);
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
            else if (head.type == "shapes") HandleShapes(JsonUtility.FromJson<ShapesMsg>(json));
            else if (head.type == "status") CameraFailed = JsonUtility.FromJson<StatusMsg>(json).state == "no_camera";
        }

        private void HandleShapes(ShapesMsg m)
        {
            if (m.patterns == null || m.spells == null) return;
            for (int i = 0; i < m.patterns.Length && i < m.spells.Length; i++) ShapeSpells[m.patterns[i]] = m.spells[i];
        }

        private void HandleFrame(FrameMsg f)
        {
            if (f.right != null)
            {
                _rightTracked = f.right.tracked;
                RightPose = f.right.pose;
                IsDrawing = f.right.drawing;
                bool shaped = f.right.tracked && f.right.shape != null;
                ShapePattern = shaped ? f.right.shape.pattern : "";
                ShapeSpell = shaped ? f.right.shape.spell : "";
                ShapeProgress = shaped ? f.right.shape.progress : 0f;
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
                _leftTracked = f.left.tracked;
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
                    if (right) _rightTracked = e.tracked; else _leftTracked = e.tracked;
                    break;
                case "shape":
                    if (right && _cast != null) _cast.SelectSpell(SpellNumber(e.spell));
                    break;
                case "pinch_start":
                    if (right) { IsDrawing = true; _drawCount = 0; OnPinchStarted?.Invoke(); }
                    break;
                case "pinch_end":
                    if (!right) break;
                    IsDrawing = false;
                    LastRune = e.rune;
                    if (e.rune != null) OnRuneRecognized?.Invoke(e.rune);
                    // 궤적은 화면 비율 좌표라 16:9 화면에서 가로로 찌그러져 있다 — 그린 모양 그대로 인식하도록 가로세로를 맞춘다
                    float aspect = e.aspect > 0f ? e.aspect : 1f;
                    var points = new Vector2[e.points?.Length ?? 0];
                    for (int i = 0; i < points.Length; i++) points[i] = new Vector2(e.points[i].x * aspect, e.points[i].y);
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

        [Serializable] private class ShapeDto { public string pattern; public string spell; public float progress; }
        [Serializable] private class ShapesMsg { public string[] patterns; public string[] spells; }
        [Serializable] private class StatusMsg { public string state; }

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
            public ShapeDto shape;
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
            public string spell;
            public PointDto[] points;
            public float aspect;
            public RuneResult rune;
            public string reason;
        }
#pragma warning restore 0649
    }
}
