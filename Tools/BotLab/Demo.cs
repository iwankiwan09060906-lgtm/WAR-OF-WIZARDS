// 전시 시연 모드 검사 — 6인 고정 · 끝나지 않는 경기 · 쓰러지면 부활 · 19종 스킬 로테이션 · AI 자리 넘겨받기 · 채운 육각형(모든 상대에게 레인)
//   dotnet run -c Release -- demotest
using System;
using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Deck;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;

internal static class Demo
{
    private static int _fails;

    private static void Check(bool ok, string what)
    {
        if (ok) return;
        _fails++;
        Console.WriteLine("  [실패] " + what);
    }

    private sealed class Sink : IMatchEventSink
    {
        public readonly HashSet<int> Cast = new HashSet<int>();
        public int Accepted;
        public void OnSpellFx(int spellId, VfxPart part, Vector3 p, int c) { }
        public void OnCastAccepted(Team team, int slot, int spellId) { Accepted++; Cast.Add(spellId); }
        public void OnCastRejected(Team team, int slot, CastRejectReason r) { }
        public void OnMoveRejected(Team team, int c) { }
        public void OnHit(int id, Vector3 p, int amount, HitResultKind k) { }
    }

    private static MatchSimulation NewSim(uint seed, Sink sink)
    {
        var rules = MatchRuleConfig.CreateDefault();
        rules.ApplyDemoRules();
        var arena = Polygon.Arena();
        arena.FilledPolygon = true;
        var sim = new MatchSimulation(rules, SpellCatalog.CreateDefault(), arena, BotConfig.CreateDefault(), null, null, null, seed)
        {
            LogCasts = false,
            AllowTestCasts = true,
        };
        sim.SetEventSink(sink);
        // 서버(NetworkMatchState.TickBotFill)처럼 사람 없이 빈자리를 모두 AI 로
        while (sim.TryGetFreeTeam(out Team free)) sim.JoinBot(free);
        return sim;
    }

    private static void Run(MatchSimulation sim, float seconds)
    {
        float dt = sim.Rules.LocalTickInterval;
        for (float t = 0f; t < seconds; t += dt) sim.Tick(dt);
    }

    private static int UnitsOf(MatchSimulation sim, Team team)
    {
        int n = 0;
        foreach (var u in sim.Units) if (u.Active && !u.Dead && u.Team == team) n++;
        return n;
    }

    public static int Test()
    {
        Console.WriteLine("[demotest] 전시 시연 모드");
        _fails = 0;
        var sink = new Sink();
        var sim = NewSim(4242u, sink);
        Run(sim, 4f); // 카운트다운
        Check(sim.Phase == MatchPhase.Playing, "사람 없이도 AI 6명으로 경기가 시작된다");
        Check(sim.Arena.RingCount == 6, "정육각형 6자리");

        // 채운 육각형: 대각선도 소환 · 웨이브 레인 → 첫 웨이브에 진영마다 레인 5개
        Run(sim, sim.Rules.FirstWaveDelay + 0.5f);
        for (int t = 0; t < 6; t++)
        {
            var lanes = new HashSet<int>();
            foreach (var u in sim.Units)
                if (u.Active && u.Team == (Team)t && u.Def.Kind == MinionKind.Melee) lanes.Add(u.Lane);
            Check(lanes.Count == 5, $"{(Team)t} 첫 웨이브 레인 {lanes.Count}개 (5개여야)");
        }

        // 소환 · 설치는 조준선의 절반까지 (대각선 레인 · 깊이 0.9 로 요청)
        CheckPlacement(sim, SpellIds.S11_AxeWarrior, MinionKind.AxeOrc);
        CheckPlacement(sim, SpellIds.S13_Turret, MinionKind.Turret);

        // 10분: 끝나지 않고, 쓰러지면 부활, 근접 미니언만, 로테이션은 늘 4장 · 겹치지 않음
        int deaths = 0;
        bool onlyMelee = true, decksOk = true;
        for (int step = 0; step < 600; step++)
        {
            Run(sim, 1f);
            foreach (var u in sim.Units)
                if (u.Active && (u.Def.Kind == MinionKind.Ranged || u.Def.Kind == MinionKind.Brute)) onlyMelee = false;
            for (int t = 0; t < 6; t++)
            {
                var deck = sim.GetDeck((Team)t);
                int filled = 0;
                for (int i = 0; i < DeckData.SlotCount; i++)
                {
                    if (deck[i] <= 0) continue;
                    filled++;
                    if (Array.IndexOf(deck, deck[i]) != i) decksOk = false;
                    if (sim.GetPlayer((Team)t).Slots[i].SpellId != deck[i]) decksOk = false;
                }
                if (filled != sim.Rules.RotationSlots) decksOk = false;
            }
        }
        for (int t = 0; t < 6; t++)
        {
            var p = sim.GetPlayer((Team)t);
            deaths += p.Deaths;
            Check(p.Present && p.Alive && !p.Eliminated, $"{(Team)t} 10분 뒤에도 자리에 살아 있다");
        }
        Check(sim.Phase == MatchPhase.Playing, "10분 뒤에도 경기가 계속된다 (끝나지 않음)");
        Check(sim.Arena.RingCount == 6, "탈락 없이 6각형 유지");
        Check(deaths > 0, "쓰러진 사람이 부활했다 (부활 " + deaths + "번)");
        Check(onlyMelee, "원거리 · 대형 미니언이 나오지 않는다");
        Check(decksOk, "로테이션 슬롯은 늘 4장 · 같은 스킬이 두 슬롯에 오지 않는다 · 서버 슬롯과 덱이 같다");
        Check(sink.Cast.Count >= 15, $"봇이 여러 스킬을 쓴다 (10분 동안 {sink.Cast.Count}종 / 19종)");
        Console.WriteLine($"  10분: 부활 {deaths}번 · 시전 {sink.Accepted}번 · 쓴 스킬 {sink.Cast.Count}종 · 유닛 {sim.Units.Length}칸 중 사용 {CountActive(sim)}");

        CheckQueueAndClient();

        // AI 자리 넘겨받기: 알림 3초 동안은 AI, 그 뒤 사람 — HP · 슬롯 · 유닛은 그대로
        Check(sim.TryReserveSeat(out Team seat, out bool takeover) && takeover, "빈자리가 없으면 AI 자리를 예약한다");
        int seatNo = sim.NextTakeover(out float left);
        Check(seatNo == TeamUtil.SeatNumber(seat) && left > 2.9f, $"입장 알림 자리 {seatNo} · {left:0.0}초");
        var before = sim.GetPlayer(seat);
        float hp = before.Hp;
        int[] deckBefore = sim.GetDeck(seat).Clone() as int[];
        int unitsBefore = UnitsOf(sim, seat);
        Run(sim, 1f);
        Check(sim.GetPlayer(seat).IsBot, "알림 중에는 AI 가 계속 앉아 있다");
        hp = sim.GetPlayer(seat).Hp;
        deckBefore = sim.GetDeck(seat).Clone() as int[];
        unitsBefore = UnitsOf(sim, seat);
        sim.SetDeck(seat, DeckData.CreateDefault().ToArray()); // 접속한 Quest 가 덱을 보내도
        Run(sim, 2.05f);
        var after = sim.GetPlayer(seat);
        Check(!after.IsBot && after.Present, "3초 뒤 사람이 그 자리를 넘겨받는다");
        Check(sim.NextTakeover(out _) == 0, "알림이 사라진다");
        Check(Mathf.Abs(after.Hp - hp) < hp * 0.5f + 1f, "HP 를 이어받는다");
        var deckAfter = sim.GetDeck(seat);
        bool sameDeck = true;
        for (int i = 0; i < DeckData.SlotCount; i++) if (deckAfter[i] != deckBefore[i]) sameDeck = false;
        Check(sameDeck, "AI 가 쓰던 스킬(로테이션 슬롯)을 그대로 이어받는다 (클라이언트 덱은 무시)");
        Check(UnitsOf(sim, seat) > 0 || unitsBefore == 0, "AI 의 유닛 · 설치물이 사라지지 않는다");

        // 사람이 나가면 다시 AI 가 잇는다, 알림 중에 나가면 예약만 취소
        sim.NotifyDisconnected(seat);
        Check(sim.GetPlayer(seat).IsBot && sim.GetPlayer(seat).Present, "사람이 나가면 AI 가 그 자리를 잇는다");
        Check(sim.TryReserveSeat(out Team seat2, out _), "다시 예약");
        sim.NotifyDisconnected(seat2);
        Run(sim, 4f);
        Check(sim.GetPlayer(seat2).IsBot, "알림 중에 나가면 AI 가 계속 앉아 있다");

        // 6명 모두 사람이면 더는 자리가 없다 (관전)
        int reserved = 0;
        while (sim.TryReserveSeat(out _, out _)) reserved++;
        Run(sim, 3.1f);
        Check(reserved == 6 && !sim.TryReserveSeat(out _, out _), $"사람 6명까지 ({reserved}명 예약)");
        Check(sim.HumanCount == 6 && sim.Phase == MatchPhase.Playing, "사람 6명 경기");

        Console.WriteLine(_fails == 0 ? "[demotest] 통과" : $"[demotest] 실패 {_fails}개");
        return _fails == 0 ? 0 : 1;
    }

    /// <summary>
    /// 사람(1번)이 스킬창 4칸 중 하나를 쓰면: 그 스킬은 대기열 맨 뒤로, 대기열 맨 앞이 그 칸에 — 19번 쓰면 19종을 모두 써 본다.
    /// 클라이언트 이벤트 발행자(HUD · 룬 후보가 보는 것)도 바뀐 칸 스킬을 알린다
    /// </summary>
    private static void CheckQueueAndClient()
    {
        var sim = NewSim(99u, new Sink());
        sim.RemovePlayer(Team.Home);
        sim.JoinHuman(Team.Home, new int[DeckData.SlotCount]);
        var events = new NetworkGameEventPublisher(sim.Arena);
        sim.SetEventSink(events);
        events.SetLocalTeam(Team.Home);
        var shown = new int[DeckData.SlotCount];
        int changes = 0;
        events.OnSlotSpellChanged += (slot, id) => { shown[slot] = id; changes++; };
        var snap = new MatchSnapshot();
        void Step(float seconds)
        {
            Run(sim, seconds);
            sim.WriteSnapshot(snap);
            events.ApplySnapshot(snap);
        }
        Step(4f);
        Check(sim.Phase == MatchPhase.Playing, "큐 검사: 경기 시작");
        var queue = sim.RotationQueue(Team.Home);
        Check(queue != null && queue.Count == 15, $"대기열 15장 (스킬창 4 + 대기열 = 19), 지금 {queue?.Count}");
        var tried = new HashSet<int>();
        bool order = true, client = true;
        for (int n = 0; n < 19; n++)
        {
            int slot = n % 4;
            int used = sim.SlotSpell(Team.Home, slot);
            int head = queue[0];
            tried.Add(used);
            sim.DebugResetCooldowns(Team.Home);
            sim.SubmitCast(Team.Home, new SpellCastRequest { SpellId = used, SlotIndex = slot, TargetColumn = Column.Left, TargetDepth = 0.3f });
            Step(0.1f);
            if (sim.SlotSpell(Team.Home, slot) != head || queue[queue.Count - 1] != used) order = false;
            if (shown[slot] != head) client = false;
        }
        Check(order, "쓴 스킬은 대기열 맨 뒤로, 맨 앞 스킬이 그 칸에");
        Check(client, "클라이언트(HUD) 가 바뀐 칸 스킬을 받는다");
        Check(tried.Count == 19, $"19번 쓰면 19종을 모두 써 본다 ({tried.Count}종)");
        Console.WriteLine($"  대기열: 19번 시전 → {tried.Count}종, 칸 스킬 변경 알림 {changes}번");

        // 사람이 쓰러지면: 클라이언트는 게임 오버, 자리는 AI 가 풀피로 잇는다 (6자리 유지)
        Check(sim.GetPlayer(Team.Home).MaxHp >= 6000f, $"시연 체력 = 다인전의 2배 ({sim.GetPlayer(Team.Home).MaxHp})");
        bool over = false;
        events.OnLocalGameOver += () => over = true;
        sim.DebugSetHp(Team.Home, 0f);
        Step(0.1f);
        var home = sim.GetPlayer(Team.Home);
        Check(over, "내가 쓰러지면 클라이언트에 게임 오버");
        Check(home.Present && home.IsBot && home.Alive && home.Hp == home.MaxHp, "쓰러진 사람 자리는 AI 가 풀피로 잇는다");
        over = false;
        sim.DebugSetHp(Team.Home, 0f);
        Step(0.1f);
        Check(!over, "AI 가 앉은 내 자리가 쓰러져도 게임 오버를 다시 띄우지 않는다");
    }

    private static int CountActive(MatchSimulation sim)
    {
        int n = 0;
        foreach (var u in sim.Units) if (u.Active) n++;
        return n;
    }

    /// <summary>대각선 상대(열 3)에게 깊이 0.9 로 소환 · 설치를 요청하면 조준선의 절반 안에 놓인다</summary>
    private static void CheckPlacement(MatchSimulation sim, int spellId, MinionKind kind)
    {
        var known = new HashSet<int>();
        foreach (var u in sim.Units) if (u.Active) known.Add(u.Id);
        sim.SubmitCast(Team.Home, new SpellCastRequest { SpellId = spellId, SlotIndex = MatchSimulation.TestCastSlot, TargetColumn = (Column)3, TargetDepth = 0.9f });
        int lane = sim.Arena.ResolveLane(Team.Home, (Column)3);
        var frame = sim.Arena.GetLane(Team.Home, lane);
        bool found = false;
        float dt = sim.Rules.LocalTickInterval;
        // 맵 밖에서 떨어지는 소환(SummonDrop)은 늦게 나타난다 — 처음 나타난 자리로 본다 (나온 뒤에는 걸어간다)
        for (float t = 0f; t < 5f && !found; t += dt)
        {
            sim.Tick(dt);
            foreach (var u in sim.Units)
            {
                if (!u.Active || known.Contains(u.Id) || u.Team != Team.Home || u.Def.Kind != kind) continue;
                found = true;
                float depth = frame.Along(u.Position) / frame.Length;
                Check(u.Lane == lane, $"{SpellIds.Code(spellId)} 대각선 레인에 놓인다");
                Check(depth <= 0.5f + 0.03f, $"{SpellIds.Code(spellId)} 깊이 {depth:0.00} (조준선 절반 0.5 이하)");
                Console.WriteLine($"  {SpellIds.Code(spellId)} 요청 깊이 0.90 → 놓인 깊이 {depth:0.00}");
            }
        }
        Check(found, $"{SpellIds.Code(spellId)} 대각선(이웃 아닌 상대)에도 소환 · 설치된다");
    }
}
