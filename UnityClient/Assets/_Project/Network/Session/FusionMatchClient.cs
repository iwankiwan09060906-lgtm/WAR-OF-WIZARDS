// IMatchClient의 Fusion 구현 — 로컬 NetworkPlayer의 RPC로 PC 서버에 요청한다.

using SpellboundVR.Contracts;

namespace SpellboundVR.Network
{
    public sealed class FusionMatchClient : IMatchClient
    {
        private readonly NetworkPlayer _player;

        public FusionMatchClient(NetworkPlayer player)
        {
            _player = player;
        }

        public bool IsReady => _player != null && _player.Object != null && _player.Object.IsValid && _player.HasTeam;

        public Team LocalTeam => _player != null ? _player.Team : Team.Home;

        public void SubmitDeck(int[] deck)
        {
            if (_player != null) _player.SubmitDeck(deck);
        }

        public void RequestCast(in SpellCastRequest request)
        {
            if (!IsReady) return;
            _player.RPC_RequestCast(request.SpellId, (byte)request.SlotIndex, (byte)request.TargetColumn, request.TargetDepth);
        }

        public void RequestMove(in MoveRequest request)
        {
            if (!IsReady) return;
            _player.RPC_RequestMove(request.Direction);
        }

        public void ReportActivity()
        {
            if (!IsReady) return;
            _player.RPC_ReportActivity();
        }
    }
}
