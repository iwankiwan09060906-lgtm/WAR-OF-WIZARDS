// §23.2 인게임 HUD — 게임 이벤트(IGameEvents)만 구독해 그린다. 게임 로직을 직접 읽지 않는다 (Rule 12).
//   · 8슬롯 (2 × 4): 룬 이름, 쿨타임 게이지, 고위력 잔여 횟수, Exhausted · Disabled 표시
//   · 양측 HP, 내 위치 L / C / R, 방어 계층(타워 · 넥서스, 받는 피해 20%/100%, 회복 여부), 진영 배율
//   · 경기 시간 · 다음 강화까지, 다음 웨이브 · Brute 여부, 상태 효과, 시전 거절 사유, AFK 경고, 돌파 알림
//   · 카운트다운 3-2-1 / 결과 (C11 · C13 간이판)
// 아트(ㅇㅎㅅ) · 정식 UI(ㅈㅇㅈ)가 들어오기 전까지 쓰는 코드 생성 HUD. 텍스트는 영문(기본 폰트 호환).

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Presentation;
using SpellboundVR.Spells;
using UnityEngine;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class BattleHudView : MonoBehaviour
    {
        private const float CanvasWidth = 1000f;
        private const float CanvasHeight = 620f;

        [Tooltip("HUD 위치 (부모 = 플레이어 리그 기준)")]
        public Vector3 localPosition = new Vector3(0f, -0.55f, 1.35f);
        public float tiltDegrees = 28f;
        public float worldScale = 0.0011f;

        private struct SlotView
        {
            public Image Background;
            public Text Label;
            public Text Info;
            public RectTransform CooldownBar;
            public SlotState State;
            public float CooldownEnd;
            public float CooldownTotal;
            public int HighPowerUses;
            public int LastShownTenths;
        }

        private ClientSession _session;
        private IGameEvents _events;
        private readonly SlotView[] _slots = new SlotView[DeckData.SlotCount];
        private readonly int[] _hp = new int[2];
        private readonly int[] _maxHp = new int[2];
        private readonly bool[] _tower = { true, true };
        private readonly bool[] _nexus = { true, true };
        private readonly float[] _mult = { 1f, 1f };
        private Text _myHp, _enemyHp, _timer, _defense, _wave, _position, _status, _message, _center, _afk;
        private float _messageUntil;
        private float _centerUntil = -1f;
        private float _shieldEnd;
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

            Panel(rt, "Bg", new Vector2(0f, 0f), new Vector2(CanvasWidth, CanvasHeight), new Color(0.02f, 0.02f, 0.06f, 0.85f));

            _myHp = Label(rt, "MyHp", new Vector2(-330f, 270f), new Vector2(320f, 50f), 30, TextAnchor.MiddleLeft, FallbackVisuals.HomeColor);
            _timer = Label(rt, "Timer", new Vector2(0f, 270f), new Vector2(340f, 50f), 30, TextAnchor.MiddleCenter, Color.white);
            _enemyHp = Label(rt, "EnemyHp", new Vector2(330f, 270f), new Vector2(320f, 50f), 30, TextAnchor.MiddleRight, FallbackVisuals.AwayColor);
            _defense = Label(rt, "Defense", new Vector2(0f, 222f), new Vector2(960f, 40f), 22, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 1f));
            _wave = Label(rt, "Wave", new Vector2(-250f, 182f), new Vector2(460f, 36f), 22, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.6f));
            _position = Label(rt, "Position", new Vector2(120f, 182f), new Vector2(200f, 36f), 22, TextAnchor.MiddleCenter, Color.white);
            _status = Label(rt, "Status", new Vector2(370f, 182f), new Vector2(240f, 36f), 22, TextAnchor.MiddleRight, new Color(0.5f, 0.85f, 1f));

            // 8슬롯 2 × 4 (§13.2)
            float cellW = 228f, cellH = 120f, gap = 10f;
            for (int i = 0; i < DeckData.SlotCount; i++)
            {
                int row = i / 4;
                int col = i % 4;
                var pos = new Vector2(-1.5f * (cellW + gap) + col * (cellW + gap), 70f - row * (cellH + gap));
                var bg = Panel(rt, "Slot" + (i + 1), pos, new Vector2(cellW, cellH), new Color(0.2f, 0.2f, 0.25f, 0.9f));
                var bar = Panel((RectTransform)bg.transform, "Cooldown", Vector2.zero, new Vector2(cellW, cellH), new Color(0f, 0f, 0f, 0.6f));
                var barRt = (RectTransform)bar.transform;
                barRt.anchorMin = new Vector2(0f, 0f);
                barRt.anchorMax = new Vector2(0f, 1f);
                barRt.pivot = new Vector2(0f, 0.5f);
                barRt.anchoredPosition = Vector2.zero;
                barRt.sizeDelta = new Vector2(0f, 0f);

                int spellId = _session.LocalDeck[i];
                var def = spellId > 0 ? _session.Catalog.Get(spellId) : null;
                string name = def != null ? def.DisplayName : (spellId > 0 ? "?" : "-");
                // 룬 아이콘 (RuneTemplate.Icon, 있으면 슬롯 배경에 은은하게)
                var rune = spellId > 0 && _session.Runes != null ? _session.Runes.Find(spellId) : null;
                if (rune != null && rune.Icon != null)
                {
                    var icon = Panel((RectTransform)bg.transform, "Icon", new Vector2(0f, 6f), new Vector2(cellH - 14f, cellH - 14f), new Color(1f, 1f, 1f, 0.45f));
                    icon.sprite = Sprite.Create(rune.Icon, new Rect(0f, 0f, rune.Icon.width, rune.Icon.height), new Vector2(0.5f, 0.5f));
                    icon.preserveAspect = true;
                }
                var label = Label((RectTransform)bg.transform, "Label", new Vector2(0f, 22f), new Vector2(cellW - 10f, 50f), 26,
                                  TextAnchor.MiddleCenter, def != null ? def.ThemeColor : Color.gray);
                label.text = (i + 1) + ". " + SpellIds.Code(spellId) + "\n" + name;
                label.fontSize = 22;
                var info = Label((RectTransform)bg.transform, "Info", new Vector2(0f, -36f), new Vector2(cellW - 10f, 36f), 22,
                                 TextAnchor.MiddleCenter, Color.white);
                _slots[i] = new SlotView
                {
                    Background = bg,
                    Label = label,
                    Info = info,
                    CooldownBar = barRt,
                    State = SlotState.Disabled,
                    HighPowerUses = -1,
                };
                RefreshSlot(i);
            }

            _message = Label(rt, "Message", new Vector2(0f, -205f), new Vector2(960f, 44f), 26, TextAnchor.MiddleCenter, new Color(1f, 0.6f, 0.4f));
            _afk = Label(rt, "Afk", new Vector2(0f, -250f), new Vector2(960f, 44f), 28, TextAnchor.MiddleCenter, new Color(1f, 0.25f, 0.25f));
            _center = Label(rt, "Center", new Vector2(0f, 60f), new Vector2(960f, 200f), 72, TextAnchor.MiddleCenter, Color.white);
            _center.gameObject.AddComponent<Outline>().effectColor = Color.black;

            _myHp.text = "YOU --";
            _enemyHp.text = "-- ENEMY";
            _timer.text = "WAITING";
            _defense.text = "";
            _wave.text = "";
            _position.text = "";
            _status.text = "";
            _message.text = "";
            _afk.text = "";
            _center.text = "";
        }

        private static Image Panel(RectTransform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private static Text Label(RectTransform parent, string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var t = go.AddComponent<Text>();
            t.font = FallbackVisuals.DefaultFont;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        // ── 이벤트 처리 ───────────────────────────────────────

        private Team Local => _session.Events.HasLocalTeam ? _session.Events.LocalTeam : Team.Home;

        private void HandleHp(Team team, int current, int max)
        {
            _hp[(int)team] = current;
            _maxHp[(int)team] = max;
            RefreshHp();
        }

        private void RefreshHp()
        {
            int me = (int)Local;
            int opp = 1 - me;
            _myHp.text = "YOU " + _hp[me] + "/" + _maxHp[me];
            _enemyHp.text = _hp[opp] + "/" + _maxHp[opp] + " ENEMY";
            _myHp.color = FallbackVisuals.TeamColor(Local);
            _enemyHp.color = FallbackVisuals.TeamColor(TeamUtil.Opponent(Local));
        }

        private void HandlePosition(Team team, Column column)
        {
            if (team == Local) _position.text = "POS  " + PositionString(column);
        }

        private static string PositionString(Column c)
        {
            switch (c)
            {
                case Column.Left: return "[L] C  R";
                case Column.Center: return "L [C] R";
                default: return "L  C [R]";
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
            Color bg;
            string info;
            switch (s.State)
            {
                case SlotState.Ready: bg = new Color(0.15f, 0.35f, 0.2f, 0.95f); info = "READY"; break;
                case SlotState.Cooldown: bg = new Color(0.2f, 0.2f, 0.28f, 0.95f); info = ""; break;
                case SlotState.Exhausted: bg = new Color(0.3f, 0.1f, 0.1f, 0.95f); info = "EXHAUSTED"; break;
                default: bg = new Color(0.12f, 0.12f, 0.12f, 0.9f); info = _session.LocalDeck[i] > 0 ? "DISABLED" : ""; break;
            }
            if (s.HighPowerUses >= 0) info += (info.Length > 0 ? "  " : "") + "x" + s.HighPowerUses;
            s.Background.color = bg;
            s.Info.text = info;
            if (s.State != SlotState.Cooldown) s.CooldownBar.sizeDelta = new Vector2(0f, 0f);
        }

        private void HandleRejected(string reason)
        {
            ShowMessage(reason, new Color(1f, 0.6f, 0.4f));
        }

        private void HandleStatus(int target, string effectId, bool active, float remaining)
        {
            if (target != TeamUtil.PlayerId(Local)) return;
            if (effectId == "S08_Shield") _shieldEnd = active ? Time.time + remaining : 0f;
        }

        private void HandleDefense(Team team, bool towerAlive, bool nexusAlive)
        {
            int t = (int)team;
            bool wasTower = _tower[t];
            bool wasNexus = _nexus[t];
            _tower[t] = towerAlive;
            _nexus[t] = nexusAlive;
            if (wasTower && !towerAlive) ShowMessage((team == Local ? "Your" : "Enemy") + " TOWER destroyed!", new Color(1f, 0.85f, 0.3f));
            if (wasNexus && !nexusAlive) ShowMessage((team == Local ? "Your" : "Enemy") + " NEXUS destroyed!", new Color(1f, 0.5f, 0.2f));
            RefreshDefense();
        }

        private void HandleMultiplier(Team team, float value)
        {
            _mult[(int)team] = value;
            RefreshDefense();
        }

        private void RefreshDefense()
        {
            int me = (int)Local;
            int opp = 1 - me;
            _defense.text = DefenseString("YOU", me) + "      " + DefenseString("ENEMY", opp);
        }

        private string DefenseString(string who, int t)
        {
            string tower = _tower[t] ? "T:O" : "T:X";
            string nexus = _nexus[t] ? "N:O" : "N:X";
            string taken = _tower[t] ? "dmg 20%" : "dmg 100%";
            string regen = _nexus[t] ? "regen" : "no regen";
            return who + " " + tower + " " + nexus + " | " + taken + " | " + regen + " | ATK x" + _mult[t].ToString("0.00");
        }

        private void HandleMatchTime(float seconds)
        {
            _matchTime = seconds;
            RefreshTimer();
        }

        private void HandleStage(int stage)
        {
            if (stage > _stage && stage > 0) ShowMessage("POWER UP! Stage " + stage, new Color(0.6f, 1f, 0.6f));
            _stage = stage;
            RefreshTimer();
        }

        private void RefreshTimer()
        {
            int total = Mathf.FloorToInt(_matchTime);
            float interval = _session.Rules.ScalingInterval;
            int next = interval > 0f ? Mathf.CeilToInt(interval - (_matchTime % interval)) : 0;
            _timer.text = (total / 60).ToString("00") + ":" + (total % 60).ToString("00") +
                          "  S" + _stage + "  up " + (next / 60) + ":" + (next % 60).ToString("00");
        }

        private void HandleWave(float nextIn, bool isBrute)
        {
            _wave.text = "Next wave " + Mathf.CeilToInt(nextIn) + "s" + (isBrute ? "  +BRUTE" : "");
        }

        private void HandleAfk(float secondsLeft)
        {
            _afk.text = secondsLeft < 0f ? "" : "AFK! Forfeit in " + Mathf.CeilToInt(secondsLeft) + "s";
        }

        private void HandleBreach(Team attacker)
        {
            ShowMessage(attacker == Local ? "BREACH! Your units attack the enemy!" : "WARNING: enemy units breaching!", new Color(1f, 0.3f, 0.3f));
        }

        private void HandleMatchStarted(OpponentType opponent)
        {
            ShowCenter("FIGHT!\nvs " + (opponent == OpponentType.Bot ? "BOT" : "HUMAN"), 1.5f);
            _afk.text = "";
        }

        private void HandleMatchEnded(MatchResult result, MatchEndReason reason, float duration)
        {
            string title;
            switch (result)
            {
                case MatchResult.Win: title = "VICTORY"; break;
                case MatchResult.Lose: title = "DEFEAT"; break;
                case MatchResult.Draw: title = "DRAW"; break;
                default: title = "NO CONTEST"; break;
            }
            int t = Mathf.FloorToInt(duration);
            ShowCenter(title + "\n" + reason + "  " + (t / 60) + ":" + (t % 60).ToString("00"), -1f);
        }

        private void HandlePhase(MatchPhase phase, float timeLeft)
        {
            switch (phase)
            {
                case MatchPhase.WaitingForPlayers:
                    ShowCenter("Waiting for opponent...", -1f);
                    _timer.text = "WAITING";
                    break;
                case MatchPhase.Countdown:
                    ShowCenter(Mathf.Max(1, Mathf.CeilToInt(timeLeft)).ToString(), -1f);
                    break;
                case MatchPhase.Ended:
                    if (timeLeft > 0f) _message.text = "Next match in " + Mathf.CeilToInt(timeLeft) + "s";
                    _messageUntil = Time.time + 1.2f;
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
            _centerUntil = seconds > 0f ? Time.time + seconds : float.MaxValue;
        }

        private void Update()
        {
            if (_session == null) return;
            float now = Time.time;

            for (int i = 0; i < _slots.Length; i++)
            {
                ref var s = ref _slots[i];
                if (s.State != SlotState.Cooldown) continue;
                float remaining = Mathf.Max(0f, s.CooldownEnd - now);
                float ratio = s.CooldownTotal > 0f ? remaining / s.CooldownTotal : 0f;
                var parent = (RectTransform)s.CooldownBar.parent;
                s.CooldownBar.sizeDelta = new Vector2(parent.sizeDelta.x * ratio, 0f);
                // 표시 값(0.1초 단위)이 바뀔 때만 문자열 갱신 (§33 Update 문자열 연결 최소화)
                int tenths = Mathf.CeilToInt(remaining * 10f);
                if (tenths != s.LastShownTenths)
                {
                    s.LastShownTenths = tenths;
                    s.Info.text = tenths > 0 ? (tenths / 10) + "." + (tenths % 10) + "s" + (s.HighPowerUses >= 0 ? "  x" + s.HighPowerUses : "") : "";
                }
            }

            int shieldSecond = _shieldEnd > now ? Mathf.CeilToInt(_shieldEnd - now) : -1;
            if (shieldSecond != _lastShieldSecond)
            {
                _lastShieldSecond = shieldSecond;
                _status.text = shieldSecond > 0 ? "SHIELD " + shieldSecond + "s" : "";
            }

            if (_message.text.Length > 0 && now > _messageUntil) _message.text = "";
            if (_center.text.Length > 0 && now > _centerUntil) _center.text = "";
        }
    }
}
