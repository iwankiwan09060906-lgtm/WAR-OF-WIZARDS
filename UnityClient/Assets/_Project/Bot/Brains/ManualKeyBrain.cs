// §20.3 수동 조종 — PC 서버 창(또는 에디터)에서 키 입력으로 봇을 강제 조작하는 테스트 도구
//   1 ~ 8     : 해당 슬롯 시전
//   Q / W / E : 조준 열 L / C / R (서버 카메라 = Home 시점 기준 화면 좌/중/우)
//   ↑ / ↓     : 범위 공격 깊이 ±0.1
//   ← / →     : 봇 이동 (화면 기준)
//   Space     : 자동(타이머) ↔ 수동 전환 (BotPlayer가 처리)
// 로컬(에디터 1인) 모드에서는 Alt를 누른 채 입력해야 한다 (사람 키보드 입력과 충돌 방지).
// 키는 프레임 단위 PollInput()으로 모아 두고, 서버 틱 Tick()에서 한 번만 소비한다.

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Bot
{
    public sealed class ManualKeyBrain : IBotBrain
    {
        private readonly Team _team;
        private readonly int[] _deck;
        private int _aimWorldColumn = 1;
        private float _depth = 0.85f;
        private int _pendingSlot = -1;
        private int _pendingWorldMove;

        public ManualKeyBrain(Team team, int[] deck)
        {
            _team = team;
            _deck = deck;
        }

        public bool RequireAlt { get; set; }

        public int AimWorldColumn => _aimWorldColumn;

        public float Depth => _depth;

        public void PollInput()
        {
            if (RequireAlt && !KeyInput.AltHeld()) return;

            int slot = KeyInput.SlotKeyDown();
            if (slot >= 0) _pendingSlot = slot;

            if (KeyInput.Down(Key.Q)) _aimWorldColumn = 0;
            if (KeyInput.Down(Key.W)) _aimWorldColumn = 1;
            if (KeyInput.Down(Key.E)) _aimWorldColumn = 2;
            if (KeyInput.Down(Key.UpArrow)) _depth = Mathf.Clamp01(_depth + 0.1f);
            if (KeyInput.Down(Key.DownArrow)) _depth = Mathf.Clamp01(_depth - 0.1f);
            if (KeyInput.Down(Key.LeftArrow)) _pendingWorldMove = -1;
            if (KeyInput.Down(Key.RightArrow)) _pendingWorldMove = 1;
        }

        public void Tick(in BotObservation obs, ref BotCommand cmd)
        {
            if (_pendingSlot >= 0)
            {
                int slot = _pendingSlot;
                _pendingSlot = -1;
                if (slot < _deck.Length && _deck[slot] > 0)
                {
                    cmd.HasCastRequest = true;
                    cmd.CastRequest = new SpellCastRequest
                    {
                        SpellId = _deck[slot],
                        SlotIndex = slot,
                        TargetColumn = TeamUtil.ToLocalColumn(_team, _aimWorldColumn),
                        TargetDepth = _depth,
                    };
                }
            }

            if (_pendingWorldMove != 0)
            {
                // 화면(월드) 방향 → 봇 자신의 시점 방향
                int localDir = _team == Team.Home ? _pendingWorldMove : -_pendingWorldMove;
                _pendingWorldMove = 0;
                cmd.HasMoveRequest = true;
                cmd.MoveRequest = new MoveRequest { Direction = (sbyte)localDir };
            }
        }
    }
}
