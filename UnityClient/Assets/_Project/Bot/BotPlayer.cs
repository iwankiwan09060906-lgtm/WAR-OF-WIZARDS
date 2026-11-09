// §20.1 봇은 "서버 안의 가상 플레이어" — 사람과 같은 요청 · 같은 검증 · 같은 스킬 코드 · 같은 승패 규칙.
// 봇 두뇌(IBotBrain)가 만든 SpellCastRequest / MoveRequest를 MatchSimulation의 공용 요청 경로로 넣는다.
// 봇은 AFK 판정 대상이 아니다.
// 사람이 룬을 그리고 조준하는 시간처럼, 봇도 시전 전에 BotConfig.CastWindupSeconds 동안 시전 준비를 상대에게 보여 준다 (CastPreview).

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Network;
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
        private readonly float _windup;
        private readonly float _windupJitter;
        private readonly float _failChance;
        private readonly DeterministicRandom _rng;
        private bool _hasPendingCast;
        private SpellCastRequest _pendingCast;
        private float _pendingCastAt;
        private float _pendingWindup;
        /// <summary>이 진행률에서 룬 그리기를 실패한다 (실패하지 않으면 1 초과)</summary>
        private float _pendingFailAt;

        public BotPlayer(Team team, int[] deck, SpellCatalog catalog, BotConfig config, uint seed,
                         bool manualKeysEnabled, bool requireAlt, IBotBrain brainOverride = null)
        {
            Team = team;
            Deck = deck;
            _windup = config != null ? Mathf.Max(0f, config.CastWindupSeconds) : 0f;
            _windupJitter = config != null ? config.CastWindupJitter : 0f;
            _failChance = config != null ? config.CastFailChance : 0f;
            _rng = new DeterministicRandom(seed ^ 0x5bd1e995u);
            _autoBrain = brainOverride ?? CreateBrain(team, deck, catalog, config, seed);
            _manualBrain = new ManualKeyBrain(team, deck) { RequireAlt = requireAlt };
            _manualKeysEnabled = manualKeysEnabled;
        }

        /// <summary>BotConfig.Brain 에 따라: AI 전략 두뇌(기본, 서버 연결이 있으면 같이 씀) · 예전 랜덤 두뇌</summary>
        public static IBotBrain CreateBrain(Team team, int[] deck, SpellCatalog catalog, BotConfig config, uint seed)
        {
            if (config != null && config.Brain == BotBrainKind.TimerRandom) return new TimerRandomBrain(deck, catalog, config, seed);
            return new AiStrategyBrain(team, deck, catalog, config, seed, StrategyAdvisors.Current);
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
            if (!ManualMode && _autoBrain is IWorldAwareBrain worldAware) worldAware.Observe(sim, Team);
            var obs = sim.BuildObservation(Team);
            var cmd = default(BotCommand);
            if (ManualMode) _manualBrain.Tick(obs, ref cmd);
            else _autoBrain.Tick(obs, ref cmd);

            if (cmd.HasCastRequest && !_hasPendingCast)
            {
                _hasPendingCast = true;
                _pendingCast = cmd.CastRequest;
                _pendingWindup = _windup * (1f + _rng.Range(0f, _windupJitter));
                _pendingCastAt = sim.MatchTime + _pendingWindup;
                // 실패는 다 그리기 전에 멈춘다 — 그린 정도가 ReleaseProgress 아래라 시전 폭발도 안 보인다
                _pendingFailAt = _rng.NextFloat() < _failChance ? _rng.Range(0.35f, 0.8f) : 2f;
            }
            if (cmd.HasMoveRequest) sim.SubmitMove(Team, cmd.MoveRequest);
            if (_hasPendingCast) TickPendingCast(sim);
        }

        /// <summary>시전 준비를 보여 주다가 시간이 되면 시전한다 (준비 중에도 이동은 한다)</summary>
        private void TickPendingCast(MatchSimulation sim)
        {
            float left = _pendingCastAt - sim.MatchTime;
            float progress = _pendingWindup > 0f ? 1f - left / _pendingWindup : 1f;
            if (progress >= _pendingFailAt)
            {
                _hasPendingCast = false;
                sim.SetCastPreview(Team, default);
                return;
            }
            if (left > 0f)
            {
                sim.SetCastPreview(Team, new CastPreview
                {
                    Phase = CastPreviewPhase.Preparing,
                    SpellId = _pendingCast.SpellId,
                    Aiming = true,
                    TargetColumn = _pendingCast.TargetColumn,
                    Progress = progress,
                });
                return;
            }
            _hasPendingCast = false;
            sim.SubmitCast(Team, _pendingCast);
            sim.SetCastPreview(Team, default);
        }
    }
}
