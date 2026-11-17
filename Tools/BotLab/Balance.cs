// 스킬 밸런스 측정 — 시연 규칙(6인 채운 육각형 · 로테이션)에서 스킬 하나를 한 번 쏘고 40초 동안 들어간 피해를 잰다.
//   로테이션은 쓴 스킬을 대기열 맨 뒤로 보내 19종이 똑같이 자주 나온다 → 한 번 시전의 값어치가 비슷해야 한다.
//   상대 5명은 가만히 서 있는 사람(봇 아님), 웨이브 없음 — 그 스킬이 낸 피해만 남는다.
//   dotnet run -c Release -- balance
using System;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;

internal static class Balance
{
    private sealed class Sink : IMatchEventSink
    {
        public float Damage;
        public int Accepted;
        public void OnSpellFx(int spellId, VfxPart part, Vector3 p, int c) { }
        public void OnCastAccepted(Team team, int slot, int spellId) => Accepted++;
        public void OnCastRejected(Team team, int slot, CastRejectReason r) { }
        public void OnMoveRejected(Team team, int c) { }
        public void OnHit(int id, Vector3 p, int amount, HitResultKind k) { if (amount > 0) Damage += amount; }
    }

    public static int Run()
    {
        var catalog = SpellCatalog.CreateDefault();
        var rules0 = MatchRuleConfig.CreateDefault();
        rules0.ApplyDemoRules();
        Console.WriteLine($"시연 플레이어 체력 {rules0.PlayerMaxHp:0} · 40초 동안 한 번 시전의 피해 (상대 플레이어 / 전체)");
        foreach (int id in SpellDefinition.AllIds)
        {
            var def = catalog.Get(id);
            var rules = MatchRuleConfig.CreateDefault();
            rules.ApplyDemoRules();
            rules.FirstWaveDelay = 1e9f;
            rules.SkillRotation = false; // 로테이션이 덱을 섞지 않게 — 슬롯 0 = 이 스킬
            var arena = Polygon.Arena();
            arena.FilledPolygon = true;
            var sink = new Sink();
            var sim = new MatchSimulation(rules, catalog, arena, SpellboundVR.Bot.BotConfig.CreateDefault(), null, null, null, 7u) { LogCasts = false, AllowTestCasts = true };
            sim.SetEventSink(sink);
            for (int t = 0; t < arena.TeamCount; t++) sim.JoinHuman((Team)t, new[] { id, 0, 0, 0, 0, 0, 0, 0 });
            float dt = rules.LocalTickInterval;
            for (int i = 0; i < 4000 && sim.Phase != MatchPhase.Playing; i++) sim.Tick(dt);
            float hpBefore = EnemyHp(sim, arena.TeamCount);
            sim.SubmitCast(Team.Home, new SpellCastRequest { SpellId = id, SlotIndex = 0, TargetColumn = Column.Center, TargetDepth = 1f });
            int maxUnits = 0;
            for (float t = 0f; t < 40f; t += dt)
            {
                sim.Tick(dt);
                int n = 0;
                foreach (var u in sim.Units) if (u.Active) n++;
                maxUnits = Math.Max(maxUnits, n);
            }
            float playerDamage = hpBefore - EnemyHp(sim, arena.TeamCount);
            Console.WriteLine($"  {SpellIds.Code(id),-4} {def.DisplayName,-16} {(def.Tier == SpellTier.HighPower ? "고위력" : "일반"),-4} 플레이어 {playerDamage,6:0}  전체 {sink.Damage,6:0}  유닛 {maxUnits}{(sink.Accepted == 0 ? "  (시전 거절)" : "")}");
        }
        return 0;
    }

    /// <summary>Home 을 뺀 상대 체력 합 — 부활(시연)하면 Deaths 만큼 최대 체력을 더해 잃은 양을 그대로 센다</summary>
    private static float EnemyHp(MatchSimulation sim, int teams)
    {
        float hp = 0f;
        for (int t = 1; t < teams; t++)
        {
            var p = sim.Players[t];
            hp += p.Hp - p.Deaths * p.MaxHp;
        }
        return hp;
    }
}
