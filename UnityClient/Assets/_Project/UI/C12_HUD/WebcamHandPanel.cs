// 웹캠 패널 — 화면 오른쪽 아래에 내 모습(손 뼈대 포함)을 작게 띄우고, 왼쪽에 내 덱의 손 모양 안내를 둔다.
//   데스크톱 전용이라 HUD(머리에 붙은 월드 캔버스)와 따로 화면 오버레이 캔버스에 그린다.
//   손 모양 그림: 다섯 칸 = 엄지 · 검지 · 중지 · 약지 · 새끼, 키가 큰 칸 = 편 손가락.

using SpellboundVR.Deck;
using SpellboundVR.Input;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class WebcamHandPanel : MonoBehaviour
    {
        private const float PreviewWidth = 320f;
        private const float PreviewHeight = 180f;
        private const float SheetWidth = 190f;
        private const float BarHeight = 30f;
        private const float RowHeight = 22f;
        private const float PreviewAspect = PreviewWidth / PreviewHeight;

        private static readonly Color Back = new Color(0.04f, 0.03f, 0.1f, 0.78f);
        private static readonly Color Dim = new Color(1f, 1f, 1f, 0.28f);

        private sealed class Row
        {
            public string Code;
            public int Slot;
            public Text Name;
            public Image[] Fingers;
            public string Pattern;
        }

        private MediaPipeHandInput _input;
        private EightSlotDeckManager _slots;
        private CastStateMachine _cast;
        private Canvas _canvas;
        private RawImage _preview;
        private Texture2D _texture;
        private Text _status;
        private Text _picking;
        private RectTransform _fill;
        private bool _drawRunes;
        private Row[] _rows;

        public void Initialize(MediaPipeHandInput input, EightSlotDeckManager slots, int[] deck, CastStateMachine cast, bool drawRunes)
        {
            _input = input;
            _slots = slots;
            _cast = cast;
            _drawRunes = drawRunes;
            Build(deck);
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_texture != null) Destroy(_texture);
        }

        private void Build(int[] deck)
        {
            var go = new GameObject("[WebcamPanel]", typeof(Canvas), typeof(CanvasScaler));
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 40;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            float width = _drawRunes ? PreviewWidth : PreviewWidth + SheetWidth;
            float height = PreviewHeight + BarHeight;
            var root = Box(go.transform, "Panel", Back);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 0f);
            root.anchoredPosition = new Vector2(-16f, 16f);
            root.sizeDelta = new Vector2(width, height);

            var image = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            image.transform.SetParent(root, false);
            _preview = image.GetComponent<RawImage>();
            _preview.color = Color.black;
            Place(_preview.rectTransform, width - PreviewWidth, BarHeight, PreviewWidth, PreviewHeight);

            _status = Label(root, "Status", UiTheme.Text, 17, TextAnchor.MiddleCenter);
            Place(_status.rectTransform, width - PreviewWidth + 16f, BarHeight, PreviewWidth - 32f, PreviewHeight);

            var track = Box(root, "Track", new Color(1f, 1f, 1f, 0.12f));
            Place(track, width - PreviewWidth, 0f, PreviewWidth, BarHeight);
            _fill = Box(track, "Fill", UiTheme.Purple);
            _fill.anchorMin = Vector2.zero;
            _fill.anchorMax = new Vector2(0f, 1f);
            _fill.pivot = new Vector2(0f, 0.5f);
            _fill.offsetMin = _fill.offsetMax = Vector2.zero;
            _picking = Label(track, "Picking", Color.white, 15, TextAnchor.MiddleCenter);
            UiKit.Stretch(_picking.rectTransform);

            if (!_drawRunes) BuildSheet(root, deck);
        }

        /// <summary>내 덱의 손 모양 안내 — 표는 추적기가 보내 주므로 도착하면 채운다</summary>
        private void BuildSheet(RectTransform root, int[] deck)
        {
            _rows = new Row[deck.Length];
            for (int i = 0; i < deck.Length; i++)
            {
                float y = PreviewHeight + BarHeight - (i + 1) * RowHeight - 8f;
                string code = deck[i] > 0 ? MediaPipeHandInput.SpellCode(deck[i]) : null;
                var row = new Row { Code = code, Slot = i, Fingers = new Image[5] };
                for (int f = 0; f < row.Fingers.Length; f++)
                {
                    var finger = Box(root, "Finger", Dim);
                    Place(finger, 8f + f * 7f, y + 3f, 5f, 6f);
                    row.Fingers[f] = finger.GetComponent<Image>();
                }
                row.Name = Label(root, "Name", UiTheme.Text, 14, TextAnchor.MiddleLeft);
                Place(row.Name.rectTransform, 48f, y, SheetWidth - 52f, RowHeight);
                row.Name.text = code != null ? MetaSkills.Name(code) : "";
                _rows[i] = row;
            }
        }

        private void Update()
        {
            if (_input == null) return;
            bool show = _input.isActiveAndEnabled; // 경기 메뉴 · 관전 중에는 입력이 꺼져 있다
            if (_canvas.enabled != show) _canvas.enabled = show;
            if (!show) return;

            byte[] jpeg = _input.TakePreview();
            if (jpeg != null) ShowFrame(jpeg);
            _status.text = Status();
            UpdatePicking();
            UpdateSheet();
        }

        private void ShowFrame(byte[] jpeg)
        {
            if (_texture == null)
            {
                _texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
                _preview.texture = _texture;
                _preview.color = Color.white;
            }
            if (!_texture.LoadImage(jpeg)) return;
            // 4:3 카메라는 위아래를 잘라 칸에 맞춘다
            float visible = Mathf.Min(1f, (float)_texture.width / _texture.height / PreviewAspect);
            _preview.uvRect = new Rect(0f, (1f - visible) * 0.5f, 1f, visible);
        }

        private string Status()
        {
            if (_input.CameraFailed) return "웹캠을 열 수 없습니다\n다른 프로그램을 끄거나 1~8 · 마우스로 플레이하세요";
            if (_input.IsTrackerConnected) return "";
            if (WebcamTracker.IsRunning)
                return WebcamTracker.IsFirstRun ? "손 인식을 처음 설치하는 중…\n(1~3분, 그동안 1~8 · 마우스)" : "웹캠 켜는 중…";
            return "손 추적기가 꺼졌습니다\n1~8 · 마우스로 플레이하세요";
        }

        private void UpdatePicking()
        {
            bool aiming = _cast != null && _cast.State == CastState.Aiming;
            bool drawing = _cast != null && _cast.State == CastState.Drawing;
            float progress = aiming ? _cast.AimProgress : _input.ShapeProgress;
            _fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
            if (!_input.IsTrackerConnected) _picking.text = "";
            else if (_drawRunes) _picking.text = drawing ? "그리는 중 · 주먹 = 끝" : "엄지 · 검지를 붙여 룬 그리기";
            else if (aiming) _picking.text = "손을 움직여 조준 · 화면 밖으로 빼면 취소";
            else if (progress > 0f && !string.IsNullOrEmpty(_input.ShapeSpell)) _picking.text = MetaSkills.Name(_input.ShapeSpell);
            else _picking.text = "오른손 모양 = 스킬 · 왼손 휘두르기 = 이동";
        }

        private void UpdateSheet()
        {
            if (_rows == null) return;
            for (int i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (row.Code == null) continue;
                if (row.Pattern == null) row.Pattern = FindPattern(row.Code);
                bool ready = _slots.IsReady(row.Slot);
                bool current = row.Pattern != null && row.Pattern == _input.ShapePattern;
                row.Name.color = current ? UiTheme.CyanLight : ready ? UiTheme.Text : Dim;
                for (int f = 0; f < row.Fingers.Length && row.Pattern != null; f++)
                {
                    bool up = row.Pattern[f] == '1';
                    row.Fingers[f].color = up ? (current ? UiTheme.CyanLight : UiTheme.Text) : Dim;
                    row.Fingers[f].rectTransform.sizeDelta = new Vector2(5f, up ? 16f : 6f);
                }
            }
        }

        private string FindPattern(string code)
        {
            foreach (var pair in _input.ShapeSpells)
                if (pair.Value == code) return pair.Key;
            return null;
        }

        private static RectTransform Box(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        private static Text Label(Transform parent, string name, Color color, int size, TextAnchor align)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = UiTheme.Regular;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>왼쪽 아래 기준 위치 · 크기</summary>
        private static void Place(RectTransform rt, float x, float y, float width, float height)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, height);
        }
    }
}
