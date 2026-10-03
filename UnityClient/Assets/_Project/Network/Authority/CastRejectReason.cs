// §37 디버깅 — "거절 사유 코드는?" / §23.2 HUD: 시전 거절 사유

namespace SpellboundVR.Network
{
    public enum CastRejectReason : byte
    {
        None = 0,
        MatchNotRunning,
        CasterDead,
        InvalidSlot,
        SpellMismatch,
        OnCooldown,
        Exhausted,
        Disabled,
        NotImplemented,
        InvalidTarget,
        UnitCapReached,
        QueueFull,
    }

    public static class CastRejectReasonText
    {
        public static string ToMessage(CastRejectReason reason)
        {
            switch (reason)
            {
                case CastRejectReason.MatchNotRunning: return "Match not running";
                case CastRejectReason.CasterDead: return "You are down";
                case CastRejectReason.InvalidSlot: return "Invalid slot";
                case CastRejectReason.SpellMismatch: return "Spell / slot mismatch";
                case CastRejectReason.OnCooldown: return "On cooldown";
                case CastRejectReason.Exhausted: return "No uses left";
                case CastRejectReason.Disabled: return "Slot disabled";
                case CastRejectReason.NotImplemented: return "Spell not implemented";
                case CastRejectReason.InvalidTarget: return "Invalid target";
                case CastRejectReason.UnitCapReached: return "Unit limit reached";
                case CastRejectReason.QueueFull: return "Server busy";
                default: return "Rejected";
            }
        }
    }
}
