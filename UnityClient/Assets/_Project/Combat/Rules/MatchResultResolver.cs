// §10 승패 조건
//   사망: HP 0 → 패배 (구조물 생존 무관, 리스폰 없음) / 양측 같은 틱 사망 → 무승부
//   기권: 사람 30초 무활동 / 연결 끊김: 사람 이탈 → 패배 / 경기 무효: 서버 오류

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;

namespace SpellboundVR.Combat
{
    public struct MatchOutcome
    {
        public bool Decided;
        public MatchWinner Winner;
        public MatchEndReason Reason;
    }

    public static class MatchResultResolver
    {
        public static MatchOutcome Evaluate(PlayerState home, PlayerState away, float matchTime, MatchRuleConfig rules)
        {
            var outcome = new MatchOutcome { Decided = false, Winner = MatchWinner.None };

            // 연결 끊김 (사람만)
            bool homeLeft = home.IsHuman && !home.Connected;
            bool awayLeft = away.IsHuman && !away.Connected;
            if (homeLeft || awayLeft)
            {
                outcome.Decided = true;
                outcome.Reason = MatchEndReason.Disconnect;
                outcome.Winner = homeLeft && awayLeft ? MatchWinner.Void : (homeLeft ? MatchWinner.Away : MatchWinner.Home);
                if (homeLeft && awayLeft) outcome.Reason = MatchEndReason.Void;
                return outcome;
            }

            // 사망 (같은 틱 양측 사망 = 무승부)
            bool homeDead = !home.Alive;
            bool awayDead = !away.Alive;
            if (homeDead || awayDead)
            {
                outcome.Decided = true;
                if (homeDead && awayDead)
                {
                    outcome.Winner = MatchWinner.Draw;
                    outcome.Reason = MatchEndReason.Draw;
                }
                else
                {
                    outcome.Winner = homeDead ? MatchWinner.Away : MatchWinner.Home;
                    outcome.Reason = MatchEndReason.PlayerDeath;
                }
                return outcome;
            }

            // 기권
            bool homeAfk = AfkMonitor.IsForfeit(home, matchTime, rules);
            bool awayAfk = AfkMonitor.IsForfeit(away, matchTime, rules);
            if (homeAfk || awayAfk)
            {
                outcome.Decided = true;
                outcome.Reason = MatchEndReason.Afk;
                outcome.Winner = homeAfk && awayAfk ? MatchWinner.Draw : (homeAfk ? MatchWinner.Away : MatchWinner.Home);
                return outcome;
            }

            return outcome;
        }

        public static MatchResult ToLocalResult(MatchWinner winner, Team localTeam)
        {
            switch (winner)
            {
                case MatchWinner.Home: return localTeam == Team.Home ? MatchResult.Win : MatchResult.Lose;
                case MatchWinner.Away: return localTeam == Team.Away ? MatchResult.Win : MatchResult.Lose;
                case MatchWinner.Draw: return MatchResult.Draw;
                default: return MatchResult.Void;
            }
        }
    }
}
