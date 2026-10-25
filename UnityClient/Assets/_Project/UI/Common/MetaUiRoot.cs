// 메타 UI(C01~C13) 뿌리 — 캔버스 · 머리글 · 화면 이동 · 알림(토스트) · 확인 창 · VR 화면 키보드.
//   · 데스크톱(키보드 · 마우스 모드): 화면 전체를 덮는 오버레이 캔버스, 마우스로 조작 (StandaloneInputModule)
//   · VR(핸드트래킹): 눈앞 2.2m에 뜨는 월드 캔버스, 오른손 레이 + 핀치로 조작 (VrRayInputModule). 화면을 바꿀 때마다 정면으로 다시 세운다.
//   · 머리글: 왼쪽 "WAR OF WIZARDS" 글자 로고(누르면 로비) · 화면 이름, 오른쪽 화면별 버튼 + "로비" (웹 headerHTML 과 같은 구성)
//   · 전투 중에는 캔버스와 VR 레이를 꺼서 핀치가 룬 그리기로만 쓰이게 한다 (SetVisible).

using System;
using System.Collections.Generic;
using SpellboundVR.XR;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class MetaUiRoot : MonoBehaviour
    {
        private struct ToastItem
        {
            public GameObject Go;
            public float Until;
        }

        public static MetaUiRoot Instance { get; private set; }

        [Tooltip("VR 캔버스 거리 (m)")]
        public float vrDistance = 2.2f;
        [Tooltip("VR 캔버스 1px = 이 값(m) — 0.001 이면 폭 1.92m")]
        public float vrPixelSize = 0.001f;

        private bool _vr;
        private Transform _head;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private RectTransform _frame, _header, _headerRight, _body, _toasts, _modal;
        private Text _title;
        private MagicBackdrop _backdrop;
        private RectTransform _dot;
        private Image _dotGlow, _dotCore;

        public MagicBackdrop Backdrop => _backdrop;
        private Button _lobbyButton;
        private VrRayInputModule _vrModule;
        private VirtualKeyboard _keyboard;
        private readonly Dictionary<MetaScreenId, MetaScreen> _screens = new Dictionary<MetaScreenId, MetaScreen>();
        private readonly List<ToastItem> _toastItems = new List<ToastItem>();
        private MetaScreen _current;
        private float _presenceTimer;
        private float _settleUntil;
        private float _offAxisTime;

        [Tooltip("VR: 판이 시선에서 이 각도 이상 벗어난 채로 followDelay 초가 지나면 정면으로 다시 세운다")]
        public float followAngle = 55f;
        public float followDelay = 1.5f;

        public MetaScreenId CurrentId { get; private set; }
        public bool IsVr => _vr;
        public bool Visible => _canvas != null && _canvas.enabled;
        public RectTransform HeaderRight => _headerRight;

        public event Action<MetaScreenId> OnNavigated;

        public void Initialize(bool vr, Transform head, Transform rightHandAnchor)
        {
            Instance = this;
            _vr = vr;
            _head = head;
            Debug.Log($"[MetaUiRoot] 메뉴 모드: {(vr ? "VR (월드 캔버스 + 손 레이)" : "데스크톱 (화면 오버레이 + 마우스)")}, 오른손 앵커={(rightHandAnchor != null ? rightHandAnchor.name : "없음")}");
            BuildEventSystem(rightHandAnchor);
            BuildCanvas();
            _settleUntil = Time.unscaledTime + 3f; // 시작 직후에는 머리 추적이 아직 안정되지 않았다 — 그동안 시선을 따라간다
            _keyboard = new VirtualKeyboard(_modal);
            ApplyUiScale();
            MetaState.OnChanged += HandleState;
        }

        private void OnDestroy()
        {
            MetaState.OnChanged -= HandleState;
            if (_current != null) _current.Close();
            if (Instance == this) Instance = null;
        }

        // ── 준비 ─────────────────────────────────────────────

        private void BuildEventSystem(Transform rightHandAnchor)
        {
            var existing = FindAnyObjectByType<EventSystem>();
            GameObject go = existing != null ? existing.gameObject : new GameObject("[EventSystem]", typeof(EventSystem));
            if (_vr)
            {
                foreach (var m in go.GetComponents<BaseInputModule>()) if (!(m is VrRayInputModule)) m.enabled = false;
                if (!go.TryGetComponent(out _vrModule)) _vrModule = go.AddComponent<VrRayInputModule>();
                _vrModule.rightHandAnchor = rightHandAnchor;
                _vrModule.hand = rightHandAnchor != null ? HandJointProvider.EnsureForAnchor(rightHandAnchor, true) : null;
            }
            else if (go.GetComponent<BaseInputModule>() == null)
            {
                go.AddComponent<StandaloneInputModule>();
            }
        }

        private void BuildCanvas()
        {
            var root = new GameObject("[MetaUI]", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            root.layer = 5; // UI
            _canvas = root.AddComponent<Canvas>();
            _scaler = root.AddComponent<CanvasScaler>();
            root.AddComponent<GraphicRaycaster>();
            var rt = (RectTransform)root.transform;
            if (_vr)
            {
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = _vrModule != null ? _vrModule.EventCamera : null;
                rt.sizeDelta = new Vector2(UiTheme.CanvasWidth, UiTheme.CanvasHeight);
                _scaler.dynamicPixelsPerUnit = 3f;
                Recenter();
            }
            else
            {
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = new Vector2(UiTheme.CanvasWidth, UiTheme.CanvasHeight);
                _scaler.matchWidthOrHeight = 0.5f;
            }

            // 바탕: 프로토타입 공통 배경 (보라 빛 번짐 + 도는 마법진 + 떠오르는 입자 + 모서리 ㄱ자)
            _backdrop = MagicBackdrop.Build(rt);
            if (_vr)
            {
                var edge = UiKit.Panel(rt, Color.white, null, "VrEdge");
                edge.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0f), UiTheme.Alpha(UiTheme.Purple, 0.5f), 22, 3);
                edge.type = Image.Type.Sliced;
                UiKit.Stretch(edge.rectTransform);
            }

            _frame = UiKit.Stretch(UiKit.Node(rt, "Frame"));

            // 머리글
            _header = UiKit.Node(_frame, "Header");
            UiKit.Anchor(_header, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -96f), Vector2.zero);
            _header.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);
            var line = UiKit.Panel(_header, UiTheme.Alpha(UiTheme.Purple, 0.3f), null, "Line");
            UiKit.Anchor((RectTransform)line.transform, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 2f));
            // 프로토타입 머리글: 위에 작은 "WAR OF WIZARDS"(누르면 로비), 아래에 큰 화면 이름
            var left = UiKit.VBox(_header, 0, 0, TextAnchor.MiddleLeft, "Left");
            UiKit.Anchor(left, Vector2.zero, new Vector2(0.6f, 1f), new Vector2(48f, 4f), new Vector2(0f, -4f));
            left.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            var word = UiKit.Button(left, "", BtnStyle.Text, () => { if (MetaState.LoggedIn) Go(MetaScreenId.Lobby); }, 360, 28);
            UiKit.Clear(word.transform);
            var wl = UiKit.Kicker(word.transform, "War of Wizards", UiTheme.Alpha(UiTheme.PurpleLight, 0.8f), TextAnchor.MiddleLeft, 15);
            UiKit.Stretch(wl.rectTransform);
            _title = UiKit.Label(left, "", UiTheme.FontHeadline - 4, UiTheme.Text, true);
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            var tglow = _title.gameObject.AddComponent<Outline>();
            tglow.effectColor = UiTheme.Alpha(UiTheme.Purple, 0.35f);
            tglow.effectDistance = new Vector2(2f, 2f);
            var right = UiKit.HBox(_header, 12, 0, TextAnchor.MiddleRight, "Right");
            UiKit.Anchor(right, new Vector2(0.35f, 0f), Vector2.one, Vector2.zero, new Vector2(-48f, 0f));
            right.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            _headerRight = UiKit.HBox(right, 12, 0, TextAnchor.MiddleRight, "Custom");
            _lobbyButton = UiKit.Button(right, "← 로비", BtnStyle.Ghost, () => Go(MetaScreenId.Lobby), -1, 50, UiTheme.FontSmall);

            // 본문
            _body = UiKit.Node(_frame, "Body");

            _toasts = UiKit.VBox(rt, 10, 0, TextAnchor.UpperRight, "Toasts");
            UiKit.Anchor(_toasts, new Vector2(0.62f, 0.5f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(-36f, -110f));
            _toasts.GetComponent<VerticalLayoutGroup>().childForceExpandWidth = false;
            _toasts.GetComponent<VerticalLayoutGroup>().childControlWidth = true;

            _modal = UiKit.Stretch(UiKit.Node(rt, "Modal"));
        }

        public Vector3 CanvasPosition => _canvas != null ? _canvas.transform.position : Vector3.zero;

        /// <summary>레이가 메뉴 판 영역 안에 닿는지 (판 평면과 직접 교차)</summary>
        public bool RayHitsCanvas(Ray ray) => TryPlaneHit(ray, out _);

        private bool TryPlaneHit(Ray ray, out Vector2 local)
        {
            local = default;
            if (_canvas == null) return false;
            var plane = new Plane(_canvas.transform.forward, _canvas.transform.position);
            if (!plane.Raycast(ray, out float d) || d <= 0f) return false;
            Vector3 l = _canvas.transform.InverseTransformPoint(ray.GetPoint(d));
            var half = ((RectTransform)_canvas.transform).sizeDelta * 0.5f;
            if (Mathf.Abs(l.x) > half.x || Mathf.Abs(l.y) > half.y) return false;
            local = new Vector2(l.x, l.y);
            return true;
        }

        /// <summary>
        /// VR 레이가 메뉴 판에 닿은 자리에 점을 그린다. 선(LineRenderer)과 별개로 메뉴 판과 같은 UGUI라서 판이 보이면 이 점도 반드시 보인다.
        /// 레이를 판 평면과 직접 교차시켜 위치를 구한다 (UGUI 레이캐스트와 무관).
        /// </summary>
        public void ShowPointerDot(Ray ray, bool show, bool pressed)
        {
            if (!_vr || _canvas == null) return;
            if (_dot == null)
            {
                // 작은 점: 부드러운 빛 번짐 + 하얀 알맹이 (눌리면 작아지며 보라로)
                _dot = UiKit.Node((RectTransform)_canvas.transform, "PointerDot");
                _dot.anchorMin = _dot.anchorMax = new Vector2(0.5f, 0.5f);
                _dot.pivot = new Vector2(0.5f, 0.5f);
                _dotGlow = UiKit.Panel(_dot, UiTheme.Alpha(UiTheme.CyanLight, 0.35f), null, "Glow");
                _dotGlow.sprite = UiTheme.Glow();
                _dotGlow.type = Image.Type.Simple;
                _dotGlow.raycastTarget = false;
                UiKit.Stretch(_dotGlow.rectTransform);
                _dotCore = UiKit.Panel(_dot, Color.white, null, "Core");
                _dotCore.sprite = UiTheme.Circle();
                _dotCore.type = Image.Type.Simple;
                _dotCore.raycastTarget = false;
                _dotCore.rectTransform.anchorMin = _dotCore.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                _dot.SetAsLastSibling();
            }
            bool on = false;
            if (show && Visible && TryPlaneHit(ray, out var local))
            {
                _dot.anchoredPosition = local;
                float k = Mathf.Lerp(0.5f, 1.8f, MetaState.Settings.pointerSize / 100f); // 설정: 포인터 크기
                float core = (pressed ? 9f : 13f) * k;
                _dot.sizeDelta = new Vector2(core * 3.2f, core * 3.2f);
                _dotCore.rectTransform.sizeDelta = new Vector2(core, core);
                _dotCore.color = pressed ? UiTheme.PurplePale : Color.white;
                _dotGlow.color = UiTheme.Alpha(pressed ? UiTheme.Purple : UiTheme.CyanLight, pressed ? 0.6f : 0.35f);
                _dot.SetAsLastSibling();
                on = true;
            }
            if (_dot.gameObject.activeSelf != on) _dot.gameObject.SetActive(on);
        }

        private void ApplyUiScale()
        {
            float k = MetaState.Settings.uiLarge ? 1.12f : 1f;
            if (_vr) _canvas.transform.localScale = Vector3.one * vrPixelSize * k;
            else _scaler.referenceResolution = new Vector2(UiTheme.CanvasWidth, UiTheme.CanvasHeight) / k;
        }

        /// <summary>VR: 캔버스를 지금 바라보는 방향 정면에 세운다</summary>
        public void Recenter()
        {
            if (!_vr || _head == null || _canvas == null) return;
            Vector3 fwd = _head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 pos = _head.position + fwd * vrDistance + Vector3.down * 0.15f;
            _canvas.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        }

        // ── 화면 ─────────────────────────────────────────────

        public void Register(MetaScreenId id, MetaScreen screen)
        {
            screen.Attach(this);
            _screens[id] = screen;
        }

        private bool _leaveConfirmed;

        public void Go(MetaScreenId id)
        {
            if (!_screens.TryGetValue(id, out var next)) return;
            if (_current != null && _current != next && _current.IsOpen && !_leaveConfirmed &&
                !_current.CanLeave(() => { _leaveConfirmed = true; Go(id); _leaveConfirmed = false; }))
                return;
            if (_current != null) _current.Close();
            if (_keyboard.IsOpen) _keyboard.Close();
            CloseModal();
            CurrentId = id;
            _current = next;
            bool header = next.Title != null;
            _header.gameObject.SetActive(header);
            _title.text = next.Title ?? "";
            _lobbyButton.gameObject.SetActive(next.ShowBackToLobby && MetaState.LoggedIn);
            UiKit.Stretch(_body, 48, header ? 120 : 30, 48, 30);
            UiKit.Clear(_body);
            ClearHeaderRight();
            SetVisible(true);
            RecenterIfOff();
            if (_backdrop != null)
            {
                _backdrop.gameObject.SetActive(!next.Overlay);
                _backdrop.SetMood(next.Mood, next.CircleAlpha);
            }
            next.Open(_body);
            OnNavigated?.Invoke(id);
        }

        public T Screen<T>(MetaScreenId id) where T : MetaScreen => _screens.TryGetValue(id, out var s) ? s as T : null;

        public void ClearHeaderRight()
        {
            if (_headerRight != null) UiKit.Clear(_headerRight);
        }

        /// <summary>전투 중에는 숨긴다 (VR 레이도 꺼서 핀치를 룬 그리기에 돌려준다)</summary>
        public void SetVisible(bool visible)
        {
            if (_canvas.enabled == visible) return;
            _canvas.enabled = visible;
            if (_vrModule != null) _vrModule.enabled = visible;
            if (!visible && _keyboard.IsOpen) _keyboard.Close();
            if (!visible && _dot != null) _dot.gameObject.SetActive(false); // 경기 중에는 포인터를 완전히 숨긴다
            if (visible)
            {
                Recenter();
                _settleUntil = Time.unscaledTime + 0.6f;
            }
        }

        /// <summary>판이 시선 정면에서 많이 벗어났거나 너무 멀어졌을 때만 다시 세운다 (화면을 바꿀 때마다 판이 흔들리지 않게)</summary>
        private void RecenterIfOff()
        {
            if (!_vr || _head == null || _canvas == null) return;
            if (OffAxisAngle() > 30f || Mathf.Abs(Vector3.Distance(_head.position, _canvas.transform.position) - vrDistance) > 0.6f) Recenter();
        }

        private float OffAxisAngle()
        {
            Vector3 fwd = _head.forward;
            fwd.y = 0f;
            Vector3 to = _canvas.transform.position - _head.position;
            to.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f || to.sqrMagnitude < 1e-4f) return 0f;
            return Vector3.Angle(fwd, to);
        }

        private void UpdateVrPlacement(float dt)
        {
            if (!_vr || !Visible || _head == null) return;
            if (Time.unscaledTime < _settleUntil)
            {
                Recenter();
                return;
            }
            // 고개를 크게 돌린 채 잠시 있으면 판이 따라온다 (메뉴를 잃어버리지 않게)
            if (OffAxisAngle() > followAngle)
            {
                _offAxisTime += dt;
                if (_offAxisTime >= followDelay)
                {
                    _offAxisTime = 0f;
                    Recenter();
                }
            }
            else _offAxisTime = 0f;
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (Visible) _current?.Tick(dt);
            UpdateVrPlacement(dt);

            for (int i = _toastItems.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime < _toastItems[i].Until && _toastItems[i].Go != null) continue;
                if (_toastItems[i].Go != null) Destroy(_toastItems[i].Go);
                _toastItems.RemoveAt(i);
            }

            // VR: 입력칸을 고르면 화면 키보드
            if (_vr && Visible)
            {
                var sel = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
                var field = sel != null ? sel.GetComponent<InputField>() : null;
                if (field != null && (!_keyboard.IsOpen || _keyboard.Target != field)) _keyboard.Open(field);
            }

            // 접속 상태 · 우편은 메뉴에 있는 동안 주기적으로 새로 받는다 (웹 subscribePresence 와 같은 역할)
            if (Visible && AuthServiceLoggedIn())
            {
                _presenceTimer -= dt;
                if (_presenceTimer <= 0f)
                {
                    _presenceTimer = 15f;
                    MetaState.RefreshOnline();
                    MetaState.RefreshMails();
                }
            }
        }

        private static bool AuthServiceLoggedIn() => !MetaState.Offline && Backend.AuthService.IsLoggedIn;

        private void HandleState(string key)
        {
            if (key == "settings") ApplyUiScale();
            if (key == "user" && _lobbyButton != null) _lobbyButton.gameObject.SetActive(_current != null && _current.ShowBackToLobby && MetaState.LoggedIn);
        }

        // ── 알림 · 확인 ───────────────────────────────────────

        /// <summary>오른쪽 위 알림 (3.8초, 누르면 닫힘)</summary>
        public static void Toast(string message, Tone tone = Tone.Neutral)
        {
            if (string.IsNullOrEmpty(message)) return;
            var ui = Instance;
            if (ui == null || !ui.Visible)
            {
                Debug.Log("[MetaUI] " + message);
                return;
            }
            Color c = tone == Tone.Enemy ? UiTheme.Enemy : tone == Tone.Play ? UiTheme.PlayHi : tone == Tone.Cyan ? UiTheme.Cyan : UiTheme.Line2;
            var b = UiKit.Button(ui._toasts, message, BtnStyle.Default, null, -1, 64, UiTheme.FontSmall);
            b.GetComponent<Image>().sprite = UiTheme.Cut(UiTheme.Panel2, c, 10, 3);
            var label = b.GetComponentInChildren<Text>();
            label.color = tone == Tone.Enemy ? UiTheme.Hex(0xffd1d9) : UiTheme.Text;
            label.fontStyle = FontStyle.Normal;
            label.font = UiTheme.Regular;
            var le = b.GetComponent<LayoutElement>();
            float w = Mathf.Min(680f, label.preferredWidth + 48f);
            le.preferredWidth = le.minWidth = w;
            if (label.preferredWidth + 48f > 680f)
            {
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                le.preferredHeight = le.minHeight = 92f;
            }
            var go = b.gameObject;
            b.onClick.AddListener(() => Destroy(go));
            ui._toastItems.Add(new ToastItem { Go = go, Until = Time.unscaledTime + 3.8f });
            while (ui._toasts.childCount > 4) Destroy(ui._toasts.GetChild(0).gameObject);
        }

        /// <summary>확인 창 (취소 · 확인)</summary>
        public void Confirm(string title, string text, string okLabel, bool danger, Action<bool> result)
        {
            CloseModal();
            var shade = UiKit.Panel(_modal, new Color(0f, 0f, 0f, 0.6f), null, "Shade");
            UiKit.Stretch((RectTransform)shade.transform);
            shade.raycastTarget = true;
            var card = UiKit.Card(_modal, 32, 18, "Dialog");
            var crt = card;
            crt.anchorMin = crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = new Vector2(720f, 0f);
            UiKit.Fit(crt);
            UiKit.Label(card, title, UiTheme.FontTitle, UiTheme.Text, true);
            UiKit.Label(card, text, UiTheme.FontBody, UiTheme.Text2);
            var row = UiKit.HBox(card, 12, 0, TextAnchor.MiddleRight, "Actions");
            UiKit.Spacer(row);
            UiKit.Button(row, "취소", BtnStyle.Ghost, () => { CloseModal(); result?.Invoke(false); });
            UiKit.Button(row, okLabel, danger ? BtnStyle.Danger : BtnStyle.Play, () => { CloseModal(); result?.Invoke(true); });
        }

        private void CloseModal()
        {
            if (_modal == null) return;
            for (int i = _modal.childCount - 1; i >= 0; i--)
            {
                var c = _modal.GetChild(i);
                if (c.name == "VirtualKeyboard") continue;
                Destroy(c.gameObject);
            }
        }
    }
}
