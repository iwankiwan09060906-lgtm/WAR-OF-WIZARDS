// 전시 시연 연습 모드 — 접속하면 먼저 스킬 3개를 받아쓰기처럼 하나씩 따라 한다 (이 기기 안 로컬 경기, 서 있는 AI 과녁).
//   스킬마다: ① 눈앞 룬 실루엣(아래에 그리는 법 설명)을 핀치로 따라 그리기 → ② 주먹(준비) → ③ 검지로 가리켜 조준(선이 생김) → ④ 3초 유지 = 시전
//   지금 단계는 CastStateMachine 상태 · 서버 확정(OnLocalCastAccepted)으로 넘어간다. 틀리면 그 단계 안내를 다시 보여 준다.
//   3개를 다 하면 onComplete (GameBootstrap 이 서버 경기로 들어간다). PC 에서는 N 키로 건너뛴다.

using System;
using SpellboundVR.Core;
using SpellboundVR.Input;
using SpellboundVR.Spells;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SpellboundVR.UI
{
    public sealed class PracticeGuide : MonoBehaviour
    {
        /// <summary>연습할 스킬 3개 — 직접 공격(투사체) · 범위 · 소환 (덱 슬롯 0 ~ 2 에 넣는다)</summary>
        public static readonly int[] Skills = { SpellIds.S03_Fireball, SpellIds.S04_ArrowRain, SpellIds.S11_AxeWarrior };

        [Tooltip("안내 판 위치 (부모 = 플레이어 리그, 시작 시 머리 높이 기준) — 눈높이 위쪽")]
        public Vector3 localPosition = new Vector3(0f, 0.3f, 1.1f);
        public float worldScale = 0.0008f;

        private enum Step { Draw, Fist, Point, Hold, Done }

        private ClientSession _session;
        private CastStateMachine _cast;
        private ISpellInputSource _input;
        private SkillGuideView _guide;
        private Action _onComplete;
        private Text _title, _body;
        private int _index;
        private Step _step;
        private float _nextAt = -1f;
        private bool _finished;
        private bool _keyboard;

        public void Initialize(ClientSession session, CastStateMachine cast, ISpellInputSource input, SkillGuideView guide, Transform parent, Action onComplete)
        {
            _session = session;
            _cast = cast;
            _input = input;
            _guide = guide;
            _onComplete = onComplete;
            _keyboard = input is KeyboardInput;
            Build(parent);
            _cast.OnStateChanged += HandleState;
            _cast.OnRuneRecognized += HandleRecognized;
            _cast.OnRecognitionFailed += HandleFailed;
            _session.Events.OnLocalCastAccepted += HandleAccepted;
            Begin(0);
        }

        private void OnDestroy()
        {
            if (_cast != null)
            {
                _cast.OnStateChanged -= HandleState;
                _cast.OnRuneRecognized -= HandleRecognized;
                _cast.OnRecognitionFailed -= HandleFailed;
            }
            if (_session != null) _session.Events.OnLocalCastAccepted -= HandleAccepted;
        }

        private void Build(Transform parent)
        {
            var go = new GameObject("PracticeGuide", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * worldScale;
            go.AddComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 1.5f;
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(900f, 230f);
            var bg = UiKit.Panel(rt, Color.white, UiTheme.Cut(new Color(6 / 255f, 4 / 255f, 20 / 255f, 0.85f), UiTheme.Alpha(UiTheme.Cyan, 0.6f), 16, 2), "Bg");
            bg.type = Image.Type.Sliced;
            UiKit.Stretch(bg.rectTransform);
            _title = Line(rt, new Vector2(0f, 70f), 38, UiTheme.CyanLight);
            _body = Line(rt, new Vector2(0f, -30f), 32, UiTheme.Text);
            _body.rectTransform.sizeDelta = new Vector2(860f, 150f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        private static Text Line(RectTransform parent, Vector2 pos, int size, Color color)
        {
            var t = UiKit.Label(parent, "", size, color, true, TextAnchor.MiddleCenter);
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(860f, 60f);
            return t;
        }

        private int Target => Skills[_index];

        private string SkillName(int spellId)
        {
            string code = SpellIds.Code(spellId);
            var def = _session.Catalog.Get(spellId);
            return MetaSkills.TryGet(code, out var info) ? info.Name : def != null ? def.DisplayName : code;
        }

        private void Begin(int index)
        {
            _index = index;
            _nextAt = -1f;
            _title.text = $"연습 {index + 1} / {Skills.Length}  ·  {SkillName(Target)}";
            SetStep(Step.Draw);
        }

        private void SetStep(Step step, string extra = null)
        {
            _step = step;
            switch (step)
            {
                case Step.Draw:
                {
                    // 그리는 법은 실루엣 바로 아래에 — 위 판은 지금 단계만 짧게
                    _body.text = "① 아래 실루엣을 따라 그리세요";
                    if (extra != null) _body.text = extra + "\n" + _body.text;
                    string how = _keyboard
                        ? SkillGuideView.HowToDraw(Target) + "\n마우스 왼쪽 드래그로 그려요 (또는 숫자 " + (_index + 1) + " 키)"
                        : SkillGuideView.HowToDraw(Target) + "\n오른손 엄지와 검지를 붙인 채(핀치) 그려요.";
                    _guide.Show(Target, how, 0f);
                    break;
                }
                case Step.Fist:
                    _body.text = _keyboard ? "② F 키로 마법을 준비하세요" : "② 주먹을 꽉 쥐어 마법을 준비하세요";
                    _guide.SetHint(_keyboard ? "다 그렸으면 F 키!" : "다 그렸으면 주먹을 꽉 쥐세요!");
                    break;
                case Step.Point:
                    _guide.Hide();
                    _body.text = _keyboard ? "③ G 키 다음 마우스로 공격할 곳을 가리키세요" : "③ 검지 하나만 펴서 공격할 곳을 가리키세요\n가리킨 쪽으로 조준선이 생깁니다";
                    break;
                case Step.Hold:
                    _body.text = "④ 그대로 3초 동안 가리키고 있으면 발사됩니다!";
                    break;
                case Step.Done:
                    _guide.Hide();
                    _body.text = "<color=#7CF5C8>성공!</color>";
                    break;
            }
        }

        private void HandleState(CastState prev, CastState next)
        {
            if (_finished || _step == Step.Done) return;
            switch (next)
            {
                case CastState.Drawing:
                    if (_step != Step.Draw) SetStep(Step.Draw);
                    break;
                case CastState.Armed:
                    SetStep(Step.Point);
                    break;
                case CastState.Aiming:
                    SetStep(Step.Hold);
                    break;
                case CastState.Idle:
                    // 취소 · 실패 — 시전 확정이 오기 전 Idle 은 처음부터 다시 (확정이 오면 HandleAccepted 가 덮는다)
                    if (prev == CastState.Aiming || prev == CastState.Armed || prev == CastState.RuneReady)
                        Invoke(nameof(RetryIfNotDone), 0.6f);
                    break;
            }
        }

        private void RetryIfNotDone()
        {
            // 이미 그리기 단계면 (다른 스킬로 인식 등) 그 안내를 덮지 않는다
            if (!_finished && _step != Step.Done && _step != Step.Draw && _cast.State == CastState.Idle) SetStep(Step.Draw, "<color=#FFB86B>다시 해 볼까요?</color>");
        }

        private void HandleRecognized(SpellDefinition def, int slot)
        {
            if (_finished || _step == Step.Done) return;
            if (def == null || def.SpellId != Target)
            {
                SetStep(Step.Draw, "<color=#FFB86B>" + (def != null ? SkillName(def.SpellId) : "다른 스킬") + " 으로 인식됐어요 — 그림을 다시 따라 그려 보세요</color>");
                _cast.Cancel(CastCancelReason.None);
                return;
            }
            SetStep(def.RequiresAim ? Step.Point : Step.Hold);
        }

        private void HandleFailed(string message)
        {
            if (_finished || _step == Step.Done) return;
            SetStep(Step.Draw, "<color=#FFB86B>인식하지 못했어요 — 천천히 크게 따라 그려 보세요</color>");
        }

        private void HandleAccepted(int slot, int spellId)
        {
            if (_finished || spellId != Target) return;
            SetStep(Step.Done);
            Audio.SoundManager.Play("SFX_UIMatchFound", Audio.SoundVolume.Flow, null, 0f);
            _nextAt = Time.time + 1.8f;
        }

        private void Update()
        {
            if (_finished) return;
            if (KeyInput.Down(Key.N)) Finish(); // PC: 연습 건너뛰기
            // 획을 하나라도 그리고 손가락을 뗐으면 다음은 주먹
            if (_step == Step.Draw && _cast.State == CastState.Drawing && _cast.StrokeCount > 0 && !_input.IsDrawing) SetStep(Step.Fist);
            if (_nextAt > 0f && Time.time >= _nextAt)
            {
                _nextAt = -1f;
                if (_index + 1 < Skills.Length) Begin(_index + 1);
                else Finish();
            }
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            _guide.Hide();
            _title.text = "연습 완료!";
            _body.text = "잠시 후 6인 대전에 입장합니다\n스킬창을 내려다보고 검지로 카드를 가리켜 핀치하면 그리는 법이 나와요";
            Invoke(nameof(Complete), 3f);
        }

        private void Complete() => _onComplete?.Invoke();
    }
}
