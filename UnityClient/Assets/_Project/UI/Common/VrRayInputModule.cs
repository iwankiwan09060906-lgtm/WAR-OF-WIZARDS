// VR 메타 UI 입력 — 오른손 레이로 가리키고 엄지 · 검지 핀치로 누른다 (컨트롤러면 검지 트리거 · A 버튼).
//   · 레이: OVR 시스템 포인터 포즈 (손) → 없으면 오른손 앵커 정면 (컨트롤러)
//   · UGUI GraphicRaycaster를 그대로 쓰기 위해, 보이지 않는 카메라를 레이 위치 · 방향에 두고 화면 가운데 점으로 레이캐스트한다.
//     (월드 스페이스 캔버스의 worldCamera = 이 카메라). 버튼 · 입력칸 · 스위치 · 슬라이더 · 스크롤 모두 동작한다.
//   · 드래그는 누른 뒤 레이가 1.5° 이상 움직이면 시작 (스크롤 목록 · 슬라이더)
//   · 전투가 시작되면 MetaUiRoot가 이 모듈을 끈다 → 핀치는 룬 그리기로 돌아간다.

using SpellboundVR.XR;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SpellboundVR.UI
{
    public sealed class VrRayInputModule : PointerInputModule
    {
        public Transform rightHandAnchor;
        public HandJointProvider hand;
        [Tooltip("드래그로 인정하는 레이 회전 각도")]
        public float dragAngle = 1.5f;
        public float maxRayLength = 6f;

        private readonly PinchDetector _pinch = new PinchDetector();
        private Camera _cam;
        private PointerEventData _ped;
        private bool _pressed;
        private Vector3 _pressDir;
        private LineRenderer _line;
        private Transform _cursor;
        private bool _loggedFallback;
        private RenderTexture _rt;
        private string _hitName = "-";
        private Transform _headForRay;

        public Camera EventCamera
        {
            get
            {
                EnsureCamera();
                return _cam;
            }
        }

        private void EnsureCamera()
        {
            if (_cam != null) return;
            var go = new GameObject("[UI Pointer Camera]");
            go.transform.SetParent(transform, false);
            _cam = go.AddComponent<Camera>();
            // 레이캐스트 기준 카메라. 꺼 두면 Quest(XR)에서 pixelWidth 가 0 이 되어 "화면 가운데"를 못 구하고 클릭이 전부 빗나갈 수 있다.
            // 그래서 켜 두되 16x16 렌더 텍스처에 아무것도 안 그리게 하고(cullingMask 0) 화면 크기를 고정한다.
            _rt = new RenderTexture(16, 16, 0);
            _cam.targetTexture = _rt;
            _cam.enabled = true;
            _cam.depth = -100f;
            _cam.fieldOfView = 5f;
            _cam.nearClipPlane = 0.01f;
            _cam.farClipPlane = maxRayLength + 1f;
            _cam.clearFlags = CameraClearFlags.Nothing;
            _cam.cullingMask = 0;
            _cam.stereoTargetEye = StereoTargetEyeMask.None; // XR에서 눈 투영이 섞이지 않게 — 화면 가운데 = 레이 방향
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureCamera();
            var lineGo = new GameObject("[UI Ray]", typeof(LineRenderer));
            lineGo.transform.SetParent(transform, false);
            _line = lineGo.GetComponent<LineRenderer>();
            _line.positionCount = 2;
            _line.widthMultiplier = 0.008f;
            _line.useWorldSpace = true;
            _line.sharedMaterial = RayMaterial(new Color(0.3f, 0.85f, 1f, 1f));
            _line.startColor = new Color(0.3f, 0.85f, 1f, 0.15f); // 손 쪽은 옅게, 끝은 진하게
            _line.endColor = new Color(0.3f, 0.85f, 1f, 0.8f);
            _line.numCapVertices = 4;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.widthMultiplier = 0.003f; // 선은 기본 꺼짐(설정 "레이 선 표시"), 켜도 가늘게
            ShowVisuals(false);
        }

        private bool _pinching;
        private Vector3 _dbgOrigin, _dbgDir, _dbgCanvas;
        private string _raySource = "-";
        private bool _systemHits;

        protected override void OnDestroy()
        {
            if (_rt != null) _rt.Release();
            base.OnDestroy();
        }

        protected override void OnDisable()
        {
            MetaUiRoot.Instance?.ShowPointerDot(default, false, false);
            if (_pressed) Release();
            ShowVisuals(false);
            base.OnDisable();
        }

        public override bool ShouldActivateModule() => enabled && gameObject.activeInHierarchy;

        private bool TryGetRay(out Ray ray, out bool down)
        {
            ray = default;
            down = false;
            if (hand != null && hand.IsTracked)
            {
                _pinch.Update(hand);
                down = _pinch.IsPinching;
                if (_headForRay == null && Camera.main != null) _headForRay = Camera.main.transform;
                var ui = MetaUiRoot.Instance;

                // 후보 레이를 만들어 메뉴 판에 실제로 닿는 첫 번째를 쓴다 (시스템 포인터 포즈가 엉뚱한 방향일 수 있다)
                Ray sys = default, headTip = default, knuckle = default, finger = default;
                bool hasSys = hand.TryGetSystemPointerPose(out var pose);
                if (hasSys) sys = new Ray(pose.position, pose.rotation * Vector3.forward);
                bool hasTip = hand.TryGetJoint(HandJoint.IndexTip, out var tip);
                bool hasBase = hand.TryGetJoint(HandJoint.IndexProximal, out var proximal);
                bool hasWrist = hand.TryGetJoint(HandJoint.Wrist, out var wrist);
                if (hasTip && _headForRay != null) headTip = new Ray(tip, (tip - _headForRay.position).normalized); // 눈 → 손가락 끝: 손가락으로 가리키는 것
                if (hasTip && hasBase) finger = new Ray(tip, (tip - proximal).normalized);
                if (hasWrist && hasBase) knuckle = new Ray(proximal, (proximal - wrist).normalized);

                string[] names = { "시스템", "눈→검지끝", "검지방향", "손목→검지" };
                Ray[] rays = { sys, headTip, finger, knuckle };
                bool[] has = { hasSys, hasTip && _headForRay != null, hasTip && hasBase, hasWrist && hasBase };
                int pick = -1;
                for (int i = 0; i < rays.Length && pick < 0; i++)
                    if (has[i] && ui != null && ui.RayHitsCanvas(rays[i])) pick = i;
                if (pick < 0)
                {
                    for (int i = 0; i < rays.Length && pick < 0; i++) if (has[i]) pick = i;
                }
                if (pick >= 0)
                {
                    ray = rays[pick];
                    _raySource = names[pick];
                    _systemHits = hasSys && ui != null && ui.RayHitsCanvas(sys);
                    return true;
                }
            }
            if (rightHandAnchor != null && OVRInput.IsControllerConnected(OVRInput.Controller.RTouch))
            {
                ray = new Ray(rightHandAnchor.position, rightHandAnchor.forward);
                down = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch) ||
                       OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
                return true;
            }
            return false;
        }

        private float _nextDiag;

        /// <summary>
        /// 레이 · 커서용 재질. 메뉴 판(UGUI)이 쓰는 UI/Default 는 빌드에서 반드시 포함되므로 그것을 먼저 쓴다
        /// (Sprites/Default 는 Quest 빌드에서 안 그려지는 경우가 있었다). 항상 맨 위에 그린다.
        /// </summary>
        private static Material RayMaterial(Color color)
        {
            Shader shader = Shader.Find("UI/Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
            var m = new Material(shader) { color = color, name = "VrRay_" + (shader != null ? shader.name : "none") };
            if (m.HasProperty("unity_GUIZTestMode")) m.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
            if (m.HasProperty("_ZTest")) m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            m.renderQueue = 4000;
            return m;
        }

        /// <summary>3초마다 한 줄: 레이가 안 나올 때 원인을 adb logcat -s Unity 로 구분하기 위한 진단</summary>
        private void Diagnose(bool gotRay)
        {
            if (Time.unscaledTime < _nextDiag) return;
            _nextDiag = Time.unscaledTime + 3f;
            bool tracked = hand != null && hand.IsTracked;
            Pose p = default;
            bool sys = hand != null && hand.TryGetSystemPointerPose(out p);
            Debug.Log($"[VrRayInputModule] 진단 — 선 재질={(_line != null && _line.sharedMaterial != null ? _line.sharedMaterial.name : "없음")}, 선 켜짐={(_line != null && _line.enabled)}, 모듈 켜짐, 손 추적={tracked}, 시스템 포인터 포즈={sys}, 컨트롤러={OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)}, 레이 생성={gotRay}, 레이 출처={_raySource}, 시스템 레이가 판에 닿음={_systemHits}, 레이 시작={_dbgOrigin}, 레이 방향={_dbgDir}, 판 위치={_dbgCanvas}, 가리키는 대상={_hitName}, 핀치={_pinching}, 카메라 크기={(_cam != null ? _cam.pixelWidth : -1)}");
        }

        public override void Process()
        {
            bool gotRay = TryGetRay(out var ray, out bool down);
            Diagnose(gotRay);
            if (!gotRay)
            {
                MetaUiRoot.Instance?.ShowPointerDot(default, false, false);
                if (_pressed) Release();
                ShowVisuals(false);
                return;
            }
            _dbgOrigin = ray.origin; _dbgDir = ray.direction;
            if (MetaUiRoot.Instance != null) _dbgCanvas = MetaUiRoot.Instance.CanvasPosition;
            EnsureCamera();
            if (_headForRay == null && Camera.main != null) _headForRay = Camera.main.transform;
            _cam.transform.SetPositionAndRotation(ray.origin, Quaternion.LookRotation(ray.direction, Vector3.up));
            if (_ped == null) _ped = new PointerEventData(eventSystem) { pointerId = kMouseLeftId };
            _ped.Reset();
            _ped.position = new Vector2(_cam.pixelWidth * 0.5f, _cam.pixelHeight * 0.5f); // 카메라가 레이 방향이므로 가운데 = 레이
            _ped.delta = Vector2.zero;
            _ped.scrollDelta = Vector2.zero;
            _ped.button = PointerEventData.InputButton.Left;

            eventSystem.RaycastAll(_ped, m_RaycastResultCache);
            var hit = FindFirstRaycast(m_RaycastResultCache);
            m_RaycastResultCache.Clear();
            _ped.pointerCurrentRaycast = hit;
            _hitName = hit.gameObject != null ? hit.gameObject.name : "-";
            _pinching = down;
            MetaUiRoot.Instance?.ShowPointerDot(ray, true, down);
            HandlePointerExitAndEnter(_ped, hit.gameObject);

            if (down && !_pressed) Press(hit.gameObject, ray);
            else if (!down && _pressed) Release();
            if (_pressed) Drag(ray);

            UpdateVisuals(ray, hit);
        }

        private void Press(GameObject go, Ray ray)
        {
            _pressed = true;
            _pressDir = ray.direction;
            _ped.eligibleForClick = true;
            _ped.dragging = false;
            _ped.useDragThreshold = true;
            _ped.pressPosition = _ped.position;
            _ped.pointerPressRaycast = _ped.pointerCurrentRaycast;
            DeselectIfSelectionChanged(go, _ped);
            var pressed = ExecuteEvents.ExecuteHierarchy(go, _ped, ExecuteEvents.pointerDownHandler);
            if (pressed == null) pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            _ped.pointerPress = pressed;
            _ped.rawPointerPress = go;
            _ped.clickTime = Time.unscaledTime;
            _ped.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(go);
            if (_ped.pointerDrag != null) ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.initializePotentialDrag);
        }

        private void Drag(Ray ray)
        {
            if (_ped.pointerDrag == null) return;
            if (!_ped.dragging && Vector3.Angle(_pressDir, ray.direction) >= dragAngle)
            {
                ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.beginDragHandler);
                _ped.dragging = true;
                // 드래그가 시작되면 클릭은 취소 (스크롤하다 버튼이 눌리지 않게)
                if (_ped.pointerPress != _ped.pointerDrag)
                {
                    ExecuteEvents.Execute(_ped.pointerPress, _ped, ExecuteEvents.pointerUpHandler);
                    _ped.eligibleForClick = false;
                    _ped.pointerPress = null;
                    _ped.rawPointerPress = null;
                }
            }
            if (_ped.dragging) ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.dragHandler);
        }

        private void Release()
        {
            _pressed = false;
            if (_ped == null) return;
            ExecuteEvents.Execute(_ped.pointerPress, _ped, ExecuteEvents.pointerUpHandler);
            var go = _ped.pointerCurrentRaycast.gameObject;
            var clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(go);
            if (_ped.pointerPress != null && _ped.pointerPress == clickHandler && _ped.eligibleForClick)
                ExecuteEvents.Execute(_ped.pointerPress, _ped, ExecuteEvents.pointerClickHandler);
            if (go != null && _ped.pointerDrag != null && _ped.dragging)
                ExecuteEvents.ExecuteHierarchy(go, _ped, ExecuteEvents.dropHandler);
            if (_ped.pointerDrag != null && _ped.dragging)
                ExecuteEvents.Execute(_ped.pointerDrag, _ped, ExecuteEvents.endDragHandler);
            _ped.eligibleForClick = false;
            _ped.pointerPress = null;
            _ped.rawPointerPress = null;
            _ped.dragging = false;
            _ped.pointerDrag = null;
        }

        private void UpdateVisuals(Ray ray, RaycastResult hit)
        {
            if (!MetaState.Settings.showRayLine) { ShowVisuals(false); return; }
            ShowVisuals(true);
            bool onUi = hit.gameObject != null;
            Vector3 end = onUi ? hit.worldPosition : ray.origin + ray.direction * 1.2f;
            _line.SetPosition(0, ray.origin);
            _line.SetPosition(1, end);
        }

        private void ShowVisuals(bool show)
        {
            if (_line != null && _line.enabled != show) _line.enabled = show;
        }
    }
}
