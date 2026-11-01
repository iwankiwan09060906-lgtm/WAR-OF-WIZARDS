// §23.2 인게임 HUD (C12) — 게임 이벤트(IGameEvents)만 구독해 그린다. 게임 로직을 직접 읽지 않는다 (Rule 12).
//   디자인: VR UI 프로토타입(Figma Make, 이식 후 삭제)의 HUDScreen.tsx · SkillHUDScreen.tsx · TargetingScreen.tsx 의 시각 언어
//     · 위: 나(보라) / 상대(빨강) HP 카드 + 에너지 막대 (EnergyBar), 가운데 TIME · STAGE · 강화까지 판 (Wave/Time/Kills 자리)
//     · 둘째 줄: 방어 계층 — 타워 · 넥서스 알약(살아 있음 청록 / 파괴 빨강, TargetingScreen의 상태 색), 받는 피해 · 회복 · 공격 배율
//     · 셋째 줄: 다음 웨이브(+Brute) · 내 위치 L / C / R 칸 · 상태 효과
//     · 8슬롯 (2 × 4, 키보드 1~8 순서): 발동 마법진 그림 + 원형 쿨타임 가림(SkillHUD) + 번호 배지 + 이름 · 상태,
//       고위력은 호박색 테두리 + 남은 횟수 (프로토타입 궁극기 게이지 자리)
//     · 시전 거절 사유 · AFK 경고 · 돌파 알림 · 가운데 큰 글자(카운트다운 · FIGHT · 결과)
//   프로토타입의 마나 · 킬 수 · 궁극기 충전은 이 게임 규칙에 없어서 옮기지 않았다.
//   4인 개인전: 상대 카드 · 상대 방어 대신 FfaHudPanel(세 상대 · 전장 지도), Power Up 칸은 마나 폭풍 타이머
//   AI 코치(BattleCoach) · 입력 상태 칸: HUD 오른쪽 아래

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class BattleHudView : MonoBehaviour
    {
        private const float CanvasWidth = 1000f;
        private const float CanvasHeight = 620f;

        [Tooltip("HUD 위치 (부모 = 플레이어 리그 기준). VR: 눈 아래 약 57° · 1m — 내려다보는 전장(레인)을 가리지 않도록 발판 쪽에 둔다")]
        public Vector3 localPosition = new Vector3(0f, -0.85f, 0.55f);
        [Tooltip("눈을 향하도록 기울임 (≈ atan(아래 / 앞))")]
        public float tiltDegrees = 57f;
        [Tooltip("캔버스 1px = 이 값(m). 0.0006 → 폭 0.6m")]
        public float worldScale = 0.0006f;

        private static readonly Color Dim = new Color(0.55f, 0.5f, 0.7f, 1f);

        private struct SlotView
        {
            public Image Card;
            public Text Name;
            public Text Info;
            public Text Seconds;
            public Image CooldownMask;
            public Image Badge;
            public bool High;
            public bool Empty;
            public SlotState State;
            public float CooldownEnd;
            public float CooldownTotal;
            public int HighPowerUses;
            public int LastShownTenths;
        }

        private struct DefenseView
        {
            public Image TowerPill, NexusPill;
            public Text Tower, Nexus, Detail;
        }

        private ClientSession _session;
        private IGameEvents _events;
        private readonly SlotView[] _slots = new SlotView[DeckData.SlotCount];
        private readonly int[] _hp = new int[TeamUtil.TeamCount];
        private readonly int[] _maxHp = new int[TeamUtil.TeamCount];
        private readonly bool[] _tower = { true, true, true, true };
        private readonly bool[] _nexus = { true, true, true, true };
        /// <summary>개인전 레인 타워 — 무너진 것만 알리려고 지난 상태를 둔다 (진영 × 열)</summary>
        private readonly bool[] _laneTower = { true, true, true, true, true, true, true, true, true, true, true, true };
        private readonly float[] _mult = { 1f, 1f, 1f, 1f };
        private Text _myHp, _enemyHp, _time, _stageText, _upText, _wave, _status, _message, _center, _afk, _coach, _coachTitle;
        private Text _myTitle;
        private FfaHudPanel _ffa;
        /// <summary>관전 중 보고 있는 사람 (null = 내 시점)</summary>
        private Team? _spectated;
        /// <summary>관전 안내 한 줄 — 다른 알림이 사라지면 다시 나온다</summary>
        private string _spectateText;
        private GameObject _coachPanel;
        private RectTransform _myBar, _enemyBar;
        private GameObject _centerBack;
        private readonly Image[] _posCells = new Image[3];
        private readonly Text[] _posLabels = new Text[3];
        private DefenseView _myDefense, _enemyDefense;
        private bool Ffa => !_session.Arena.IsDuel;
        private float _messageUntil;
        private float _centerUntil = -1f;
        private float _shieldEnd;
        private bool _focusOn;
        private bool _reflectOn;
        private int _lastShieldSecond = -1;
        private float _matchTime;
        private int _stage;

        public void Initialize(ClientSession session, Transform parent)
        {
            _session = session;
            _events = session.Events;
            BuildCanvas(parent);
            Subscribe();
        }

        private void OnDestroy()
        {
            if (_events == null) return;
            _events.OnHpChanged -= HandleHp;
            _events.OnPositionChanged -= HandlePosition;
            _events.OnSlotStateChanged -= HandleSlotState;
            _events.OnSlotCooldownStarted -= HandleCooldown;
            _events.OnHighPowerUsesChanged -= HandleHighPower;
            _events.OnCastRejected -= HandleRejected;
            _events.OnStatusEffectChanged -= HandleStatus;
            _events.OnDefenseLayerChanged -= HandleDefense;
            _events.OnAttackMultiplierChanged -= HandleMultiplier;
            _events.OnMatchTimeChanged -= HandleMatchTime;
            _events.OnScalingStageChanged -= HandleStage;
            _events.OnWaveTimerChanged -= HandleWave;
            _events.OnAfkWarning -= HandleAfk;
            _events.OnBreachStarted -= HandleBreach;
            _session.Events.OnPlayerEliminated -= HandleEliminated;
            _events.OnMatchStarted -= HandleMatchStarted;
            _events.OnMatchEnded -= HandleMatchEnded;
            if (_session != null) _session.Events.OnPhaseInfo -= HandlePhase;
        }

        private void Subscribe()
        {
            _events.OnHpChanged += HandleHp;
            _events.OnPositionChanged += HandlePosition;
            _events.OnSlotStateChanged += HandleSlotState;
            _events.OnSlotCooldownStarted += HandleCooldown;
            _events.OnHighPowerUsesChanged += HandleHighPower;
            _events.OnCastRejected += HandleRejected;
            _events.OnStatusEffectChanged += HandleStatus;
            _events.OnDefenseLayerChanged += HandleDefense;
            _events.OnAttackMultiplierChanged += HandleMultiplier;
            _events.OnMatchTimeChanged += HandleMatchTime;
            _events.OnScalingStageChanged += HandleStage;
            _events.OnWaveTimerChanged += HandleWave;
            _events.OnAfkWarning += HandleAfk;
            _events.OnBreachStarted += HandleBreach;
            _session.Events.OnPlayerEliminated += HandleEliminated;
            _events.OnMatchStarted += HandleMatchStarted;
            _events.OnMatchEnded += HandleMatchEnded;
            _session.Events.OnPhaseInfo += HandlePhase;
        }

        // ── 레이아웃 ─────────────────────────────────────────

        private void BuildCanvas(Transform parent)
        {
            var root = new GameObject("BattleHUD", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            root.transform.localRotation = Quaternion.Euler(tiltDegrees, 0f, 0f);
            root.transform.localScale = Vector3.one * worldScale;

            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var scaler = root.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 4f;
            var rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);

            // 바탕: 반투명 짙은 보라 판 + 보라 테두리 + 모서리 ㄱ자
            Sliced(rt, "Bg", Vector2.zero, new Vector2(CanvasWidth, CanvasHeight),
                   UiTheme.Cut(new Color(6 / 255f, 4 / 255f, 20 / 255f, 0.86f), UiTheme.Alpha(UiTheme.Purple, 0.45f), 16, 2));

            // 위: HP 카드 · 시간 판
            _myHp = HpCard(rt, -335f, "You", UiTheme.PurpleLight, UiTheme.Purple, out _myBar, out _myTitle);
            if (!Ffa) _enemyHp = HpCard(rt, 335f, "Enemy", UiTheme.Hex(0xf87171), UiTheme.Enemy, out _enemyBar, out _);
            BuildTimer(rt);

            // 둘째 줄: 방어 계층
            _myDefense = DefenseGroup(rt, -250f, "내 방어");
            if (Ffa) _ffa = new FfaHudPanel(rt, _session);
            else _enemyDefense = DefenseGroup(rt, 250f, "상대 방어");
            BuildCoach(rt);

            // 셋째 줄: 웨이브 · 위치 · 상태
            _wave = Label(rt, "Wave", new Vector2(-330f, 142f), new Vector2(300f, 34f), 20, TextAnchor.MiddleLeft, UiTheme.Amber, true);
            BuildPosition(rt, new Vector2(0f, 142f));
            _status = Label(rt, "Status", new Vector2(330f, 142f), new Vector2(300f, 34f), 20, TextAnchor.MiddleRight, UiTheme.CyanLight, true);

            // 8슬롯 2 × 4 (§13.2)
            float cellW = 232f, cellH = 104f, gap = 10f;
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                int row = i / 4;
                int col = i % 4;
                var pos = new Vector2(-1.5f * (cellW + gap) + col * (cellW + gap), 58f - row * (cellH + gap));
                BuildSlot(rt, i, pos, new Vector2(cellW, cellH));
            }

            _message = Label(rt, "Message", new Vector2(0f, -150f), new Vector2(960f, 40f), 24, TextAnchor.MiddleCenter, UiTheme.Orange, true);
            Glow(_message, UiTheme.Alpha(Color.black, 0.8f));
            _afk = Label(rt, "Afk", new Vector2(0f, -192f), new Vector2(960f, 40f), 26, TextAnchor.MiddleCenter, UiTheme.Enemy, true);
            Glow(_afk, UiTheme.Alpha(Color.black, 0.8f));
            var hint = Label(rt, "Hint", new Vector2(Ffa ? 100f : 0f, -280f), new Vector2(Ffa ? 760f : 960f, 30f), Ffa ? 14 : 16, TextAnchor.MiddleCenter, UiTheme.Alpha(UiTheme.PurpleLight, 0.6f), false);
            hint.text = Ffa ? "왼쪽 · 가운데 · 오른쪽을 가리키면 그쪽 상대를 공격   ·   왼손 휘두르기로 피하기   ·   왼손 메뉴 제스처 = 경기 메뉴"
                            : "핀치 룬 › 주먹 › 검지 2초   ·   손바닥 취소   ·   왼손 휘두르기 이동   ·   왼손 메뉴 제스처 = 경기 메뉴";

            // 가운데 큰 글자 뒤 어두운 띠 (슬롯 위에 겹쳐도 읽히게, 글자가 있을 때만)
            _centerBack = Sliced(rt, "CenterBack", new Vector2(0f, 60f), new Vector2(980f, 190f),
                                 UiTheme.Cut(new Color(4 / 255f, 2 / 255f, 14 / 255f, 0.9f), UiTheme.Alpha(UiTheme.Purple, 0.55f), 14, 2)).gameObject;
            _centerBack.SetActive(false);
            _center = Label(rt, "Center", new Vector2(0f, 60f), new Vector2(960f, 220f), 84, TextAnchor.MiddleCenter, UiTheme.Text, true);
            _center.font = UiTheme.Display;
            var o = _center.gameObject.AddComponent<Outline>();
            o.effectColor = UiTheme.Alpha(UiTheme.Purple, 0.6f);
            o.effectDistance = new Vector2(3f, 3f);
            _center.gameObject.AddComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.9f);

            _myHp.text = "--";
            if (_enemyHp != null) _enemyHp.text = "--";
            _time.text = "대기";
            _stageText.text = "S0";
            _upText.text = "-:--";
            _wave.text = "";
            _status.text = "";
            _message.text = "";
            _afk.text = "";
            _center.text = "";
            RefreshDefense();
        }

        /// <summary>HP 카드 (프로토타입 YOU / ENEMY 카드 + EnergyBar)</summary>
        private static Text HpCard(RectTransform rt, float x, string who, Color text, Color bar, out RectTransform fill, out Text title)
        {
            var card = Sliced(rt, who + "Card", new Vector2(x, 262f), new Vector2(300f, 70f),
                              UiTheme.Cut(new Color(0f, 0f, 0f, 0.45f), UiTheme.Alpha(bar, 0.45f), 0, 2));
            var crt = card.rectTransform;
            var k = UiKit.Kicker(crt, who, text, TextAnchor.MiddleLeft, 15);
            title = k;
            Place(k.rectTransform, new Vector2(-55f, 14f), new Vector2(170f, 26f));
            var hp = Label(crt, "Hp", new Vector2(60f, 14f), new Vector2(160f, 30f), 22, TextAnchor.MiddleRight, UiTheme.Text, true);
            hp.font = UiTheme.Display;
            // 막대: 어두운 바탕 + 색 채움 + 위쪽 반짝임
            var track = Box(crt, "Track", new Vector2(0f, -16f), new Vector2(270f, 12f), UiTheme.Alpha(bar, 0.12f));
            var frt = Box(track.rectTransform, "Fill", Vector2.zero, Vector2.zero, bar).rectTransform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = new Vector2(1f, 1f);
            frt.pivot = new Vector2(0f, 0.5f);
            frt.offsetMin = frt.offsetMax = Vector2.zero;
            UiGradient.Apply(frt.GetComponent<Image>(), UiTheme.Alpha(bar, 0.65f), bar, Color.Lerp(bar, Color.white, 0.25f));
            var shine = Box(track.rectTransform, "Shine", Vector2.zero, Vector2.zero, new Color(1f, 1f, 1f, 0.12f)).rectTransform;
            shine.anchorMin = new Vector2(0f, 0.66f);
            shine.anchorMax = Vector2.one;
            shine.offsetMin = shine.offsetMax = Vector2.zero;
            fill = frt;
            return hp;
        }

        private void BuildTimer(RectTransform rt)
        {
            Sliced(rt, "TimerPanel", new Vector2(0f, 262f), new Vector2(320f, 70f),
                   UiTheme.Cut(new Color(0f, 0f, 0f, 0.55f), UiTheme.Alpha(UiTheme.Purple, 0.45f), 10, 2));
            _time = TimerCell(rt, -100f, "Time", UiTheme.CyanLight);
            _stageText = TimerCell(rt, 0f, "Stage", UiTheme.PurplePale);
            _upText = TimerCell(rt, 100f, Ffa ? "Storm" : "Power Up", UiTheme.Amber);
        }

        /// <summary>AI 코치 · 입력 상태 — 말할 것이 있을 때만 보인다</summary>
        private void BuildCoach(RectTransform rt)
        {
            var panel = Sliced(rt, "Coach", new Vector2(275f, -202f), new Vector2(430f, 124f),
                               UiTheme.Cut(new Color(0f, 0f, 0f, 0.55f), UiTheme.Alpha(UiTheme.Cyan, 0.45f), 8, 2));
            _coachPanel = panel.gameObject;
            var crt = panel.rectTransform;
            _coachTitle = Label(crt, "Title", new Vector2(0f, 46f), new Vector2(404f, 26f), 15, TextAnchor.MiddleLeft, UiTheme.CyanLight, true);
            _coachTitle.text = "AI 코치";
            _coach = Label(crt, "Text", new Vector2(0f, -14f), new Vector2(404f, 88f), 16, TextAnchor.UpperLeft, UiTheme.Text, false);
            _coach.horizontalOverflow = HorizontalWrapMode.Wrap;
            _coachPanel.SetActive(false);
        }

        public void ShowCoach(string text)
        {
            if (_coach == null) return;
            _coachTitle.text = "AI 코치";
            _coach.text = text;
            _coachPanel.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>입력 장치 안내 (웹캠 손 추적 연결 상태)</summary>
        public void ShowInputStatus(string text)
        {
            ShowMessage(text, UiTheme.CyanLight);
        }

        private static Text TimerCell(RectTransform rt, float x, string title, Color color)
        {
            var k = UiKit.Kicker(rt, title, UiTheme.Alpha(UiTheme.PurpleLight, 0.75f), TextAnchor.MiddleCenter, 11);
            Place(k.rectTransform, new Vector2(x, 282f), new Vector2(100f, 20f));
            var v = Label(rt, title, new Vector2(x, 252f), new Vector2(100f, 36f), 26, TextAnchor.MiddleCenter, color, true);
            v.font = UiTheme.Display;
            return v;
        }

        private static DefenseView DefenseGroup(RectTransform rt, float x, string title)
        {
            var v = new DefenseView();
            var t = Label(rt, title, new Vector2(x - 190f, 194f), new Vector2(100f, 30f), 17, TextAnchor.MiddleLeft, UiTheme.PurpleLight, true);
            t.text = title;
            v.TowerPill = Pill(rt, new Vector2(x - 95f, 194f), out v.Tower);
            v.NexusPill = Pill(rt, new Vector2(x - 10f, 194f), out v.Nexus);
            v.Tower.text = "타워";
            v.Nexus.text = "넥서스";
            v.Detail = Label(rt, title + "Detail", new Vector2(x + 140f, 194f), new Vector2(210f, 30f), 15, TextAnchor.MiddleLeft, UiTheme.Text2, false);
            return v;
        }

        private static Image Pill(RectTransform rt, Vector2 pos, out Text label)
        {
            var img = Sliced(rt, "Pill", pos, new Vector2(78f, 28f), UiTheme.Cut(new Color(1f, 1f, 1f, 0.16f), Color.white, 0, 2));
            label = Label(img.rectTransform, "Label", Vector2.zero, new Vector2(78f, 28f), 15, TextAnchor.MiddleCenter, Color.white, true);
            return img;
        }

        private void BuildPosition(RectTransform rt, Vector2 pos)
        {
            string[] names = { "L", "C", "R" };
            for (int i = 0; i < 3; i++)
            {
                var cell = Sliced(rt, "Pos" + names[i], pos + new Vector2((i - 1) * 50f, 0f), new Vector2(44f, 32f),
                                  UiTheme.Cut(new Color(1f, 1f, 1f, 0.12f), Color.white, 0, 2));
                _posCells[i] = cell;
                _posLabels[i] = Label(cell.rectTransform, "L", Vector2.zero, new Vector2(44f, 32f), 18, TextAnchor.MiddleCenter, Dim, true);
                _posLabels[i].font = UiTheme.Display;
                _posLabels[i].text = names[i];
            }
            PaintPosition(-1);
        }

        private void BuildSlot(RectTransform rt, int i, Vector2 pos, Vector2 size)
        {
            int spellId = _session.LocalDeck[i];
            var def = spellId > 0 ? _session.Catalog.Get(spellId) : null;
            string code = spellId > 0 ? SpellIds.Code(spellId) : "";
            var card = Sliced(rt, "Slot" + (i + 1), pos, size, null);
            var crt = card.rectTransform;

            // 발동 마법진 (원 안으로 잘림) + 원형 쿨타임 가림
            float icon = size.y - 22f;
            var art = string.IsNullOrEmpty(code) ? null : UiKit.SkillIcon(crt, code, icon);
            Image mask = null;
            Text sec = null;
            if (art != null)
            {
                Place(art, new Vector2(-size.x / 2f + 12f + icon / 2f, 0f), new Vector2(icon, icon));
                mask = UiKit.Panel(art, new Color(0.02f, 0.01f, 0.06f, 0.72f), null, "Cooldown");
                mask.sprite = UiTheme.Circle();
                mask.type = UnityEngine.UI.Image.Type.Filled;
                mask.fillMethod = UnityEngine.UI.Image.FillMethod.Radial360;
                mask.fillOrigin = (int)UnityEngine.UI.Image.Origin360.Top;
                mask.fillClockwise = false;
                mask.fillAmount = 0f;
                UiKit.Stretch(mask.rectTransform);
                sec = Label(art, "Seconds", Vector2.zero, new Vector2(icon, icon), 24, TextAnchor.MiddleCenter, Color.white, true);
                sec.font = UiTheme.Display;
                sec.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.9f);
            }

            // 번호 배지 (키보드 1~8)
            var badge = Box(crt, "Key", new Vector2(-size.x / 2f + 14f, size.y / 2f - 14f), new Vector2(26f, 26f), UiTheme.Purple);
            badge.sprite = UiTheme.Circle();
            var key = Label(badge.rectTransform, "N", Vector2.zero, new Vector2(26f, 26f), 15, TextAnchor.MiddleCenter, Color.white, true);
            key.font = UiTheme.Display;
            key.text = (i + 1).ToString();

            float textX = -size.x / 2f + icon + 24f;
            float textW = size.x - icon - 32f;
            string display = string.IsNullOrEmpty(code) ? "-" : MetaSkills.TryGet(code, out var info) ? info.Name : def != null ? def.DisplayName : code;
            var name = Label(crt, "Name", new Vector2(textX + textW / 2f, 18f), new Vector2(textW, 34f), 23, TextAnchor.MiddleLeft, UiTheme.Text, true);
            name.text = display;
            var infoText = Label(crt, "Info", new Vector2(textX + textW / 2f, -20f), new Vector2(textW, 30f), 16, TextAnchor.MiddleLeft, UiTheme.Text2, true);

            _slots[i] = new SlotView
            {
                Card = card,
                Name = name,
                Info = infoText,
                Seconds = sec,
                CooldownMask = mask,
                Badge = badge,
                High = MetaSkills.IsHigh(code),
                Empty = string.IsNullOrEmpty(code),
                State = SlotState.Disabled,
                HighPowerUses = -1,
            };
            RefreshSlot(i);
        }

        // ── 만들기 도우미 ─────────────────────────────────────

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        private static Image Box(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var img = UiKit.Panel(parent, color, null, name);
            Place(img.rectTransform, pos, size);
            return img;
        }

        private static Image Sliced(RectTransform parent, string name, Vector2 pos, Vector2 size, Sprite sprite)
        {
            var img = UiKit.Panel(parent, Color.white, null, name);
            img.sprite = sprite;
            img.type = UnityEngine.UI.Image.Type.Sliced;
            Place(img.rectTransform, pos, size);
            return img;
        }

        private static Text Label(RectTransform parent, string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor, Color color, bool bold)
        {
            var t = UiKit.Label(parent, "", fontSize, color, bold, anchor, name);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            Place(t.rectTransform, pos, size);
            return t;
        }

        private static void Glow(Text t, Color color)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = color;
            o.effectDistance = new Vector2(2f, 2f);
        }

        // ── 이벤트 처리 ───────────────────────────────────────

        private Team Local => _session.Events.HasLocalTeam ? _session.Events.LocalTeam : Team.Home;

        /// <summary>HUD 가 보여 주는 사람 — 평소엔 나, 관전 중엔 보고 있는 사람</summary>
        private Team Viewed => _spectated ?? Local;

        private void HandleHp(Team team, int current, int max)
        {
            _hp[(int)team] = current;
            _maxHp[(int)team] = max;
            RefreshHp();
        }

        private void RefreshHp()
        {
            int me = (int)Viewed;
            _myHp.text = _hp[me] + " / " + _maxHp[me];
            SetBar(_myBar, _maxHp[me] > 0 ? (float)_hp[me] / _maxHp[me] : 0f);
            if (_enemyHp == null) return;
            int opp = (int)TeamUtil.Opponent(Local);
            _enemyHp.text = _hp[opp] + " / " + _maxHp[opp];
            SetBar(_enemyBar, _maxHp[opp] > 0 ? (float)_hp[opp] / _maxHp[opp] : 0f);
        }

        private static void SetBar(RectTransform fill, float ratio)
        {
            fill.anchorMax = new Vector2(Mathf.Clamp01(ratio), 1f);
            fill.offsetMax = Vector2.zero;
        }

        private void HandlePosition(Team team, Column column)
        {
            if (_spectated.HasValue) RefreshSpectatedPosition();
            else if (team == Local) PaintPosition(column == Column.Left ? 0 : column == Column.Center ? 1 : 2);
        }

        /// <summary>OnPositionChanged 의 열은 내 시점이라, 관전 중에는 보고 있는 사람 시점으로 다시 구한다</summary>
        private void RefreshSpectatedPosition()
        {
            var s = _session.Events.Latest;
            if (s != null) PaintPosition((int)TeamUtil.ToLocalColumn(Viewed, s.Players[(int)Viewed].WorldColumn));
        }

        private void PaintPosition(int index)
        {
            for (int i = 0; i < 3; i++)
            {
                bool on = i == index;
                _posCells[i].color = on ? UiTheme.Cyan : UiTheme.Alpha(UiTheme.Purple, 0.6f);
                _posLabels[i].color = on ? Color.white : Dim;
            }
        }

        private void HandleSlotState(int slot, SlotState state)
        {
            if (slot < 0 || slot >= _slots.Length) return;
            _slots[slot].State = state;
            RefreshSlot(slot);
        }

        private void HandleCooldown(int slot, float duration)
        {
            if (slot < 0 || slot >= _slots.Length) return;
            _slots[slot].CooldownEnd = Time.time + duration;
            _slots[slot].CooldownTotal = duration;
            _slots[slot].LastShownTenths = -1;
        }

        private void HandleHighPower(int slot, int remaining)
        {
            if (slot < 0 || slot >= _slots.Length) return;
            _slots[slot].HighPowerUses = remaining;
            RefreshSlot(slot);
        }

        private void RefreshSlot(int i)
        {
            ref var s = ref _slots[i];
            Color fill, border, nameColor;
            string info;
            switch (s.State)
            {
                case SlotState.Ready:
                    fill = s.High ? new Color(0.35f, 0.24f, 0.04f, 0.45f) : new Color(0.30f, 0.14f, 0.62f, 0.45f);
                    border = s.High ? UiTheme.Amber : UiTheme.Alpha(UiTheme.PurpleLight, 0.9f);
                    nameColor = UiTheme.Text;
                    info = "<color=" + UiKit.Hex(UiTheme.CyanLight) + ">READY</color>";
                    break;
                case SlotState.Cooldown:
                    fill = new Color(0.06f, 0.04f, 0.16f, 0.7f);
                    border = UiTheme.Alpha(s.High ? UiTheme.Amber : UiTheme.Purple, 0.4f);
                    nameColor = UiTheme.Text2;
                    info = "쿨타임";
                    break;
                case SlotState.Exhausted:
                    fill = new Color(0.25f, 0.04f, 0.06f, 0.6f);
                    border = UiTheme.Alpha(UiTheme.Enemy, 0.6f);
                    nameColor = Dim;
                    info = "<color=" + UiKit.Hex(UiTheme.Enemy) + ">소진</color>";
                    break;
                default:
                    fill = new Color(0.05f, 0.05f, 0.08f, 0.6f);
                    border = UiTheme.Alpha(Dim, 0.3f);
                    nameColor = Dim;
                    info = s.Empty ? "" : "비활성";
                    break;
            }
            if (s.HighPowerUses >= 0) info += (info.Length > 0 ? "  ·  " : "") + "<color=" + UiKit.Hex(UiTheme.Amber) + ">남은 " + s.HighPowerUses + "회</color>";
            s.Card.sprite = UiTheme.Cut(fill, border, s.State == SlotState.Ready ? 10 : 0, 2);
            s.Name.color = nameColor;
            s.Info.text = info;
            s.Badge.color = s.State == SlotState.Ready ? (s.High ? UiTheme.Amber : UiTheme.Purple) : UiTheme.Alpha(Dim, 0.5f);
            if (s.State != SlotState.Cooldown)
            {
                if (s.CooldownMask != null) s.CooldownMask.fillAmount = s.State == SlotState.Ready ? 0f : 1f;
                if (s.Seconds != null) s.Seconds.text = "";
            }
        }

        private void HandleRejected(string reason)
        {
            ShowMessage(reason, UiTheme.Orange);
        }

        private void HandleStatus(int target, string effectId, bool active, float remaining)
        {
            if (target != TeamUtil.PlayerId(Local)) return;
            if (effectId == "S08_Shield") _shieldEnd = active ? Time.time + remaining : 0f;
            else if (effectId == "S10_Focus") _focusOn = active;
            else if (effectId == "H04_Reflect") _reflectOn = active;
            _lastShieldSecond = int.MinValue; // 상태 글자 다시 그리기
        }

        private void HandleDefense(Team team, bool towerAlive, bool nexusAlive)
        {
            int t = (int)team;
            bool wasTower = _tower[t];
            bool wasNexus = _nexus[t];
            _tower[t] = towerAlive;
            _nexus[t] = nexusAlive;
            string who = Ffa ? TeamUtil.SeatNumber(team) + "번 " : "상대 ";
            if (Ffa) AnnounceLaneTowers(team);
            else if (wasTower && !towerAlive) ShowMessage(team == Local ? "내 타워가 무너졌습니다!" : who + "타워 파괴!", UiTheme.Amber);
            var s = _session.Events.Latest;
            bool eliminated = s != null && s.Players[t].Place != 0; // 탈락 알림을 덮지 않는다
            if (wasNexus && !nexusAlive && !eliminated) ShowMessage(team == Local ? "내 넥서스가 무너졌습니다!" : who + "넥서스 파괴!", UiTheme.Orange);
            RefreshDefense();
        }

        /// <summary>개인전: 무너진 레인 타워 알림 — 어느 상대와의 레인이 뚫렸는지까지 말한다</summary>
        private void AnnounceLaneTowers(Team team)
        {
            var s = _session.Events.Latest;
            if (s == null) return;
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                int index = (int)team * TeamUtil.ColumnCount + c;
                bool alive = s.Tower(team, (Column)c).Alive != 0;
                bool was = _laneTower[index];
                _laneTower[index] = alive;
                if (!was || alive || s.Players[(int)team].Place != 0) continue;
                Team toward = _session.Arena.ColumnTarget(team, (Column)c);
                if (team == Local) ShowMessage("내 타워 파괴 — " + TeamUtil.SeatNumber(toward) + "번 공격이 100% 들어옵니다!", UiTheme.Enemy);
                else if (toward == Local) ShowMessage(TeamUtil.SeatNumber(team) + "번 타워 파괴! 내 공격이 100% 들어갑니다", UiTheme.Amber);
                else ShowMessage(TeamUtil.SeatNumber(team) + "번 타워 파괴!", UiTheme.Amber);
            }
        }

        private void HandleMultiplier(Team team, float value)
        {
            _mult[(int)team] = value;
            RefreshDefense();
        }

        private void RefreshDefense()
        {
            int me = (int)Viewed;
            PaintDefense(_myDefense, me);
            if (!Ffa) PaintDefense(_enemyDefense, (int)TeamUtil.Opponent(Local));
        }

        /// <summary>개인전 받는 스킬 피해 — 왼쪽 · 맞은편 · 오른쪽 상대별 (그 레인 타워가 무너지면 100%)</summary>
        private string LaneDamageText(MatchSnapshot s, Team team, int ratio)
        {
            var sb = new System.Text.StringBuilder(64);
            for (int c = 0; c < TeamUtil.ColumnCount; c++)
            {
                bool tower = s.Tower(team, (Column)c).Alive != 0;
                sb.Append(c == 0 ? "◀" : c == 1 ? " ▲" : " ▶");
                if (tower) sb.Append(ratio);
                else sb.Append("<color=").Append(UiKit.Hex(UiTheme.Enemy)).Append(">100</color>");
            }
            return sb.Append('%').ToString();
        }

        private void PaintDefense(DefenseView v, int t)
        {
            if (v.Detail == null) return;
            v.TowerPill.color = _tower[t] ? UiTheme.Cyan : UiTheme.Enemy;
            v.Tower.color = _tower[t] ? UiTheme.Hex(0xcffafe) : UiTheme.Hex(0xfecaca);
            v.NexusPill.color = _nexus[t] ? UiTheme.Cyan : UiTheme.Enemy;
            v.Nexus.color = _nexus[t] ? UiTheme.Hex(0xcffafe) : UiTheme.Hex(0xfecaca);
            // 타워가 서 있으면 본체는 줄어든 피해만, 넥서스가 서 있으면 HP 회복
            int ratio = Mathf.RoundToInt(_session.Rules.TowerAliveSkillDamageRatio * 100f);
            var s = _session.Events.Latest;
            if (Ffa && s != null)
            {
                v.Tower.text = "타워 " + s.TowersAlive((Team)t) + "/3";
                v.Detail.text = LaneDamageText(s, (Team)t, ratio) + " · " + (_nexus[t] ? "회복" : "회복 없음");
                return;
            }
            v.Detail.text = "피해 " + (_tower[t] ? ratio + "%" : "<color=" + UiKit.Hex(UiTheme.Enemy) + ">100%</color>") +
                            " · " + (_nexus[t] ? "회복" : "회복 없음") + " · 공격 x" + _mult[t].ToString("0.00");
        }

        private void HandleMatchTime(float seconds)
        {
            _matchTime = seconds;
            RefreshTimer();
        }

        private void HandleStage(int stage)
        {
            if (stage > _stage && stage > 0) ShowMessage("강화! " + stage + "단계 — 모든 공격이 세집니다", UiTheme.Green);
            _stage = stage;
            RefreshTimer();
        }

        private void RefreshTimer()
        {
            int total = Mathf.FloorToInt(_matchTime);
            float interval = _session.Rules.ScalingInterval;
            int next = interval > 0f ? Mathf.CeilToInt(interval - (_matchTime % interval)) : 0;
            _time.text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
            _stageText.text = "S" + _stage;
            _upText.text = (next / 60) + ":" + (next % 60).ToString("00");
            if (!Ffa) return;
            var s = _session.Events.Latest;
            if (s == null) return;
            int storm = Mathf.CeilToInt(s.Header.StormIn);
            _upText.text = storm > 0 ? (storm / 60) + ":" + (storm % 60).ToString("00") : "폭풍!";
            if (storm == 30) ShowMessage("30초 뒤 마나 폭풍 — 구조물과 마법사가 깎입니다", UiTheme.Amber);
            if (storm == 0 && !_stormAnnounced)
            {
                _stormAnnounced = true;
                ShowMessage("마나 폭풍 시작!", UiTheme.Enemy);
            }
        }

        private bool _stormAnnounced;

        /// <summary>관전 중 안내 — 큰 "N위 탈락" 글자는 잠깐만 두고, 아래에 누구를 보고 있는지 계속 보여 준다.
        /// target: HUD 가 보여 줄 사람 (null 이면 지금 사람 유지 — 경기가 끝났을 때)</summary>
        public void SetSpectating(Team? target, string text)
        {
            if (_message == null) return;
            if (_centerUntil == float.MaxValue) _centerUntil = Time.time + 2.5f;
            ShowCoach("");
            if (target.HasValue)
            {
                _spectated = target;
                _myTitle.text = TeamUtil.SeatNumber(target.Value) + "번";
                RefreshHp();
                RefreshDefense();
                RefreshSpectatedPosition();
            }
            _spectateText = text;
            _message.text = text;
            _message.color = UiTheme.CyanLight;
            _messageUntil = float.MaxValue;
        }

        private void HandleEliminated(Team team, int place)
        {
            if (team == Local) return; // 내 탈락은 경기 종료 글자로 나온다
            ShowMessage(TeamUtil.SeatNumber(team) + "번 탈락 — " + place + "위", UiTheme.Amber);
        }

        private void HandleWave(float nextIn, bool isBrute)
        {
            _wave.text = "다음 웨이브 " + Mathf.CeilToInt(nextIn) + "초" + (isBrute ? "  +브루트" : "");
        }

        private void HandleAfk(float secondsLeft)
        {
            _afk.text = secondsLeft < 0f ? "" : "움직임이 없습니다!  " + Mathf.CeilToInt(secondsLeft) + "초 뒤 기권 처리";
        }

        private void HandleBreach(Team attacker)
        {
            ShowMessage(attacker == Local ? "돌파! 아군 유닛이 상대 마법사를 공격합니다" : "경고: 적 유닛이 돌파했습니다!", UiTheme.Enemy);
        }

        private void HandleMatchStarted(OpponentType opponent)
        {
            ShowCenter("FIGHT!", 1.5f);
            ShowMessage(Ffa ? "마지막까지 살아남으세요!" : opponent == OpponentType.Bot ? "상대: 봇" : "상대: 사람", UiTheme.CyanLight);
            _afk.text = "";
        }

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            string title;
            switch (result)
            {
                case MatchResult.Win: title = "VICTORY"; break;
                case MatchResult.Lose: title = "DEFEATED"; break;
                case MatchResult.Draw: title = "DRAW"; break;
                default: title = "NO CONTEST"; break;
            }
            _center.color = result == MatchResult.Lose ? UiTheme.Enemy : result == MatchResult.Win ? UiTheme.PurplePale : UiTheme.Text;
            if (Ffa && result == MatchResult.Lose) title = _session.Events.LocalPlace + "위 탈락";
            ShowCenter(title, -1f);
            int t = Mathf.FloorToInt(duration);
            _message.text = ReasonText(reason) + "  ·  " + (t / 60) + ":" + (t % 60).ToString("00");
            _message.color = UiTheme.Text2;
            _messageUntil = float.MaxValue;
        }

        private static string ReasonText(MatchEndReason reason)
        {
            switch (reason)
            {
                case MatchEndReason.PlayerDeath: return "마법사 쓰러짐";
                case MatchEndReason.Afk: return "장시간 무활동";
                case MatchEndReason.Disconnect: return "연결 끊김";
                case MatchEndReason.Draw: return "무승부";
                default: return "무효 경기";
            }
        }

        private void HandlePhase(MatchPhase phase, float timeLeft)
        {
            switch (phase)
            {
                case MatchPhase.WaitingForPlayers:
                    _center.font = UiTheme.Bold;
                    _center.fontSize = 44;
                    ShowCenter(Ffa ? "자리가 차기를 기다리는 중…" : "상대를 기다리는 중…", -1f);
                    _time.text = "대기";
                    break;
                case MatchPhase.Countdown:
                    _center.font = UiTheme.Display;
                    _center.fontSize = 84;
                    _center.color = UiTheme.Text;
                    ShowCenter(Mathf.Max(1, Mathf.CeilToInt(timeLeft)).ToString(), -1f);
                    break;
                case MatchPhase.Playing:
                    _center.font = UiTheme.Display;
                    _center.fontSize = 84;
                    break;
                case MatchPhase.Ended:
                    if (timeLeft > 0f) _afk.text = "다음 경기까지 " + Mathf.CeilToInt(timeLeft) + "초";
                    break;
            }
        }

        private void ShowMessage(string text, Color color)
        {
            _message.text = text;
            _message.color = color;
            _messageUntil = Time.time + 2.5f;
        }

        private void ShowCenter(string text, float seconds)
        {
            _center.text = text;
            _centerBack.SetActive(!string.IsNullOrEmpty(text));
            _centerUntil = seconds > 0f ? Time.time + seconds : float.MaxValue;
        }

        private void Update()
        {
            if (_session == null) return;
            float now = Time.time;
            if (_ffa != null) _ffa.Tick(_session.Events.Latest, Viewed);

            for (int i = 0; i < _slots.Length; i++)
            {
                ref var s = ref _slots[i];
                if (s.State != SlotState.Cooldown) continue;
                float remaining = Mathf.Max(0f, s.CooldownEnd - now);
                float ratio = s.CooldownTotal > 0f ? remaining / s.CooldownTotal : 0f;
                if (s.CooldownMask != null) s.CooldownMask.fillAmount = ratio;
                // 표시 값(0.1초 단위)이 바뀔 때만 문자열 갱신 (§33 Update 문자열 연결 최소화)
                int tenths = Mathf.CeilToInt(remaining * 10f);
                if (tenths != s.LastShownTenths && s.Seconds != null)
                {
                    s.LastShownTenths = tenths;
                    s.Seconds.text = tenths > 0 ? (tenths >= 100 ? (tenths / 10).ToString() : (tenths / 10) + "." + (tenths % 10)) : "";
                }
            }

            int shieldSecond = _shieldEnd > now ? Mathf.CeilToInt(_shieldEnd - now) : -1;
            if (shieldSecond != _lastShieldSecond)
            {
                _lastShieldSecond = shieldSecond;
                string status = shieldSecond > 0 ? "쉴드 " + shieldSecond + "초" : "";
                if (_reflectOn) status += (status.Length > 0 ? "  ·  " : "") + "반사 결계";
                if (_focusOn) status += (status.Length > 0 ? "  ·  " : "") + "집중 x1.5";
                _status.text = status;
            }

            if (_message.text.Length > 0 && now > _messageUntil)
            {
                _message.text = _spectateText ?? "";
                _message.color = UiTheme.CyanLight;
                _messageUntil = float.MaxValue;
            }
            if (_center.text.Length > 0 && now > _centerUntil)
            {
                _center.text = "";
                _centerBack.SetActive(false);
            }
        }
    }
}
