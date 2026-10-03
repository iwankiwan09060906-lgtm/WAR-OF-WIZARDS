// 서버 시뮬레이션 → 클라이언트 "1회성" 이벤트 출구.
// 지속 상태(HP · 위치 · 슬롯 등)는 스냅샷으로, 순간 이벤트(VFX · 거절 · 피격 표시)는 이 싱크로 보낸다.
//   · 로컬 모드: LocalMatchHost가 직접 NetworkGameEventPublisher에 전달
//   · Fusion 모드: NetworkMatchState가 RPC로 모든 클라이언트에 전달
// 위치는 아레나 공간 (x = u, y = 높이, z = v)이다.

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Network
{
    public enum HitResultKind : byte
    {
        Damage = 0,
        Miss = 1,
        Dodge = 2,
        Immune = 3,
        Absorbed = 4,
        Heal = 5,
    }

    public interface IMatchEventSink
    {
        void OnSpellFx(int spellId, VfxPart part, Vector3 arenaPosition, int worldColumn);

        void OnCastAccepted(Team team, int slotIndex, int spellId);

        void OnCastRejected(Team team, int slotIndex, CastRejectReason reason);

        void OnMoveRejected(Team team, int confirmedWorldColumn);

        /// <summary>플레이어 스킬 피격 · 플레이어 피격 결과 (데미지 텍스트 · MISS 표시용)</summary>
        void OnHit(int targetId, Vector3 arenaPosition, int amount, HitResultKind kind);
    }

    /// <summary>아무것도 하지 않는 싱크 (테스트 · 헤드리스용)</summary>
    public sealed class NullMatchEventSink : IMatchEventSink
    {
        public static readonly NullMatchEventSink Instance = new NullMatchEventSink();
        public void OnSpellFx(int spellId, VfxPart part, Vector3 arenaPosition, int worldColumn) { }
        public void OnCastAccepted(Team team, int slotIndex, int spellId) { }
        public void OnCastRejected(Team team, int slotIndex, CastRejectReason reason) { }
        public void OnMoveRejected(Team team, int confirmedWorldColumn) { }
        public void OnHit(int targetId, Vector3 arenaPosition, int amount, HitResultKind kind) { }
    }
}
