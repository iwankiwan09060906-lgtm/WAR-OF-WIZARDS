// VR 화면 키보드 — 헤드셋에는 물리 키보드가 없으므로 입력칸을 고르면 아래에 영문 · 숫자 키보드가 뜬다.
// 이메일 · 비밀번호 · 룸 코드 · 영문 닉네임 · 서버 주소(: /)용 (한글 입력은 지원하지 않는다 — 한글 닉네임은 PC에서 바꾼다).

using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class VirtualKeyboard
    {
        private static readonly string[] Rows = { "1234567890-", "qwertyuiop_", "asdfghjkl@", "zxcvbnm.:/" };

        private readonly RectTransform _root;
        private readonly Text _preview;
        private InputField _target;
        private bool _shift;
        private readonly System.Collections.Generic.List<Text> _letterLabels = new System.Collections.Generic.List<Text>();

        public bool IsOpen => _root.gameObject.activeSelf;
        public InputField Target => _target;

        public VirtualKeyboard(Transform layer)
        {
            _root = UiKit.Node(layer, "VirtualKeyboard");
            // 메뉴 판 바로 아래에 붙인다 (입력칸을 가리지 않게 — 월드 캔버스라 판 밖에 그려져도 레이로 누를 수 있다)
            _root.anchorMin = new Vector2(0.5f, 0f);
            _root.anchorMax = new Vector2(0.5f, 0f);
            _root.pivot = new Vector2(0.5f, 1f);
            _root.anchoredPosition = new Vector2(0f, -16f);
            _root.sizeDelta = new Vector2(1180f, 430f);
            var bg = _root.gameObject.AddComponent<Image>();
            bg.sprite = UiTheme.Cut(UiTheme.Bg1, UiTheme.Cyan2, 14, 2);
            bg.type = Image.Type.Sliced;
            var col = UiKit.Stretch(UiKit.VBox(_root, 8, 16, TextAnchor.UpperCenter, "Keys"));
            _preview = UiKit.Label(col, "", UiTheme.FontBody, UiTheme.Cyan, true, TextAnchor.MiddleCenter);
            UiKit.Size(_preview, -1, 40);
            foreach (string row in Rows)
            {
                var r = UiKit.HBox(col, 8, 0, TextAnchor.MiddleCenter, "Row");
                foreach (char ch in row)
                {
                    char c = ch;
                    var b = UiKit.Button(r, c.ToString(), BtnStyle.Default, () => Type(c), 96, 62, UiTheme.FontTitle);
                    if (char.IsLetter(c)) _letterLabels.Add(b.GetComponentInChildren<Text>());
                }
            }
            var last = UiKit.HBox(col, 8, 0, TextAnchor.MiddleCenter, "Row");
            UiKit.Button(last, "Shift", BtnStyle.Ghost, ToggleShift, 150, 62);
            UiKit.Button(last, "Space", BtnStyle.Default, () => Type(' '), 270, 62);
            UiKit.Button(last, "← 지우기", BtnStyle.Danger, Backspace, 180, 62);
            UiKit.Button(last, "모두 지우기", BtnStyle.Ghost, ClearAll, 180, 62, UiTheme.FontSmall);
            UiKit.Button(last, "완료", BtnStyle.Play, Close, 190, 62);
            _root.gameObject.SetActive(false);
        }

        public void Open(InputField target)
        {
            _target = target;
            _root.gameObject.SetActive(true);
            _root.SetAsLastSibling();
            RefreshPreview();
        }

        public void Close()
        {
            _root.gameObject.SetActive(false);
            if (_target != null) _target.DeactivateInputField();
            _target = null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        private void Type(char c)
        {
            if (_target == null) return;
            if (char.IsLetter(c) && _shift) c = char.ToUpperInvariant(c);
            if (_target.characterLimit > 0 && _target.text.Length >= _target.characterLimit) return;
            _target.text += c;
            RefreshPreview();
        }

        private void Backspace()
        {
            if (_target == null || _target.text.Length == 0) return;
            _target.text = _target.text.Substring(0, _target.text.Length - 1);
            RefreshPreview();
        }

        private void ClearAll()
        {
            if (_target == null) return;
            _target.text = "";
            RefreshPreview();
        }

        private void ToggleShift()
        {
            _shift = !_shift;
            foreach (var t in _letterLabels) t.text = _shift ? t.text.ToUpperInvariant() : t.text.ToLowerInvariant();
        }

        private void RefreshPreview()
        {
            if (_target == null) return;
            bool hidden = _target.contentType == InputField.ContentType.Password;
            _preview.text = hidden ? new string('*', _target.text.Length) : _target.text;
        }
    }
}
