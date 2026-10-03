// §7 ServerSpellValidator — 슬롯 · 쿨타임 · 고위력 잔여 · 생존 · 설치 구역 검증
// 봇도 같은 검증을 통과한다 (Rule 10).

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;
using SpellboundVR.Spells;

namespace SpellboundVR.Network
{
    public static class ServerSpellValidator
    {
        public static CastRejectReason Validate(MatchPhase phase, PlayerState caster, in SpellCastRequest request,
                                                SpellCatalog catalog, ISpellExecutorLookup executors,
                                                out SpellDefinition definition)
        {
            definition = null;
            if (phase != MatchPhase.Playing) return CastRejectReason.MatchNotRunning;
            if (!caster.Alive) return CastRejectReason.CasterDead;
            if (request.SlotIndex < 0 || request.SlotIndex >= DeckData.SlotCount) return CastRejectReason.InvalidSlot;

            ref SlotRuntime slot = ref caster.Slots[request.SlotIndex];
            if (slot.SpellId <= 0 || slot.SpellId != request.SpellId) return CastRejectReason.SpellMismatch;

            switch (slot.State)
            {
                case SlotState.Cooldown: return CastRejectReason.OnCooldown;
                case SlotState.Exhausted: return CastRejectReason.Exhausted;
                case SlotState.Disabled: return CastRejectReason.Disabled;
            }

            if (!catalog.TryGet(request.SpellId, out definition)) return CastRejectReason.NotImplemented;
            if (!executors.Has(request.SpellId)) return CastRejectReason.NotImplemented;

            if ((int)request.TargetColumn > 2) return CastRejectReason.InvalidTarget;
            if (float.IsNaN(request.TargetDepth) || request.TargetDepth < -0.01f || request.TargetDepth > 1.01f)
                return CastRejectReason.InvalidTarget;

            return CastRejectReason.None;
        }
    }

    /// <summary>검증기가 Executor 존재 여부만 확인하기 위한 최소 뷰</summary>
    public interface ISpellExecutorLookup
    {
        bool Has(int spellId);
    }
}
