// 로컬 실행 호스트 — 같은 프로세스 안에서 "서버" 시뮬레이션을 돌린다.
//   · 에디터(키보드 대체 입력) 개발 · Quest 단독 봇전(오프라인) 테스트용
//   · 판정 코드는 PC 서버와 100% 같다 (MatchSimulation). 네트워크만 생략된다.
//   · 고정 틱 누산기로 프레임 속도와 무관하게 결정적 틱을 유지한다.

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Network
{
    public sealed class LocalMatchHost : MonoBehaviour, IMatchClient
    {
        private const int MaxStepsPerFrame = 5;

        private MatchSimulation _sim;
        private NetworkGameEventPublisher _events;
        private readonly MatchSnapshot _snapshot = new MatchSnapshot();
        private float _accumulator;
        private float _tickInterval = 1f / 30f;
        private Team _localTeam = Team.Home;

        public MatchSimulation Simulation => _sim;

        public bool IsReady => _sim != null;

        public Team LocalTeam => _localTeam;

        public void Initialize(MatchSimulation sim, NetworkGameEventPublisher events, Team localTeam, int[] deck, bool fillOpponentWithBot)
        {
            _sim = sim;
            _events = events;
            _localTeam = localTeam;
            _tickInterval = Mathf.Max(1f / 120f, sim.Rules.LocalTickInterval);

            sim.SetEventSink(events);
            sim.JoinHuman(localTeam, deck);
            if (fillOpponentWithBot) sim.JoinBot(TeamUtil.Opponent(localTeam));
            events.SetLocalTeam(localTeam);

            sim.WriteSnapshot(_snapshot);
            events.ApplySnapshot(_snapshot);
        }

        private void Update()
        {
            if (_sim == null) return;
            _sim.PollFrameInput();

            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= _tickInterval && steps < MaxStepsPerFrame)
            {
                _sim.Tick(_tickInterval);
                _accumulator -= _tickInterval;
                steps++;
            }
            if (steps == MaxStepsPerFrame) _accumulator = 0f; // 프레임 급락 시 따라잡기 포기 (스파이럴 방지)

            if (steps > 0)
            {
                _sim.WriteSnapshot(_snapshot);
                _events.ApplySnapshot(_snapshot);
            }
        }

        // ── IMatchClient ─────────────────────────────────────

        public void SubmitDeck(int[] deck) => _sim.SetDeck(_localTeam, deck);

        public void RequestCast(in SpellCastRequest request) => _sim.SubmitCast(_localTeam, request);

        public void RequestMove(in MoveRequest request) => _sim.SubmitMove(_localTeam, request);

        public void ReportActivity() => _sim.ReportActivity(_localTeam);
    }
}
