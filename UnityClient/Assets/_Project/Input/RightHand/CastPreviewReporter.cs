// 내 시전 준비를 서버에 알린다 — 상대 화면에서 내 아바타가 룬을 그리는 모습으로 보인다 (CastPreviewView).
//   · 룬 그리기(핀치 · 마우스 드래그): 실제 손끝 궤적을 보낸다
//   · 손 모양 · 슬롯 키: 그린 궤적이 없으므로 스킬과 진행률만 보내고, 상대 화면이 그 스킬의 룬을 그려 보인다
//   · 조준 중에는 어느 열을 노리는지만 보낸다 — 정확한 조준점은 본인에게만 보인다 (§6.3)
//   · 표시 전용 — 시전 가능 여부는 그대로 서버가 정한다 (Rule 4). 아무것도 안 할 때는 보내지 않는다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Input
{
    public sealed class CastPreviewReporter : MonoBehaviour
    {
        private const float SendInterval = 1f / 12f;
        private const float KeepAliveInterval = 1f;
        /// <summary>손끝이 첫 점에서 이만큼(m) 떨어지면 룬 칸 끝으로 본다</summary>
        private const float PenSpan = 0.45f;
        /// <summary>시전 진행률에서 스킬을 고르기까지가 차지하는 몫 (나머지는 조준 시간)</summary>
        private const float SelectShare = 0.25f;

        private ClientSession _session;
        private ISpellInputSource _input;
        private CastStateMachine _cast;
        private EightSlotDeckManager _deck;
        private Transform _head;
        private CastPreview _sent;
        private float _nextSend;
        private float _nextKeepAlive;
        private bool _hasOrigin;
        private Vector3 _origin;
        private Vector2 _pen;
        private int _stroke;

        public void Initialize(ClientSession session, ISpellInputSource input, CastStateMachine cast, EightSlotDeckManager deck, Transform head)
        {
            _session = session;
            _input = input;
            _cast = cast;
            _deck = deck;
            _head = head;
        }

        private void Update()
        {
            var client = _session?.Client;
            if (client == null || !client.IsReady) return;

            var next = Build();
            bool stepChanged = next.Phase != _sent.Phase || next.SpellId != _sent.SpellId || next.Stroke != _sent.Stroke ||
                               next.Aiming != _sent.Aiming || next.TargetColumn != _sent.TargetColumn;
            if (next.Phase == CastPreviewPhase.None && !stepChanged) return;

            float now = Time.unscaledTime;
            bool moved = next.Phase != CastPreviewPhase.None && now >= _nextSend &&
                         (next.Progress != _sent.Progress || next.PenX != _sent.PenX || next.PenY != _sent.PenY);
            if (!stepChanged && !moved && now < _nextKeepAlive) return;

            client.ReportCastPreview(next);
            _sent = next;
            _nextSend = now + SendInterval;
            _nextKeepAlive = now + KeepAliveInterval;
        }

        private CastPreview Build()
        {
            var state = _cast.State;
            if (state != CastState.Drawing) _hasOrigin = false;

            switch (state)
            {
                case CastState.Drawing:
                    TrackPen();
                    return new CastPreview { Phase = CastPreviewPhase.Drawing, PenX = _pen.x, PenY = _pen.y, Stroke = _stroke };
                case CastState.RuneReady:
                case CastState.Armed:
                    return Preparing(SelectShare, false);
                case CastState.Aiming:
                    return Preparing(SelectShare + (1f - SelectShare) * _cast.AimProgress, true);
                case CastState.Idle:
                    return ShapeInProgress();
                default:
                    return default;
            }
        }

        private CastPreview Preparing(float progress, bool aiming)
        {
            var spell = _cast.SelectedSpell;
            var aim = _cast.aimPointer != null ? _cast.aimPointer.Current : default;
            return new CastPreview
            {
                Phase = CastPreviewPhase.Preparing,
                SpellId = spell != null ? spell.SpellId : 0,
                Aiming = aiming && aim.Valid,
                TargetColumn = aim.LocalColumn,
                Progress = progress,
            };
        }

        /// <summary>웹캠: 손 모양을 유지하는 동안 — 고르기 전이지만 덱에 있는 준비된 스킬이면 시작된 것으로 보여 준다</summary>
        private CastPreview ShapeInProgress()
        {
            if (!(_input is MediaPipeHandInput hands) || hands.ShapeProgress <= 0f) return default;
            int spellId = MediaPipeHandInput.SpellNumber(hands.ShapeSpell);
            int slot = _deck.FindSlot(spellId);
            if (spellId <= 0 || (slot >= 0 ? !_deck.IsReady(slot) : !_cast.recognizeAllRunes)) return default;
            return new CastPreview { Phase = CastPreviewPhase.Preparing, SpellId = spellId, Progress = hands.ShapeProgress * SelectShare };
        }

        /// <summary>손끝 위치 = 첫 점에서 머리 기준 오른쪽 · 위쪽으로 얼마나 왔나. 획 사이에는 마지막 값을 유지한다</summary>
        private void TrackPen()
        {
            int count = _input.DrawPointCount;
            if (!_input.IsDrawing || count == 0 || _head == null) return;
            var points = _input.DrawPoints;
            if (!_hasOrigin)
            {
                _hasOrigin = true;
                _origin = points[0];
                _pen = Vector2.zero;
            }
            _stroke = _cast.StrokeCount;
            Vector3 offset = points[count - 1] - _origin;
            _pen = new Vector2(Mathf.Clamp(Vector3.Dot(offset, _head.right) / PenSpan, -1f, 1f),
                               Mathf.Clamp(Vector3.Dot(offset, _head.up) / PenSpan, -1f, 1f));
        }
    }
}
