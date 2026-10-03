// §1 Commander 방식 — 플레이어는 자기 진영 후방 Commander 발판의 좌 · 중 · 우 3칸을 이동한다.
// 로컬 플레이어 리그(OVRCameraRig)를 팀 · 열에 맞는 발판 위로 순간 이동시킨다 (§6.5 스냅 이동).

using System;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Arena
{
    public sealed class CommanderPlatform : MonoBehaviour
    {
        [Tooltip("이동시킬 플레이어 리그 루트 (OVRCameraRig)")]
        public Transform playerRig;
        [Tooltip("리그 원점의 지면 기준 높이 (비우면 시작 시 리그 높이 사용)")]
        public float rigHeight = -1f;
        [Tooltip("리그 아래쪽 기울기 (헤드셋 없는 데스크톱 테스트 전용, VR에서는 0)")]
        public float viewPitch = 0f;

        private ArenaLayout _layout;
        private Team _team = Team.Home;
        private int _worldColumn = 1;

        public int WorldColumn => _worldColumn;

        public Team Team => _team;

        /// <summary>순간 이동 직후 (비네팅 트리거)</summary>
        public event Action OnSnapped;

        public void Initialize(ArenaLayout layout, Transform rig)
        {
            _layout = layout;
            if (rig != null) playerRig = rig;
            if (playerRig != null && rigHeight < 0f)
                rigHeight = playerRig.position.y - layout.Geometry.GroundY;
            if (rigHeight < 0f) rigHeight = 0f;
        }

        public void SetTeam(Team team, int worldColumn)
        {
            _team = team;
            Place(worldColumn, false);
        }

        public void Place(int worldColumn, bool withEffect)
        {
            _worldColumn = Mathf.Clamp(worldColumn, 0, 2);
            if (playerRig == null || _layout == null) return;
            var g = _layout.Geometry;
            Vector3 pos = g.ToWorld(g.PlayerPosition(_team, _worldColumn), rigHeight);
            playerRig.SetPositionAndRotation(pos, g.FacingRotation(_team) * Quaternion.Euler(viewPitch, 0f, 0f));
            if (withEffect) OnSnapped?.Invoke();
        }
    }
}
