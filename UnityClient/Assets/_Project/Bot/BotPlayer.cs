// §20.1 봇은 "서버 안의 가상 플레이어" — 사람과 같은 요청 · 같은 검증 · 같은 스킬 코드 · 같은 승패 규칙.
// 봇 두뇌(IBotBrain)가 만든 SpellCastRequest / MoveRequest를 MatchSimulation의 공용 요청 경로로 넣는다.
// 봇은 AFK 판정 대상이 아니다.

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Spells;
using SpellboundVR.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Bot
{
    public sealed class BotPlayer
    {
        private readonly IBotBrain _autoBrain;
        private readonly ManualKeyBrain _manualBrain;
        private readonly bool _manualKeysEnabled;

        public BotPlayer(Team team, int[] deck, SpellCatalog catalog, BotConfig config, uint seed,
                         bool manualKeysEnabled, bool requireAlt, IBotBrain brainOverride = null)
        {
            Team = team;
            Deck = deck;
            _autoBrain = brainOverride ?? new TimerRandomBrain(deck, catalog, config, seed);
            _manualBrain = new ManualKeyBrain(team, deck) { RequireAlt = requireAlt };
            _manualKeysEnabled = manualKeysEnabled;
        }

        public Team Team { get; }

        public int[] Deck { get; }

        public bool ManualMode { get; private set; }

        public ManualKeyBrain Manual => _manualBrain;

        public void SetManualMode(bool manual)
        {
            ManualMode = manual && _manualKeysEnabled;
        }

        public string BrainName => ManualMode ? "Manual" : _autoBrain.GetType().Name;

        /// <summary>프레임마다 호출 (키 입력 수집). 서버 틱과 분리해 키 중복 · 누락을 막는다.</summary>
        public void PollInput()
        {
            if (!_manualKeysEnabled) return;
            bool modifierOk = !_manualBrain.RequireAlt || KeyInput.AltHeld();
            if (modifierOk && KeyInput.Down(Key.Space))
            {
                ManualMode = !ManualMode;
                Debug.Log($"[BotPlayer] {Team} 봇 → {(ManualMode ? "수동" : "자동")} 조종");
            }
            if (ManualMode) _manualBrain.PollInput();
        }

        public void Tick(MatchSimulation sim)
        {
            var obs = sim.BuildObservation(Team);
            var cmd = default(BotCommand);
            if (ManualMode) _manualBrain.Tick(obs, ref cmd);
            else _autoBrain.Tick(obs, ref cmd);

            if (cmd.HasCastRequest) sim.SubmitCast(Team, cmd.CastRequest);
            if (cmd.HasMoveRequest) sim.SubmitMove(Team, cmd.MoveRequest);
        }
    }
}
