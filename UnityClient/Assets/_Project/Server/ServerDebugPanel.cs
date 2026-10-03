// §37 디버깅 — 서버 디버그 패널 (PC 서버 창 / 에디터 로컬 모드)
// "서버 값이 맞는가", "요청이 서버에 도착했나" 를 확인하는 화면. 봇 수동 조종 상태와 키 안내 포함.

using System.Text;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpellboundVR.Server
{
    public sealed class ServerDebugPanel : MonoBehaviour
    {
        public bool visible = true;
        public Key toggleKey = Key.F12;

        private System.Func<MatchSimulation> _simProvider;
        private System.Func<string> _statusProvider;
        private bool _localMode;
        private readonly StringBuilder _sb = new StringBuilder(2048);
        private string _cached = "";
        private float _nextRefresh;
        private GUIStyle _style;

        public void Initialize(System.Func<MatchSimulation> simProvider, System.Func<string> statusProvider, bool localMode)
        {
            _simProvider = simProvider;
            _statusProvider = statusProvider;
            _localMode = localMode;
        }

        private void Update()
        {
            if (Utils.KeyInput.Down(toggleKey)) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || _simProvider == null) return;
            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                _cached = Build();
            }
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    fontSize = 13,
                    richText = false,
                    wordWrap = false,
                };
                _style.normal.textColor = Color.white;
            }
            GUI.Box(new Rect(10, 10, 470, 430), _cached, _style);
        }

        private string Build()
        {
            var sim = _simProvider();
            _sb.Clear();
            _sb.Append(_localMode ? "[LOCAL SERVER]" : "[PC SERVER]").Append("  F12: hide\n");
            if (_statusProvider != null) _sb.Append(_statusProvider()).Append('\n');
            if (sim == null)
            {
                _sb.Append("simulation not started");
                return _sb.ToString();
            }

            _sb.Append("Match #").Append(sim.MatchSerial).Append("  ").Append(sim.Phase)
               .Append("  t=").Append(sim.MatchTime.ToString("0.0"))
               .Append("  stage ").Append(sim.ScalingStage)
               .Append("  wave ").Append(sim.WaveIndex).Append('\n');
            if (sim.Phase == MatchPhase.Ended)
                _sb.Append("Result: ").Append(sim.Outcome.Winner).Append(" / ").Append(sim.Outcome.Reason).Append('\n');

            for (int t = 0; t < 2; t++)
            {
                var team = (Team)t;
                var p = sim.GetPlayer(team);
                _sb.Append(team).Append(": ");
                if (!p.Present)
                {
                    _sb.Append("(empty)\n");
                    continue;
                }
                _sb.Append(p.IsBot ? "BOT" : "HUMAN").Append(p.Connected ? "" : "(DC)")
                   .Append("  HP ").Append(Mathf.CeilToInt(p.Hp)).Append('/').Append(Mathf.CeilToInt(p.MaxHp))
                   .Append("  col ").Append(p.WorldColumn)
                   .Append("  ATK x").Append(sim.GetAttackMultiplier(team).ToString("0.00"))
                   .Append("  units ").Append(AliveUnitLimiter.CountAlive(sim, team)).Append('\n');
                _sb.Append("   T ").Append(sim.Defense.TowerAlive(team) ? Mathf.CeilToInt(sim.Defense.Tower(team).Hp).ToString() : "X")
                   .Append("  N ").Append(sim.Defense.NexusAlive(team) ? Mathf.CeilToInt(sim.Defense.Nexus(team).Hp).ToString() : "X")
                   .Append(NexusLock.IsLocked(sim.Defense, team) ? " (locked)" : "").Append('\n');
                _sb.Append("   slots: ");
                for (int i = 0; i < DeckData.SlotCount; i++)
                {
                    var s = p.Slots[i];
                    if (s.SpellId <= 0) continue;
                    _sb.Append(i + 1).Append(':').Append(SpellIds.Code(s.SpellId)).Append('=');
                    switch (s.State)
                    {
                        case SlotState.Ready: _sb.Append("R"); break;
                        case SlotState.Cooldown: _sb.Append(Mathf.CeilToInt(s.CooldownEndTime - sim.MatchTime)).Append('s'); break;
                        case SlotState.Exhausted: _sb.Append("EX"); break;
                        default: _sb.Append("D"); break;
                    }
                    _sb.Append(' ');
                }
                _sb.Append('\n');

                var bot = sim.GetBot(team);
                if (bot != null)
                {
                    _sb.Append("   brain: ").Append(bot.BrainName);
                    if (bot.ManualMode)
                        _sb.Append("  aim col ").Append(bot.Manual.AimWorldColumn).Append(" depth ").Append(bot.Manual.Depth.ToString("0.0"));
                    _sb.Append('\n');
                }
            }

            _sb.Append('\n').Append(_localMode ? "BOT KEYS (hold Alt): " : "BOT KEYS: ")
               .Append("Space auto/manual, 1-8 cast,\n  Q/W/E column(screen L/C/R), Up/Down depth, Left/Right move\n")
               .Append("TEST (dev build): F1/F2 tower, F3/F4 nexus, F5/F6 HP1,\n  F7 power-up stage, F8 reset cooldowns\n");
            if (_localMode)
                _sb.Append("PLAYER KEYS: 1-8 cast slot, mouse drag = draw rune,\n  F fist, G point, X/RMB cancel, A/D move");
            return _sb.ToString();
        }
    }
}
