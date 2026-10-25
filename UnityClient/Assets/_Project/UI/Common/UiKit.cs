// 메타 UI 조립 도구 — VR UI 프로토타입(Figma Make, 이식 후 삭제)의 부품(VRButton · MagicInput · EnergyBar · Toggle)과 웹 클라이언트의 공용 부품
// (버튼 · 카드 · 입력칸 · 스위치 · 범위 · 탭 · 칩 · 배지 · 스킬 아이콘)을 UGUI로 코드에서 만든다.
// 화면(C01~C13)은 이 함수들만 조합해 그린다. 레이아웃은 Vertical/Horizontal LayoutGroup.

using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public enum BtnStyle { Play, Default, Info, Danger, Ghost, Text }
    public enum Tone { Neutral, Cyan, Enemy, Amber, Play }

    public static class UiKit
    {
        // ── 노드 · 레이아웃 ──────────────────────────────────

        public static RectTransform Node(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent != null ? parent.gameObject.layer : 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>부모를 꽉 채우기 (여백: 왼 · 위 · 오른 · 아래)</summary>
        public static RectTransform Stretch(RectTransform rt, float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static RectTransform Anchor(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            return rt;
        }

        public static T Size<T>(T c, float width = -1, float height = -1, float flexWidth = -1, float flexHeight = -1) where T : Component
        {
            if (!c.TryGetComponent(out LayoutElement le)) le = c.gameObject.AddComponent<LayoutElement>();
            // 크기를 정하면 그 방향은 고정 (늘어나지 않음) — 늘리고 싶으면 flex 값을 함께 준다
            if (width >= 0) { le.preferredWidth = width; le.minWidth = width; if (flexWidth < 0) le.flexibleWidth = 0; }
            if (height >= 0) { le.preferredHeight = height; le.minHeight = height; if (flexHeight < 0) le.flexibleHeight = 0; }
            if (flexWidth >= 0) le.flexibleWidth = flexWidth;
            if (flexHeight >= 0) le.flexibleHeight = flexHeight;
            return c;
        }

        public static RectTransform VBox(Transform parent, float spacing = 14, int pad = 0, TextAnchor align = TextAnchor.UpperLeft, string name = "VBox")
        {
            var rt = Node(parent, name);
            var g = rt.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(pad, pad, pad, pad);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            return rt;
        }

        public static RectTransform HBox(Transform parent, float spacing = 12, int pad = 0, TextAnchor align = TextAnchor.MiddleLeft, string name = "HBox")
        {
            var rt = Node(parent, name);
            var g = rt.gameObject.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(pad, pad, pad, pad);
            g.childAlignment = align;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            return rt;
        }

        public static RectTransform Grid(Transform parent, Vector2 cell, Vector2 spacing, int columns, string name = "Grid")
        {
            var rt = Node(parent, name);
            var g = rt.gameObject.AddComponent<GridLayoutGroup>();
            g.cellSize = cell;
            g.spacing = spacing;
            g.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            g.constraintCount = columns;
            g.childAlignment = TextAnchor.UpperLeft;
            return rt;
        }

        /// <summary>내용 높이(· 폭)에 맞춰 크기 조절</summary>
        public static RectTransform Fit(RectTransform rt, bool vertical = true, bool horizontal = false)
        {
            if (!rt.TryGetComponent(out ContentSizeFitter f)) f = rt.gameObject.AddComponent<ContentSizeFitter>();
            f.verticalFit = vertical ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            f.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            return rt;
        }

        public static RectTransform Spacer(Transform parent, float flex = 1, float minSize = 0)
        {
            var rt = Node(parent, "Spacer");
            var le = rt.gameObject.AddComponent<LayoutElement>();
            // 가로 줄에서는 가로로만, 세로 줄에서는 세로로만 늘어난다 (반대 방향으로 부모를 늘리지 않게)
            bool horizontal = parent != null && parent.GetComponent<HorizontalLayoutGroup>() != null;
            bool vertical = parent != null && parent.GetComponent<VerticalLayoutGroup>() != null;
            le.flexibleWidth = vertical ? 0 : flex;
            le.flexibleHeight = horizontal ? 0 : flex;
            le.minWidth = vertical ? 0 : minSize;
            le.minHeight = horizontal ? 0 : minSize;
            return rt;
        }

        public static void Clear(Transform t)
        {
            for (int i = t.childCount - 1; i >= 0; i--)
            {
                var c = t.GetChild(i).gameObject;
                c.SetActive(false);
                UnityEngine.Object.Destroy(c);
            }
        }

        // ── 판 · 카드 ─────────────────────────────────────────

        public static Image Panel(Transform parent, Color color, Sprite sprite = null, string name = "Panel")
        {
            var rt = Node(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            if (sprite != null)
            {
                img.sprite = sprite;
                img.type = Image.Type.Sliced;
                img.color = Color.white;
            }
            img.raycastTarget = false;
            return img;
        }

        /// <summary>깎은 모서리 카드 (panel-2, 1px 선) — 세로 배치 + 안쪽 여백</summary>
        public static RectTransform Card(Transform parent, int pad = 24, float spacing = 14, string name = "Card", Color? fill = null, Color? border = null)
        {
            var rt = VBox(parent, spacing, pad, TextAnchor.UpperLeft, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(fill ?? UiTheme.Panel2, border ?? UiTheme.Line, 14, 2);
            img.type = Image.Type.Sliced;
            img.raycastTarget = true; // 카드 위 빈 곳 클릭이 뒤로 새지 않게
            return rt;
        }

        public static RectTransform CardRow(Transform parent, int pad = 22, float spacing = 14, string name = "CardRow", Color? fill = null, Color? border = null)
        {
            var rt = HBox(parent, spacing, pad, TextAnchor.MiddleLeft, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(fill ?? UiTheme.Panel2, border ?? UiTheme.Line, 14, 2);
            img.type = Image.Type.Sliced;
            return rt;
        }

        // ── 글자 ─────────────────────────────────────────────

        public static Text Label(Transform parent, string text, int size = UiTheme.FontBody, Color? color = null, bool bold = false,
                                 TextAnchor align = TextAnchor.MiddleLeft, string name = "Label")
        {
            var rt = Node(parent, name);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = bold ? UiTheme.Bold : UiTheme.Regular;
            t.fontSize = size;
            t.color = color ?? UiTheme.Text;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.text = text;
            t.lineSpacing = 1.05f;
            return t;
        }

        /// <summary>프로토타입 display 글자 (Cinzel) — 한글이 들어 있으면 Noto Sans KR Bold로 바꾼다</summary>
        public static Text Display(Transform parent, string text, int size, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, bool glow = false)
        {
            var t = Label(parent, text, size, color ?? UiTheme.Text, true, align, "Display");
            if (!UiTheme.HasHangul(text)) { t.font = UiTheme.Display; t.fontStyle = FontStyle.Bold; }
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            if (glow)
            {
                // 빛 번짐 흉내: 같은 색 반투명 외곽선 + 아래 그림자
                var o = t.gameObject.AddComponent<Outline>();
                o.effectColor = UiTheme.Alpha(color ?? UiTheme.Purple, 0.28f);
                o.effectDistance = new Vector2(3f, 3f);
                var sh = t.gameObject.AddComponent<Shadow>();
                sh.effectColor = UiTheme.Alpha(color ?? UiTheme.Purple, 0.22f);
                sh.effectDistance = new Vector2(0f, -4f);
            }
            return t;
        }

        /// <summary>작은 대문자 머리말 (프로토타입 "VR MAGIC BATTLE" · "WAR OF WIZARDS") — 글자 사이를 띄운다</summary>
        public static Text Kicker(Transform parent, string text, Color? color = null, TextAnchor align = TextAnchor.MiddleLeft, int size = 16)
        {
            bool hangul = UiTheme.HasHangul(text);
            var spaced = hangul ? text : string.Join(" ", text.ToUpperInvariant().ToCharArray());
            var t = Label(parent, spaced, size, color ?? UiTheme.Text3, true, align, "Kicker");
            if (!hangul) t.font = UiTheme.Display;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return t;
        }

        /// <summary>가운데 마름모가 있는 장식 구분선 (보라 → 청록)</summary>
        public static RectTransform Divider(Transform parent, float width = 220)
        {
            var row = HBox(parent, 10, 0, TextAnchor.MiddleCenter, "Divider");
            var l = Panel(row, UiTheme.Alpha(UiTheme.Purple, 0.6f));
            Size(l, width * 0.42f, 2);
            var holder = Node(row, "Diamond");
            Size(holder, 14, 14);
            var d = Panel(holder, UiTheme.PurpleLight);
            d.rectTransform.sizeDelta = new Vector2(10, 10);
            d.rectTransform.localRotation = Quaternion.Euler(0, 0, 45);
            var r = Panel(row, UiTheme.Alpha(UiTheme.Cyan, 0.6f));
            Size(r, width * 0.42f, 2);
            return row;
        }

        /// <summary>버튼 · 판 바깥으로 번지는 빛 (CSS box-shadow 흉내) — target 의 첫 자식으로 깔린다</summary>
        public static Image Halo(Transform target, Color color, float alpha = 0.22f, float spreadX = 46, float spreadY = 28)
        {
            var glow = Panel(target, Alpha(color, alpha), null, "Halo");
            glow.sprite = UiTheme.Glow();
            glow.rectTransform.SetAsFirstSibling();
            Stretch(glow.rectTransform, -spreadX, -spreadY, -spreadX, -spreadY);
            glow.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            return glow;
        }

        private static Color Alpha(Color c, float a) => UiTheme.Alpha(c, a);

        /// <summary>제목 (h2) + 오른쪽 작은 숫자</summary>
        public static Text Heading(Transform parent, string text, string count = null, int size = UiTheme.FontTitle)
        {
            var t = Label(parent, count == null ? text : $"{text}  <size={size - 8}><color=#{ColorUtility.ToHtmlStringRGB(UiTheme.Text3)}>{count}</color></size>", size, UiTheme.Text, true);
            return t;
        }

        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        // ── 버튼 ─────────────────────────────────────────────

        public static Button Button(Transform parent, string label, BtnStyle style, UnityAction onClick, float width = -1, float height = UiTheme.ButtonHeight, int fontSize = UiTheme.FontBody)
        {
            var rt = Node(parent, "Btn_" + label);
            var img = rt.gameObject.AddComponent<Image>();
            Color text;
            switch (style)
            {
                // primary: 반투명 보라 + 보라 테두리 + 코너 강조 (VRButton primary)
                case BtnStyle.Play: img.sprite = UiTheme.Cut(new Color(0.30f, 0.14f, 0.62f, 0.62f), UiTheme.Alpha(UiTheme.Purple, 0.85f), 10, 2); text = UiTheme.Hex(0xede9fe); break;
                // secondary: 반투명 청록
                case BtnStyle.Info: img.sprite = UiTheme.Cut(new Color(0.03f, 0.30f, 0.38f, 0.45f), UiTheme.Alpha(UiTheme.Cyan, 0.7f), 10, 2); text = UiTheme.Hex(0xcffafe); break;
                case BtnStyle.Danger: img.sprite = UiTheme.Cut(new Color(0.45f, 0.08f, 0.10f, 0.4f), UiTheme.Alpha(UiTheme.Enemy, 0.7f), 10, 2); text = UiTheme.Hex(0xfecaca); break;
                // ghost: 테두리만
                case BtnStyle.Ghost: img.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.02f), UiTheme.Alpha(UiTheme.Purple, 0.4f), 10, 2); text = UiTheme.PurplePale; break;
                case BtnStyle.Text: img.sprite = UiTheme.Cut(new Color(0f, 0f, 0f, 0.01f), new Color(0f, 0f, 0f, 0.01f), 0, 1); text = UiTheme.CyanLight; break;
                default: img.sprite = UiTheme.Cut(UiTheme.Panel3, UiTheme.Line2, 10, 2); text = UiTheme.Text; break;
            }
            img.type = Image.Type.Sliced;
            var b = rt.gameObject.AddComponent<Button>();
            var cb = b.colors;
            cb.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            cb.highlightedColor = Color.white;
            cb.selectedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
            cb.pressedColor = new Color(0.72f, 0.72f, 0.72f, 1f);
            cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.55f);
            cb.fadeDuration = 0.06f;
            b.colors = cb;
            if (onClick != null) b.onClick.AddListener(onClick);
            var l = Label(rt, label, fontSize, text, true, TextAnchor.MiddleCenter);
            Stretch((RectTransform)l.transform, 18, 0, 18, 0);
            l.horizontalOverflow = HorizontalWrapMode.Overflow;
            float w = width > 0 ? width : Mathf.Max(96f, l.preferredWidth + 44f);
            Size(b, w, height);
            return b;
        }

        public static void SetLabel(Button b, string label)
        {
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        /// <summary>요청 중 버튼: 글자를 바꾸고 막았다가 done 때 되돌린다</summary>
        public static Action Busy(Button b, string busyLabel = "잠시만요…")
        {
            if (b == null) return () => { };
            var t = b.GetComponentInChildren<Text>();
            string old = t != null ? t.text : null;
            bool wasOn = b.interactable;
            if (t != null) t.text = busyLabel;
            b.interactable = false;
            return () =>
            {
                if (b == null) return;
                if (t != null) t.text = old;
                b.interactable = wasOn;
            };
        }

        // ── 입력 ─────────────────────────────────────────────

        public static InputField Input(Transform parent, string placeholder, bool password = false, int maxLength = 64, float height = UiTheme.InputHeight)
        {
            var rt = Node(parent, "Input");
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(UiTheme.InputWell, UiTheme.Alpha(UiTheme.Purple, 0.4f), 0, 2);
            img.type = Image.Type.Sliced;
            Size(img, -1, height);

            var text = Label(rt, "", UiTheme.FontBody, UiTheme.Text, false, TextAnchor.MiddleLeft, "Text");
            Stretch((RectTransform)text.transform, 18, 4, 18, 4);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.supportRichText = false;
            var ph = Label(rt, placeholder, UiTheme.FontBody, UiTheme.Text3, false, TextAnchor.MiddleLeft, "Placeholder");
            Stretch((RectTransform)ph.transform, 18, 4, 18, 4);
            ph.horizontalOverflow = HorizontalWrapMode.Overflow;

            var input = rt.gameObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = ph;
            input.characterLimit = maxLength;
            input.lineType = InputField.LineType.SingleLine;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            // Quest(안드로이드)에서는 시스템 키보드를 띄우지 않는다 — VR 화면 키보드(VirtualKeyboard)가 글자를 넣는다
            input.shouldActivateOnSelect = !Application.isMobilePlatform;
            input.caretColor = UiTheme.Cyan;
            input.selectionColor = new Color(UiTheme.Cyan.r, UiTheme.Cyan.g, UiTheme.Cyan.b, 0.35f);
            input.caretWidth = 3;
            var cb = input.colors;
            cb.highlightedColor = new Color(1.0f, 1.0f, 1.0f, 1f);
            cb.normalColor = new Color(0.92f, 0.92f, 0.92f, 1f);
            input.colors = cb;
            return input;
        }

        /// <summary>라벨 + 입력칸 (+ 아래 안내)</summary>
        public static InputField Field(Transform parent, string label, string placeholder, bool password = false, int maxLength = 64, string hint = null)
        {
            var box = VBox(parent, 8, 0, TextAnchor.UpperLeft, "Field");
            Label(box, label, UiTheme.FontLabel, UiTheme.PurpleLight, true);
            var input = Input(box, placeholder, password, maxLength);
            if (hint != null) Label(box, hint, UiTheme.FontSmall - 2, UiTheme.Text3, false, TextAnchor.UpperLeft, "Hint");
            return input;
        }

        /// <summary>켜짐 · 꺼짐 스위치</summary>
        public static Toggle Switch(Transform parent, bool value, Action<bool> onChange) => Switch(parent, value, onChange, UiTheme.Purple);

        /// <summary>네온 토글 (프로토타입 Settings Toggle) — 켜지면 color 테두리 · 손잡이</summary>
        public static Toggle Switch(Transform parent, bool value, Action<bool> onChange, Color color)
        {
            var rt = Node(parent, "Switch");
            Size(rt, 76, 40);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.Cut(Color.white, Color.white, 0, 0);
            bg.type = Image.Type.Sliced;
            var knobRt = Node(rt, "Knob");
            knobRt.anchorMin = knobRt.anchorMax = new Vector2(0f, 0.5f);
            knobRt.sizeDelta = new Vector2(30, 30);
            var knob = knobRt.gameObject.AddComponent<Image>();
            knob.sprite = UiTheme.Circle();
            knob.raycastTarget = false;
            var t = rt.gameObject.AddComponent<Toggle>();
            t.targetGraphic = bg;
            t.isOn = value;
            bg.sprite = UiTheme.Cut(new Color(1f, 1f, 1f, 0.22f), Color.white, 0, 2);
            void Paint(bool on)
            {
                bg.color = on ? color : UiTheme.Alpha(UiTheme.Purple, 0.4f);
                knobRt.anchoredPosition = new Vector2(on ? 56f : 20f, 0f);
                knob.color = on ? color : UiTheme.Alpha(UiTheme.Purple, 0.5f);
            }
            Paint(value);
            t.onValueChanged.AddListener(on => { Paint(on); onChange?.Invoke(on); });
            return t;
        }

        public static Slider Range(Transform parent, float min, float max, float value, bool whole, Action<float> onChange) =>
            Range(parent, min, max, value, whole, onChange, UiTheme.Purple);

        /// <summary>네온 슬라이더 (프로토타입 Settings Slider) — color 채움 + 어두운 손잡이에 color 테두리</summary>
        public static Slider Range(Transform parent, float min, float max, float value, bool whole, Action<float> onChange, Color color)
        {
            var rt = Node(parent, "Range");
            Size(rt, -1, 36, 1);
            var bgRt = Stretch(Node(rt, "Track"), 0, 14, 0, 14);
            bgRt.gameObject.AddComponent<Image>().color = UiTheme.Alpha(UiTheme.Purple, 0.22f);
            var fillArea = Stretch(Node(rt, "FillArea"), 0, 14, 0, 14);
            var fill = Node(fillArea, "Fill");
            fill.sizeDelta = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = color;
            var handleArea = Stretch(Node(rt, "HandleArea"), 14, 0, 14, 0);
            var handle = Node(handleArea, "Handle");
            handle.sizeDelta = new Vector2(30, 0);
            var hImg = handle.gameObject.AddComponent<Image>();
            hImg.sprite = UiTheme.Circle();
            hImg.color = color;
            var hole = Panel(handle, UiTheme.Bg1);
            hole.sprite = UiTheme.Circle();
            Stretch(hole.rectTransform, 5, 5, 5, 5);
            var s = rt.gameObject.AddComponent<Slider>();
            s.fillRect = fill;
            s.handleRect = handle;
            s.targetGraphic = hImg;
            s.direction = Slider.Direction.LeftToRight;
            s.minValue = min;
            s.maxValue = max;
            s.wholeNumbers = whole;
            s.value = value;
            if (onChange != null) s.onValueChanged.AddListener(v => onChange(v));
            return s;
        }

        /// <summary>세로 스크롤 목록 — content에 항목을 넣는다 (VerticalLayout + 높이 맞춤)</summary>
        public static ScrollRect Scroll(Transform parent, out RectTransform content, float spacing = 10, int pad = 0)
        {
            var rt = Node(parent, "Scroll");
            var sr = rt.gameObject.AddComponent<ScrollRect>();
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = new Color(0, 0, 0, 0.001f); // 빈 곳도 드래그 · 휠이 먹도록
            var viewport = Stretch(Node(rt, "Viewport"));
            viewport.gameObject.AddComponent<RectMask2D>();
            content = Node(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var g = content.gameObject.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(pad, pad, pad, pad);
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = true;
            g.childForceExpandHeight = false;
            Fit(content);
            sr.viewport = viewport;
            sr.content = content;
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            sr.inertia = true;
            return sr;
        }

        // ── 표시 ─────────────────────────────────────────────

        /// <summary>작은 꼬리표 (상태 · 등급 등)</summary>
        public static Text Chip(Transform parent, string text, Tone tone = Tone.Neutral, int size = UiTheme.FontSmall)
        {
            Color fill, border, fg;
            switch (tone)
            {
                case Tone.Cyan: fill = Color.Lerp(UiTheme.Panel, UiTheme.Cyan, 0.14f); border = UiTheme.Cyan2; fg = UiTheme.Cyan; break;
                case Tone.Enemy: fill = Color.Lerp(UiTheme.Panel, UiTheme.Enemy, 0.14f); border = Color.Lerp(UiTheme.Panel, UiTheme.Enemy, 0.6f); fg = UiTheme.Enemy; break;
                case Tone.Amber: fill = Color.Lerp(UiTheme.Panel, UiTheme.Amber, 0.16f); border = Color.Lerp(UiTheme.Panel, UiTheme.Amber, 0.6f); fg = UiTheme.Amber; break;
                case Tone.Play: fill = Color.Lerp(UiTheme.Panel, UiTheme.Play, 0.18f); border = UiTheme.PlayLo; fg = UiTheme.PlayHi; break;
                default: fill = UiTheme.Panel2; border = UiTheme.Line; fg = UiTheme.Text2; break;
            }
            var box = HBox(parent, 0, 0, TextAnchor.MiddleCenter, "Chip");
            box.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(14, 14, 4, 4);
            var img = box.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(fill, border, 0, 2);
            img.type = Image.Type.Sliced;
            var t = Label(box, text, size, fg, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Fit(box, true, true);
            return t;
        }

        /// <summary>안 읽음 수 배지 (분홍빨강 원)</summary>
        public static Image Badge(Transform parent, int count, float size = 34)
        {
            var rt = Node(parent, "Badge");
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Circle();
            img.color = UiTheme.Enemy;
            img.raycastTarget = false;
            Size(img, size, size);
            var t = Label(rt, count > 99 ? "99+" : count.ToString(), (int)(size * 0.52f), Color.white, true, TextAnchor.MiddleCenter);
            Stretch((RectTransform)t.transform);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            return img;
        }

        public static Image Dot(Transform parent, Color color, float size = 14)
        {
            var rt = Node(parent, "Dot");
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Circle();
            img.color = color;
            img.raycastTarget = false;
            Size(img, size, size);
            return img;
        }

        /// <summary>안내 띠 (주황 기본 / 청록 정보 / 빨강 오류)</summary>
        public static Text Note(Transform parent, string text, Tone tone = Tone.Amber)
        {
            Color c = tone == Tone.Cyan ? UiTheme.Cyan : tone == Tone.Enemy ? UiTheme.Enemy : UiTheme.Amber;
            var box = HBox(parent, 0, 0, TextAnchor.MiddleLeft, "Note");
            box.GetComponent<HorizontalLayoutGroup>().padding = new RectOffset(18, 18, 12, 12);
            box.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            var img = box.gameObject.AddComponent<Image>();
            img.sprite = UiTheme.Cut(Color.Lerp(UiTheme.Panel, c, 0.12f), Color.Lerp(UiTheme.Panel, c, 0.5f), 0, 2);
            img.type = Image.Type.Sliced;
            return Label(box, text, UiTheme.FontSmall, Color.Lerp(c, Color.white, 0.35f), false, TextAnchor.MiddleLeft);
        }

        /// <summary>탭 줄 — 선택된 탭은 청록 밑줄</summary>
        public static RectTransform Tabs(Transform parent, string[] labels, int selected, Action<int> onSelect, int[] badges = null)
        {
            var row = HBox(parent, 6, 0, TextAnchor.LowerLeft, "Tabs");
            Size(row, -1, 56);
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                bool on = i == selected;
                var b = Button(row, labels[i], BtnStyle.Text, () => onSelect(idx), -1, 52, UiTheme.FontBody);
                b.GetComponentInChildren<Text>().color = on ? UiTheme.Text : UiTheme.Text3;
                if (badges != null && i < badges.Length && badges[i] > 0)
                {
                    var bd = Badge(b.transform, badges[i], 26);
                    var brt = (RectTransform)bd.transform;
                    brt.anchorMin = brt.anchorMax = new Vector2(1f, 0.7f);
                    brt.anchoredPosition = new Vector2(-6f, 0f);
                    bd.GetComponent<LayoutElement>().ignoreLayout = true;
                    brt.sizeDelta = new Vector2(26, 26);
                }
                if (on)
                {
                    var line = Panel(b.transform, UiTheme.Cyan, null, "Underline");
                    var lrt = (RectTransform)line.transform;
                    Anchor(lrt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(10, 0), new Vector2(-10, 4));
                }
            }
            return row;
        }

        /// <summary>진행 막대 (0~1)</summary>
        public static RectTransform Progress(Transform parent, float value, Color color, float height = 10)
        {
            var rt = Node(parent, "Progress");
            Size(rt, -1, height, 1);
            rt.gameObject.AddComponent<Image>().color = UiTheme.InputWell;
            var fill = Node(rt, "Fill");
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;
            fill.gameObject.AddComponent<Image>().color = color;
            return fill;
        }

        public static void SetProgress(RectTransform fill, float value)
        {
            if (fill != null) fill.anchorMax = new Vector2(Mathf.Clamp01(value), 1f);
        }

        // ── 스킬 · 마법사 그림 ─────────────────────────────────

        /// <summary>발동 마법진 (둥근 슬롯, 고위력은 호박 테두리)</summary>
        public static RectTransform SkillIcon(Transform parent, string code, float size, bool grey = false)
        {
            var rt = Node(parent, "Skill_" + code);
            Size(rt, size, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.Circle();
            bg.color = UiTheme.Bg1;
            bg.raycastTarget = false;
            rt.gameObject.AddComponent<Mask>(); // 그림(검은 네모 바탕)을 원 안으로 자른다
            var art = Panel(rt, Color.white, null, "Art");
            Stretch((RectTransform)art.transform, size * 0.06f, size * 0.06f, size * 0.06f, size * 0.06f);
            art.sprite = UiTheme.SkillCircle(code);
            art.preserveAspect = true;
            art.color = grey ? new Color(0.45f, 0.45f, 0.45f, 0.8f) : Color.white;
            bool high = MetaSkills.IsHigh(code);
            var ring = Panel(rt, high ? UiTheme.Amber : UiTheme.Line2, null, "Ring");
            ring.sprite = UiTheme.Ring(128, high ? 0.07f : 0.04f);
            Stretch((RectTransform)ring.transform);
            return rt;
        }

        /// <summary>그리는 룬 그림 (깎은 판 위)</summary>
        public static RectTransform RuneIcon(Transform parent, string code, float size)
        {
            var rt = Node(parent, "Rune_" + code);
            Size(rt, size, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.Cut(UiTheme.Hex(0xf3efe6), UiTheme.Line2, 8, 2); // 룬 그림은 흰 바탕 검은 선
            bg.type = Image.Type.Sliced;
            bg.raycastTarget = false;
            var art = Panel(rt, Color.white, null, "Art");
            Stretch((RectTransform)art.transform, size * 0.08f, size * 0.08f, size * 0.08f, size * 0.08f);
            art.sprite = UiTheme.SkillRune(code);
            art.preserveAspect = true;
            return rt;
        }

        public static Image Wizard(Transform parent, bool foe, float height, bool unknown = false)
        {
            var img = Panel(parent, Color.white, null, foe ? "WizardFoe" : "WizardYou");
            img.sprite = UiTheme.Wizard(foe);
            img.preserveAspect = true;
            if (unknown) img.color = new Color(0.08f, 0.1f, 0.14f, 0.95f); // 아직 모르는 상대 = 실루엣
            Size(img, height * 0.8f, height);
            return img;
        }

        /// <summary>마법사 1:1 (파랑 나 vs 빨강 상대)</summary>
        public static RectTransform Duel(Transform parent, float height, bool foeKnown = true)
        {
            var row = HBox(parent, 24, 0, TextAnchor.MiddleCenter, "Duel");
            Wizard(row, false, height);
            var vs = Label(row, "VS", (int)(height * 0.16f), UiTheme.Text3, true, TextAnchor.MiddleCenter);
            vs.horizontalOverflow = HorizontalWrapMode.Overflow;
            Wizard(row, true, height, !foeKnown);
            return row;
        }

        /// <summary>"룬 그리기 → 준비 → 조준 → 발동" 4단계 안내</summary>
        public static RectTransform HowTo(Transform parent)
        {
            var row = HBox(parent, 10, 0, TextAnchor.MiddleLeft, "HowTo");
            row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = true;
            string[,] steps = { { "01", "룬 그리기", "핀치한 채 허공에" }, { "02", "준비", "주먹을 쥔다" }, { "03", "조준", "검지로 열 지정" }, { "04", "발동", "마법진이 터진다" } };
            for (int i = 0; i < 4; i++)
            {
                var c = Card(row, 14, 2, "Step", i == 0 ? Color.Lerp(UiTheme.Panel, UiTheme.Cyan, 0.12f) : UiTheme.Panel, i == 0 ? UiTheme.Cyan2 : UiTheme.Line);
                Size(c, -1, -1, 1);
                Label(c, steps[i, 0], UiTheme.FontLabel, i == 0 ? UiTheme.Cyan : UiTheme.Text3, true);
                Label(c, steps[i, 1], UiTheme.FontBody, UiTheme.Text, true);
                Label(c, steps[i, 2], UiTheme.FontSmall - 2, UiTheme.Text3);
            }
            return row;
        }
    }
}
