// 룬 가이드 — 스킬 룬의 실루엣(굵고 흐린 흰 획 + 획 시작 초록 점)을 눈앞에 크게 띄운다.
//   받아쓰기 칸의 흐린 글씨처럼 실루엣을 보고 그대로 따라 그리면 된다. 게임 중에는 설명 없이 실루엣만,
//   연습 모드(PracticeGuide)는 실루엣 아래에 그리는 법 설명을 붙인다 (Show 의 description).
//   Quest: 스킬창을 내려다보면 (눈 → 검지 끝) 방향이 스킬창에 닿는 곳에 포인터가 뜨고 가장 가까운 카드가 커진다 — 그대로 핀치하면 그 카드의 가이드.
//     내려다보지 않을 때의 핀치는 언제나 룬 그리기다 (앞에서 그리는 핀치를 카드 누르기로 잘못 먹지 않게).
//   아무 동작 없이 idleGuideSeconds 가 지나면 스킬창 카드 중 하나의 가이드를 저절로 띄운다.
//   그 룬이 인식되거나, 같은 카드를 다시 고르거나, 일정 시간이 지나면 닫힌다. 카드를 고른 핀치는 룬 그리기를 시작하지 않는다 (CastStateMachine.PinchFilter).

using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Input;
using SpellboundVR.Spells;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class SkillGuideView : MonoBehaviour
    {
        [Tooltip("눈앞 거리 (m) — 손을 뻗어 룬을 그리는 곳 바로 뒤 (손이 실루엣 앞에 보인다)")]
        public float distance = 0.65f;
        [Tooltip("눈높이에서 아래로 (m)")]
        public float drop = 0.12f;
        [Tooltip("가이드 판 폭 (m) — 실루엣이 실제로 그리는 크기(약 0.4m)가 되게")]
        public float width = 0.44f;
        [Tooltip("스킬창 가운데가 시선에서 이 각도(°) 안이면 내려다보는 중 — 포인터를 띄우고 핀치를 카드 고르기로 쓴다")]
        public float lookDownDegrees = 30f;
        [Tooltip("이 시간(초) 동안 아무 동작이 없으면 스킬창 카드 하나의 가이드를 띄운다 (0 = 안 함)")]
        public float idleGuideSeconds = 10f;
        [Tooltip("이 시간(초)이 지나면 저절로 닫힌다 (0 = 안 닫힘)")]
        public float autoHideSeconds = 15f;
        /// <summary>false: 카드 고르기 · 저절로 뜨는 가이드를 끈다 (연습 모드 — PracticeGuide 가 가이드를 직접 띄운다)</summary>
        public bool cardPicking = true;

        private const float CanvasSize = 600f;
        /// <summary>실루엣 아래 설명 칸 높이 (설명이 있을 때만)</summary>
        private const float DescriptionHeight = 250f;

        private ClientSession _session;
        private ISpellInputSource _input;
        private CastStateMachine _cast;
        private BattleHudView _hud;
        private Transform _head;
        private RectTransform _root;
        private RectTransform _guide;
        private Image _bg;
        private Text _title, _hint;
        private int _shownSpell;
        private float _hideAt = -1f;

        public bool Visible => _root != null && _root.gameObject.activeSelf;
        public int ShownSpell => Visible ? _shownSpell : 0;

        public void Initialize(ClientSession session, ISpellInputSource input, CastStateMachine cast, BattleHudView hud, Transform head)
        {
            _session = session;
            _input = input;
            _cast = cast;
            _hud = hud;
            _head = head;
            _idleSince = Time.time;
            Build();
            if (_cast != null)
            {
                _cast.PinchFilter = HandlePinch;
                _cast.OnRuneRecognized += HandleRecognized;
            }
        }

        private void OnDestroy()
        {
            if (_cast == null) return;
            if (_cast.PinchFilter == HandlePinch) _cast.PinchFilter = null;
            _cast.OnRuneRecognized -= HandleRecognized;
        }

        private void Build()
        {
            var go = new GameObject("SkillGuide", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 1.5f;
            _root = (RectTransform)go.transform;
            _root.sizeDelta = new Vector2(CanvasSize, CanvasSize);
            _root.localScale = Vector3.one * (width / CanvasSize);

            // 실루엣이 밝은 하늘 · 바닥 위에서도 보이게 옅은 어두운 판
            _bg = UiKit.Panel(_root, Color.white, UiTheme.Cut(new Color(6 / 255f, 4 / 255f, 20 / 255f, 0.6f), UiTheme.Alpha(UiTheme.Purple, 0.5f), 16, 2), "Bg");
            _bg.type = Image.Type.Sliced;
            _bg.raycastTarget = false;
            UiKit.Stretch(_bg.rectTransform);
            _title = Text("Title", Vector2.zero, 38, UiTheme.Text);
            _hint = Text("Description", Vector2.zero, 27, UiTheme.CyanLight);
            _hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hint.rectTransform.sizeDelta = new Vector2(CanvasSize - 40f, DescriptionHeight - 70f);
            go.SetActive(false);
        }

        private Text Text(string name, Vector2 pos, int size, Color color)
        {
            var t = UiKit.Label(_root, "", size, color, true, TextAnchor.MiddleCenter, name);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(CanvasSize - 20f, 50f);
            t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return t;
        }

        /// <summary>
        /// 그 스킬의 룬 실루엣을 눈앞에 띄운다. description 이 있으면 실루엣 아래에 스킬 이름 + 설명 (연습), 없으면 실루엣만 (게임 중).
        /// seconds: 저절로 닫히기까지 (음수 = autoHideSeconds, 0 = 안 닫힘)
        /// </summary>
        public void Show(int spellId, string description = null, float seconds = -1f)
        {
            if (spellId <= 0 || _root == null) return;
            string code = SpellIds.Code(spellId);
            if (_guide != null) Destroy(_guide.gameObject);
            bool described = !string.IsNullOrEmpty(description);
            float height = CanvasSize + (described ? DescriptionHeight : 0f);
            _root.sizeDelta = new Vector2(CanvasSize, height);
            float pictureY = height * 0.5f - CanvasSize * 0.5f;
            _guide = UiKit.RuneSilhouette(_root, spellId, code, CanvasSize - 50f);
            _guide.anchorMin = _guide.anchorMax = _guide.pivot = new Vector2(0.5f, 0.5f);
            _guide.sizeDelta = new Vector2(CanvasSize - 50f, CanvasSize - 50f); // UiKit.Size 는 레이아웃 크기만 정한다
            _guide.anchoredPosition = new Vector2(0f, pictureY);
            _title.gameObject.SetActive(described);
            _hint.gameObject.SetActive(described);
            if (described)
            {
                var def = _session != null ? _session.Catalog.Get(spellId) : null;
                _title.text = MetaSkills.TryGet(code, out var info) ? info.Name : def != null ? def.DisplayName : code;
                _title.rectTransform.anchoredPosition = new Vector2(0f, -CanvasSize * 0.5f + pictureY - 30f);
                _hint.rectTransform.anchoredPosition = new Vector2(0f, -CanvasSize * 0.5f + pictureY - 60f - (DescriptionHeight - 70f) * 0.5f);
                _hint.text = description;
            }
            _shownSpell = spellId;
            float s = seconds < 0f ? autoHideSeconds : seconds;
            _hideAt = s > 0f ? Time.time + s : -1f;
            _settleUntil = Time.time + 0.5f;
            PlaceInFront();
            _root.gameObject.SetActive(true);
        }

        /// <summary>그리는 법 설명 — 룬 모양 이름 · 획 수 · 시작 점 (연습 모드 실루엣 아래)</summary>
        public static string HowToDraw(int spellId)
        {
            string code = SpellIds.Code(spellId);
            string shape = MetaSkills.TryGet(code, out var info) && !string.IsNullOrEmpty(info.Rune) ? "'" + info.Rune + "' 모양 실루엣을" : "실루엣을";
            int strokes = Gesture.RuneDesigns.TryGet(spellId, out var design) ? design.Strokes.Length : 1;
            string order = strokes > 1
                ? $"획이 {strokes}개 — 번호 순서대로, 획마다 손가락을 뗐다가 다시 붙여 그려요."
                : "한 번에 이어서 그려요.";
            return shape + " 초록 점에서 시작해 그대로 따라 그리세요.\n" + order;
        }

        /// <summary>설명만 바꾼다 (실루엣 아래, 설명이 있는 가이드일 때)</summary>
        public void SetHint(string hint)
        {
            if (_hint != null && _hint.gameObject.activeSelf) _hint.text = hint;
        }

        public void Hide()
        {
            if (_root != null) _root.gameObject.SetActive(false);
            _shownSpell = 0;
        }

        /// <summary>띄울 때 한 번 눈앞에 놓는다 (머리를 따라다니면 그리는 동안 흔들려 따라 그리기 어렵다)</summary>
        private void PlaceInFront()
        {
            if (_head == null) return;
            Vector3 fwd = _head.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
            fwd.Normalize();
            _root.position = _head.position + fwd * distance + Vector3.down * drop;
            _root.rotation = Quaternion.LookRotation(fwd, Vector3.up);
        }

        /// <summary>핀치 시작 — 스킬창 카드를 가리키고 있으면 가이드를 열고(같은 카드면 닫고) 그리기를 시작하지 않는다</summary>
        private bool HandlePinch()
        {
            if (!cardPicking || _hud == null || _input == null) return false;
            int spellId;
            if (_input is KeyboardInput)
            {
                // PC: 마우스로 카드를 클릭
                if (!_input.TryGetAimRay(out Ray ray) || !_hud.TryGetSlotOnRay(ray, out _, out spellId)) return false;
            }
            else
            {
                // Quest: 내려다보며 포인터가 가리키던 카드 (Update 가 핀치 직전 프레임까지 정해 둔다)
                if (_hoverSlot < 0) return false;
                spellId = _hoverSpell;
            }
            _idleSince = Time.time;
            if (Visible && _shownSpell == spellId) Hide();
            else Show(spellId);
            Audio.SoundManager.Play("SFX_UIClick", Audio.SoundVolume.Flow, null, 0f);
            return true;
        }

        private void HandleRecognized(Spells.SpellDefinition def, int slot)
        {
            if (Visible && def != null && def.SpellId == _shownSpell && _hideAt > 0f) Hide(); // 연습 모드(시간 제한 없음)는 PracticeGuide 가 닫는다
        }

        private float _settleUntil;
        private int _hoverSlot = -1, _hoverSpell, _highlighted = -1;
        private float _idleSince;
        private Image _pointer;

        private void Update()
        {
            // 앱 시작 직후엔 헤드셋 위치가 아직 안 잡혀 있고 리그가 자리로 순간이동한다 — 그동안은 계속 눈앞으로 다시 놓는다 (연습 첫 스킬)
            if (Visible && (Time.timeSinceLevelLoad < 4f || Time.time < _settleUntil)) PlaceInFront();
            if (_hideAt > 0f && Visible && Time.time > _hideAt) Hide();
            UpdatePointer();
            UpdateIdleGuide();
        }

        /// <summary>스킬창을 내려다보는가 — 스킬창 가운데가 시선 lookDownDegrees 안</summary>
        private bool LookingAtHud()
        {
            var canvas = _hud != null ? _hud.CanvasRect : null;
            return canvas != null && _head != null && Vector3.Angle(_head.forward, canvas.position - _head.position) < lookDownDegrees;
        }

        /// <summary>Quest 스킬창 포인터 — 눈 → 검지 끝 방향이 스킬창에 닿는 곳 (손이 안 보이면 시선), 가장 가까운 카드를 키운다</summary>
        private void UpdatePointer()
        {
            if (_input == null || _input is KeyboardInput) return;
            if (_input.IsDrawing) return; // 핀치로 손가락을 모으는 동안은 직전 카드 그대로 (핀치가 그 카드를 고른다)
            int slot = -1, spell = 0;
            Vector2 local = default;
            bool idle = _cast == null || (_cast.enabled && _cast.State == CastState.Idle);
            if (cardPicking && idle && LookingAtHud())
            {
                // 검지 끝이 스킬창 밖이면 시선으로 — 내려다보고 핀치하면 언제나 카드 하나를 고른다
                bool onHud = _input.TryGetRightHandPosition(out Vector3 tip) && _hud.TryPointAtSlots(new Ray(_head.position, tip - _head.position), out local, out slot, out spell);
                if (!onHud && !_hud.TryPointAtSlots(new Ray(_head.position, _head.forward), out local, out slot, out spell)) slot = -1;
            }
            _hoverSlot = slot;
            _hoverSpell = spell;
            if (slot != _highlighted)
            {
                _hud.HighlightSlot(slot);
                _highlighted = slot;
            }
            if (slot >= 0 && _pointer == null) _pointer = BuildPointer(_hud.CanvasRect);
            if (_pointer == null) return;
            _pointer.gameObject.SetActive(slot >= 0);
            if (slot < 0) return;
            _pointer.rectTransform.anchoredPosition = local;
            _pointer.rectTransform.SetAsLastSibling(); // 카드가 로테이션으로 다시 만들어져도 위에
            _idleSince = Time.time; // 스킬창을 보고 있는 것도 동작
        }

        private static Image BuildPointer(RectTransform canvas)
        {
            var dot = UiKit.Panel(canvas, UiTheme.CyanLight, UiTheme.Circle(), "Pointer");
            dot.raycastTarget = false;
            var rt = dot.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(34f, 34f);
            dot.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            return dot;
        }

        /// <summary>아무 동작 없이 idleGuideSeconds 가 지나면 스킬창 카드 하나의 가이드를 띄운다 (무엇을 그릴지 모르는 첫 관람객)</summary>
        private void UpdateIdleGuide()
        {
            bool busy = !cardPicking || idleGuideSeconds <= 0f || Visible || _session == null || _input == null || _input.IsDrawing ||
                        (_cast != null && (!_cast.enabled || _cast.State != CastState.Idle));
            if (busy || Time.time - _idleSince < idleGuideSeconds)
            {
                if (busy) _idleSince = Time.time;
                return;
            }
            _idleSince = Time.time;
            int count = 0, pick = 0;
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                int id = _session.LocalDeck[i];
                if (id > 0 && Random.Range(0, ++count) == 0) pick = id; // 빈 슬롯을 뺀 카드 중 고르게 하나
            }
            if (pick > 0) Show(pick);
        }
    }
}
