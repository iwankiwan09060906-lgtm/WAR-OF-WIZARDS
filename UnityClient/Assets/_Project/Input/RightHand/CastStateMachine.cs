// §6.2 오른손 시전 상태 머신
//
//   Idle ──(엄지+검지 핀치)──▶ Drawing ──(핀치 해제)──▶ Recognizing ($P+, 사용 가능한 슬롯만)
//     ▲                                                   │ 성공              실패 → Idle
//     │                                                   ▼
//     │                                               RuneReady ──(주먹)──▶ Armed ──(검지 포인팅)──▶ Aiming ──(1초)──▶ Cast
//     │                                                   │ 자기/타워/전역 스킬: 주먹 즉시 Cast (§6.3)
//     └──────────── 손바닥 펴기 · 5초 무입력 · 트래킹 손실 → 취소 (쿨타임 미소모, §6.4)
//
// 키보드 대체 입력(§6.6)은 슬롯 번호로 RuneReady를 건너뛰고 바로 Aiming(또는 즉시 시전)으로 간다.
// 클라이언트는 요청만 보낸다 — 시전 가능 여부 · 결과는 서버가 결정한다(Rule 4).

using System;
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

        private ClientSession _session;
        private ISpellInputSource _input;
        private EightSlotDeckManager _deck;
        private readonly ReadySlotTemplateSet _templates = new ReadySlotTemplateSet();
        private readonly CastCancelHandler _cancel = new CastCancelHandler();
        private bool _templatesDirty = true;
        private float _stateEnterTime;
        private int _selectedSlot = -1;
        private SpellDefinition _selectedSpell;

        public CastState State { get; private set; } = CastState.Idle;

        /// <summary>마지막으로 그린 룬 궤적 (룬 샘플 녹화용 — Spellbound > Runes 메뉴)</summary>
        public Vector2[] LastStroke { get; private set; }
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
                                          _input.IsRightHandTracked, _deck, _selectedSlot);
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
            if (State != CastState.Idle) return; // RuneReady 이후의 핀치는 무시
            SetState(CastState.Drawing);
            _session.Client?.ReportActivity(); // §10 활동 = 룬 드로잉 시작
        }

        private void HandlePinchEnded(Vector2[] stroke)
        {
            if (State != CastState.Drawing) return;
            LastStroke = stroke;
            if (!_input.IsRightHandTracked)
            {
                Cancel(CastCancelReason.TrackingLost);
                return;
            }
            SetState(CastState.Recognizing);
            Recognize(stroke);
        }

        private void HandleFist()
        {
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

        private void Recognize(Vector2[] stroke)
        {
            recognition.MinStrokeSize = _input.MinStrokeSize;
            var strokeReject = GestureValidator.ValidateStroke(stroke, recognition);
            if (strokeReject != GestureRejectReason.None)
            {
                Fail("Rune too short");
                return;
            }

            if (_templatesDirty)
            {
                _templates.Rebuild(_session.LocalDeck, _deck.GetState, _session.Runes);
                _templatesDirty = false;
            }
            if (_templates.Candidates.Count == 0)
            {
                Fail("No ready runes");
                return;
            }

            var match = _templates.Recognize(stroke);
            var reject = GestureValidator.ValidateMatch(match, recognition);
            if (logRecognition)
                Debug.Log($"[CastStateMachine] $P+ → {SpellIds.Code(match.SpellId)} d={match.Distance:0.00} " +
                          $"2nd={SpellIds.Code(match.SecondSpellId)} d2={(match.SecondDistance < float.MaxValue ? match.SecondDistance.ToString("0.00") : "-")} → {reject}");
            if (reject != GestureRejectReason.None)
            {
                Fail(reject == GestureRejectReason.Ambiguous ? "Rune unclear" : "Rune not recognized");
                return;
            }

            int slot = _deck.FindSlot(match.SpellId);
            var def = _session.Catalog.Get(match.SpellId);
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

        private void SetState(CastState next)
        {
            if (next == State) return;
            var prev = State;
            State = next;
            _stateEnterTime = Time.time;
            OnStateChanged?.Invoke(prev, next);
        }
    }
}
