// §6.2 오른손 시전 상태 머신
//
//   Idle ──(엄지+검지 핀치)──▶ Drawing ⟲ 핀치 해제 = 한 획 저장, 다시 핀치 = 다음 획 (한붓그리기가 아니어도 된다)
//     ▲                          │ 주먹 = 그리기 끝
//     │                          ▼
//     │                      Recognizing ($P+ 다중 획, 사용 가능한 슬롯만) ── 실패 → Idle
//     │                          │ 성공
//     │                          ▼
//     │                      RuneReady ──(같은 주먹)──▶ Armed ──(검지 포인팅)──▶ Aiming ──(2초)──▶ Cast
//     │                          │ 자기/타워/전역 스킬: 주먹 즉시 Cast (§6.3)
//     └──────────── 손바닥 펴기 · 5초 무입력 · 트래킹 손실 → 취소 (쿨타임 미소모, §6.4)
//
// 키보드 대체 입력(§6.6)은 슬롯 번호로 RuneReady를 건너뛰고 바로 Aiming(또는 즉시 시전)으로 간다.
// 룬 인식 테스트(recognizeAllRunes): 덱에 없는 룬도 후보로 인식해 손 옆 아이콘 · 조준까지 진행하고, SlotIndex = -1 테스트 시전을 보낸다
//   (로컬 봇전 서버만 받아들인다 — 쿨타임 · 횟수 없음). 덱에 있는 룬은 원래대로 시전한다.
// 클라이언트는 요청만 보낸다 — 시전 가능 여부 · 결과는 서버가 결정한다(Rule 4).

using System;
using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Gesture;
using SpellboundVR.Spells;
using UnityEngine;

namespace SpellboundVR.Input
{
    public enum CastState
    {
        Idle,
        Drawing,
        Recognizing,
        RuneReady,
        Armed,
        Aiming,
    }

    public sealed class CastStateMachine : MonoBehaviour
    {
        public AimPointer aimPointer;
        public GestureValidationSettings recognition = new GestureValidationSettings();
        [Tooltip("인식 결과를 콘솔에 출력")]
        public bool logRecognition = true;
        [Tooltip("룬 인식 테스트: 덱 · 쿨타임과 상관없이 라이브러리의 모든 룬을 후보로 인식한다. 덱 밖의 룬은 시전 요청 없이 로그만 남긴다")]
        public bool recognizeAllRunes;

        private ClientSession _session;
        private ISpellInputSource _input;
        private EightSlotDeckManager _deck;
        private readonly ReadySlotTemplateSet _templates = new ReadySlotTemplateSet();
        private readonly CastCancelHandler _cancel = new CastCancelHandler();
        private readonly List<Vector2[]> _strokes = new List<Vector2[]>(8);
        private bool _templatesDirty = true;
        private float _stateEnterTime;
        private int _selectedSlot = -1;
        private SpellDefinition _selectedSpell;
        /// <summary>테스트용 임시 스킬 정의 (카탈로그에 없는 룬 — 이름 · 조준 표시용)</summary>
        private readonly Dictionary<int, SpellDefinition> _testSpells = new Dictionary<int, SpellDefinition>();

        public CastState State { get; private set; } = CastState.Idle;

        /// <summary>마지막으로 인식을 시도한 룬의 획들 (룬 샘플 녹화용 — Spellbound > Runes 메뉴)</summary>
        public IReadOnlyList<Vector2[]> LastStrokes { get; private set; } = Array.Empty<Vector2[]>();
        /// <summary>지금 그리는 룬에 저장된 획 수 (Drawing 중)</summary>
        public int StrokeCount => _strokes.Count;
        public int SelectedSlot => _selectedSlot;
        public SpellDefinition SelectedSpell => _selectedSpell;
        public float TimeInState => Time.time - _stateEnterTime;
        public float AimProgress => State == CastState.Aiming && _session != null
            ? Mathf.Clamp01(TimeInState / Mathf.Max(0.01f, _session.Rules.AimDurationSeconds))
            : 0f;

        /// <summary>(이전, 다음)</summary>
        public event Action<CastState, CastState> OnStateChanged;
        public event Action<SpellDefinition, int> OnRuneRecognized;
        public event Action<string> OnRecognitionFailed;
        public event Action<SpellCastRequest> OnCastRequested;
        public event Action<CastCancelReason> OnCancelled;

        public void Initialize(ClientSession session, ISpellInputSource input, EightSlotDeckManager deck)
        {
            Unsubscribe();
            _session = session;
            _input = input;
            _deck = deck;
            if (aimPointer != null) aimPointer.Initialize(session, input);

            _input.OnPinchStarted += HandlePinchStarted;
            _input.OnPinchEnded += HandlePinchEnded;
            _input.OnFistDetected += HandleFist;
            _input.OnPointDetected += HandlePoint;
            _input.OnPalmOpenDetected += HandlePalmOpen;
            _input.OnDirectSlotCast += HandleDirectSlot;
            _deck.OnChanged += MarkTemplatesDirty;
            _templatesDirty = true;
        }

        private void OnDestroy() => Unsubscribe();

        private void Unsubscribe()
        {
            if (_input != null)
            {
                _input.OnPinchStarted -= HandlePinchStarted;
                _input.OnPinchEnded -= HandlePinchEnded;
                _input.OnFistDetected -= HandleFist;
                _input.OnPointDetected -= HandlePoint;
                _input.OnPalmOpenDetected -= HandlePalmOpen;
                _input.OnDirectSlotCast -= HandleDirectSlot;
            }
            if (_deck != null) _deck.OnChanged -= MarkTemplatesDirty;
        }

        private void MarkTemplatesDirty() => _templatesDirty = true;

        private void Update()
        {
            if (_session == null || _input == null) return;

            var reason = _cancel.Evaluate(State, TimeInState, _session.Rules.RuneReadyTimeoutSeconds,
                                          _input.IsRightHandTracked, _input.IsDrawing, _deck, _selectedSlot);
            if (reason != CastCancelReason.None)
            {
                Cancel(reason);
                return;
            }

            if (State == CastState.Aiming)
            {
                float progress = AimProgress;
                if (aimPointer != null) aimPointer.Tick(progress);
                if (progress >= 1f) CommitCast();
            }
        }

        // ── 입력 핸들러 ───────────────────────────────────────

        private void HandlePinchStarted()
        {
            if (State == CastState.Drawing)
            {
                _stateEnterTime = Time.time; // 다음 획 — 무입력 타이머 재시작
                _session.Client?.ReportActivity();
                return;
            }
            if (State != CastState.Idle) return; // RuneReady 이후의 핀치는 무시
            SetState(CastState.Drawing);
            _session.Client?.ReportActivity(); // §10 활동 = 룬 드로잉 시작
        }

        private void HandlePinchEnded(Vector2[] stroke)
        {
            if (State != CastState.Drawing) return;
            if (!_input.IsRightHandTracked)
            {
                Cancel(CastCancelReason.TrackingLost);
                return;
            }
            if (stroke != null && stroke.Length > 0) _strokes.Add(stroke);
            _stateEnterTime = Time.time; // 획 사이 무입력 타이머 시작 (주먹을 쥘 때까지 Drawing 유지)
        }

        private void HandleFist()
        {
            if (State == CastState.Drawing)
            {
                if (_input.IsDrawing) return; // 획을 긋는 중 — 핀치를 놓은 뒤에만 끝낼 수 있다
                Recognize();
                if (State != CastState.RuneReady) return; // 인식 실패 → Idle
            }
            if (State != CastState.RuneReady || _selectedSpell == null) return;
            if (!_selectedSpell.RequiresAim)
            {
                CommitCast(); // §6.3 자기 · 타워 · 전역: 주먹 즉시 시전
                return;
            }
            SetState(CastState.Armed);
        }

        private void HandlePoint()
        {
            if (State != CastState.Armed) return;
            BeginAiming();
        }

        private void HandlePalmOpen()
        {
            _cancel.RequestPalmCancel();
        }

        private void HandleDirectSlot(int slot)
        {
            if (State == CastState.Drawing || State == CastState.Recognizing) return;
            if (_deck == null || !_deck.IsReady(slot))
            {
                _session.Events.RaiseLocalRejection(_deck != null && _deck.GetSpellId(slot) <= 0 ? "Empty slot" : "Slot not ready");
                return;
            }
            var def = _session.Catalog.Get(_deck.GetSpellId(slot));
            if (def == null)
            {
                _session.Events.RaiseLocalRejection("Unknown spell");
                return;
            }
            if (State == CastState.Aiming && aimPointer != null) aimPointer.End();
            _selectedSlot = slot;
            _selectedSpell = def;
            _session.Client?.ReportActivity();
            OnRuneRecognized?.Invoke(def, slot);
            if (def.RequiresAim) BeginAiming();
            else CommitCast();
        }

        // ── 단계 ─────────────────────────────────────────────

        /// <summary>지금까지 그린 획 전부를 룬 하나로 인식한다 (주먹 = 그리기 끝).</summary>
        private void Recognize()
        {
            var strokes = _strokes.ToArray();
            LastStrokes = strokes;
            EndRune();
            SetState(CastState.Recognizing);

            recognition.MinStrokeSize = _input.MinStrokeSize;
            var strokeReject = GestureValidator.ValidateStrokes(strokes, recognition);
            if (strokeReject != GestureRejectReason.None)
            {
                Fail("Rune too short");
                return;
            }

            if (_templatesDirty)
            {
                if (recognizeAllRunes) _templates.RebuildAll(_session.Runes);
                else _templates.Rebuild(_session.LocalDeck, _deck.GetState, _session.Runes);
                _templatesDirty = false;
            }
            if (_templates.Candidates.Count == 0)
            {
                Fail("No ready runes");
                return;
            }

            var match = _templates.Recognize(strokes);
            var reject = GestureValidator.ValidateMatch(match, recognition);
            if (logRecognition)
                Debug.Log($"[CastStateMachine] $P+ → {SpellIds.Code(match.SpellId)} d={match.Distance:0.00} " +
                          $"2nd={SpellIds.Code(match.SecondSpellId)} d2={(match.SecondDistance < float.MaxValue ? match.SecondDistance.ToString("0.00") : "-")} → {reject} ({strokes.Length}획)");
            if (reject != GestureRejectReason.None)
            {
                Fail(reject == GestureRejectReason.Ambiguous ? "Rune unclear" : "Rune not recognized");
                return;
            }

            int slot = _deck.FindSlot(match.SpellId);
            var def = _session.Catalog.Get(match.SpellId);
            if (recognizeAllRunes && slot < 0)
            {
                // 덱 밖의 룬 — 테스트 진행 (아이콘 · 조준까지, 시전 요청 없음)
                _selectedSlot = -1;
                _selectedSpell = def != null ? def : TestSpell(match.SpellId);
                Debug.Log($"[RuneTest] {SpellIds.Code(match.SpellId)} {_selectedSpell.DisplayName} 인식 (덱 밖 — 시전 요청 없이 테스트)");
                SetState(CastState.RuneReady);
                OnRuneRecognized?.Invoke(_selectedSpell, -1);
                return;
            }
            if (slot < 0 || def == null || !_deck.IsReady(slot))
            {
                Fail("Rune not ready");
                return;
            }

            _selectedSlot = slot;
            _selectedSpell = def;
            SetState(CastState.RuneReady);
            OnRuneRecognized?.Invoke(def, slot);
        }

        private void Fail(string message)
        {
            OnRecognitionFailed?.Invoke(message);
            _session.Events.RaiseLocalRejection(message);
            ResetSelection();
            SetState(CastState.Idle);
        }

        private void BeginAiming()
        {
            SetState(CastState.Aiming);
            if (aimPointer != null) aimPointer.Begin(_selectedSpell);
        }

        private void CommitCast()
        {
            if (_selectedSpell != null && _selectedSlot < 0 && recognizeAllRunes)
            {
                CommitTestCast();
                return;
            }
            if (_selectedSpell == null || _selectedSlot < 0)
            {
                Cancel(CastCancelReason.SlotUnavailable);
                return;
            }

            var request = new SpellCastRequest
            {
                SpellId = _selectedSpell.SpellId,
                SlotIndex = _selectedSlot,
                TargetColumn = Column.Center,
                TargetDepth = 0f,
            };

            if (_selectedSpell.RequiresAim && aimPointer != null)
            {
                var aim = aimPointer.Current;
                if (aim.Valid)
                {
                    request.TargetColumn = aim.LocalColumn;
                    request.TargetDepth = aim.Depth;
                }
                aimPointer.End();
            }

            if (_session.Client != null && _session.Client.IsReady)
            {
                _session.Client.RequestCast(request);
                OnCastRequested?.Invoke(request);
            }
            else
            {
                _session.Events.RaiseLocalRejection("Not connected");
            }

            ResetSelection();
            SetState(CastState.Idle);
        }

        /// <summary>
        /// 덱 밖 룬의 테스트 시전 — 카탈로그에 있는 스킬이면 SlotIndex = -1 로 서버에 보낸다
        /// (로컬 봇전 서버만 허용: 쿨타임 · 횟수 없이 실제 발동. PC 서버는 거절).
        /// </summary>
        private void CommitTestCast()
        {
            var request = new SpellCastRequest
            {
                SpellId = _selectedSpell.SpellId,
                SlotIndex = -1,
                TargetColumn = Column.Center,
                TargetDepth = 0f,
            };
            string target = "-";
            if (_selectedSpell.RequiresAim && aimPointer != null)
            {
                var aim = aimPointer.Current;
                if (aim.Valid)
                {
                    target = $"{aim.LocalColumn} 깊이 {aim.Depth:0.00}";
                    request.TargetColumn = aim.LocalColumn;
                    request.TargetDepth = aim.Depth;
                }
                aimPointer.End();
            }

            bool real = _session.Catalog.Get(_selectedSpell.SpellId) != null;
            if (real && _session.Client != null && _session.Client.IsReady)
            {
                _session.Client.RequestCast(request);
                OnCastRequested?.Invoke(request);
                Debug.Log($"[RuneTest] {_selectedSpell.Code} {_selectedSpell.DisplayName} 테스트 시전 요청 (조준: {target}, 덱 밖 — 쿨타임 없음)");
            }
            else
            {
                Debug.Log($"[RuneTest] {_selectedSpell.Code} {_selectedSpell.DisplayName} 시전 테스트 완료 (조준: {target}) — 서버 요청 안 보냄");
            }
            ResetSelection();
            SetState(CastState.Idle);
        }

        private SpellDefinition TestSpell(int spellId)
        {
            if (_testSpells.TryGetValue(spellId, out var def)) return def;
            var rune = _session.Runes != null ? _session.Runes.Find(spellId) : null;
            def = SpellDefinition.Create(spellId, rune != null ? rune.RuneName : SpellIds.Code(spellId), "Test", Color.white);
            def.Targeting = SpellTargeting.Column; // 조준 단계까지 확인
            _testSpells.Add(spellId, def);
            return def;
        }

        public void Cancel(CastCancelReason reason)
        {
            if (State == CastState.Idle) return;
            if (aimPointer != null && aimPointer.IsActive) aimPointer.End();
            ResetSelection();
            _cancel.ClearRequests();
            SetState(CastState.Idle);
            OnCancelled?.Invoke(reason);
        }

        private void ResetSelection()
        {
            _selectedSlot = -1;
            _selectedSpell = null;
        }

        /// <summary>그리던 룬을 끝낸다 — 저장한 획을 비우고 다음 핀치는 새 룬 (그리기 평면 다시 잡기)</summary>
        private void EndRune()
        {
            _strokes.Clear();
            _input?.EndRune();
        }

        private void SetState(CastState next)
        {
            if (next == State) return;
            if (next == CastState.Idle) EndRune();
            var prev = State;
            State = next;
            _stateEnterTime = Time.time;
            OnStateChanged?.Invoke(prev, next);
        }
    }
}
