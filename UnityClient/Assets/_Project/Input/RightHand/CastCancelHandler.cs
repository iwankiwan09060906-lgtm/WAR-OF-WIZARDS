// §6.4 취소와 타임아웃
//   · RuneReady / Armed / Aiming 중 오른손 손바닥을 펴면 취소 (쿨타임 소모 없음)
//   · RuneReady 또는 Armed 상태 5초 무입력 시 자동 취소
//   · Drawing 중 획 사이(핀치를 놓고 주먹을 안 쥔 채) 5초 무입력 시 자동 취소
//   · 핸드트래킹 손실 시 즉시 취소
//   · 선택한 슬롯이 Ready가 아니게 되면 취소 (예: 타워 파괴로 Disabled)

using SpellboundVR.Deck;

namespace SpellboundVR.Input
{
    public enum CastCancelReason
    {
        None,
        PalmOpen,
        Timeout,
        TrackingLost,
        SlotUnavailable,
        RecognitionFailed,
    }

    public sealed class CastCancelHandler
    {
        private bool _palmOpenRequested;

        public void RequestPalmCancel() => _palmOpenRequested = true;

        public void ClearRequests() => _palmOpenRequested = false;

        /// <param name="strokeInProgress">핀치를 유지한 채 획을 긋는 중 (Drawing 무입력 타이머 제외)</param>
        public CastCancelReason Evaluate(CastState state, float timeInState, float timeout, bool rightTracked,
                                         bool strokeInProgress, EightSlotDeckManager deck, int selectedSlot)
        {
            bool cancellable = state == CastState.RuneReady || state == CastState.Armed || state == CastState.Aiming;
            bool active = cancellable || state == CastState.Drawing;
            if (!active)
            {
                _palmOpenRequested = false;
                return CastCancelReason.None;
            }

            if (!rightTracked) return CastCancelReason.TrackingLost;

            if (cancellable && _palmOpenRequested)
            {
                _palmOpenRequested = false;
                return CastCancelReason.PalmOpen;
            }

            bool waiting = state == CastState.RuneReady || state == CastState.Armed ||
                           (state == CastState.Drawing && !strokeInProgress);
            if (waiting && timeInState >= timeout)
                return CastCancelReason.Timeout;

            if (cancellable && deck != null && selectedSlot >= 0 && !deck.IsReady(selectedSlot))
                return CastCancelReason.SlotUnavailable;

            return CastCancelReason.None;
        }
    }
}
