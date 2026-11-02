// 맵 밖 스킬 · 시전 준비 표시 검사 — 실제 MatchSimulation 으로 스킬을 쏘고 틱마다 확인한다.
//
//   dotnet run -c Release -- skilltest    (실패하면 exit 1)
using System;
using SpellboundVR.Arena;
using SpellboundVR.Bot;
using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Gesture;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using UnityEngine;
using Random = System.Random;

internal static class SkillTest
{
    private static int s_failed;

    private static void Check(bool ok, string what)
    {
        Console.WriteLine($"  {(ok ? "통과" : "실패")}  {what}");
        if (!ok) s_failed++;
    }

    /// <summary>실제 씬과 같은 치수</summary>
    private static ArenaGeometry Duel() => new ArenaGeometry(Vector3.zero, Vector3.forward, 41f, 5f, 5f, 13f, 6f);

    private static BotConfig Config(float windup)
    {
        var cfg = BotConfig.CreateDefault();
        cfg.UseServerStrategy = false;
        cfg.ReportExperience = false;
        cfg.CastWindupSeconds = windup;
        return cfg;
    }

    /// <summary>스킬 하나만 든 사람 2명 — 봇 없이 서로 때릴 수 있는 상태까지</summary>
    private static MatchSimulation Start(ArenaGeometry arena, int spellId, out MatchRuleConfig rules)
    {
        rules = MatchRuleConfig.CreateDefault();
        if (arena.Mode == ArenaMode.Ffa4) rules.ApplyFfaRules();
        var sim = new MatchSimulation(rules, SpellCatalog.CreateDefault(), arena, Config(0f), null, null, null, 12345u) { LogCasts = false, AllowTestCasts = true };
        for (int t = 0; t < arena.TeamCount; t++) sim.JoinHuman((Team)t, new[] { spellId, 0, 0, 0, 0, 0, 0, 0 });
        for (int i = 0; i < 2000 && sim.Phase != MatchPhase.Playing; i++) sim.Tick(rules.LocalTickInterval);
        return sim;
    }

    private static void Cast(MatchSimulation sim, Team team, int spellId, Column column, float depth) =>
        sim.SubmitCast(team, new SpellCastRequest { SpellId = spellId, SlotIndex = MatchSimulation.TestCastSlot, TargetColumn = column, TargetDepth = depth });

    private static bool OutsideField(ArenaGeometry arena, Vector2 p) => (arena.ClampToField(p) - p).magnitude > 5f;

    private static ProjectileState FirstProjectile(MatchSimulation sim)
    {
        foreach (var p in sim.Projectiles.Pool) if (p.Active) return p;
        return null;
    }

    /// <summary>쏜 뒤 다음 틱의 투사체 — 맵 밖에서 출발해 정해진 시간 뒤 도착하는가</summary>
    private static void CheckFlight(int spellId, Column column, float depth, string name)
    {
        var def = SpellCatalog.CreateDefault().Get(spellId);
        foreach (var arena in new[] { Duel(), Ffa.Arena() })
        {
            var sim = Start(arena, spellId, out var rules);
            float dt = rules.LocalTickInterval;
            Cast(sim, Team.Home, spellId, column, depth);
            sim.Tick(dt);
            var shot = FirstProjectile(sim);
            string mode = arena.IsDuel ? "1:1" : "개인전";
            Check(shot != null && OutsideField(arena, shot.Position), $"{name} ({mode}): 맵 밖에서 출발 {(shot != null ? shot.Position.ToString() : "투사체 없음")}");
            if (shot == null) continue;
            int id = shot.Id;
            float flown = dt;
            for (int i = 0; i < 400 && shot.Active && shot.Id == id; i++)
            {
                sim.Tick(dt);
                flown += dt;
            }
            Check(Mathf.Abs(flown - def.OffMapFlightSeconds) < 0.25f, $"{name} ({mode}): 비행 {flown:0.00}초 (설정 {def.OffMapFlightSeconds:0.0}초)");
        }
    }

    private static void FireballHits()
    {
        var arena = Duel();
        var sim = Start(arena, SpellIds.S03_Fireball, out var rules);
        var enemy = sim.GetPlayer(Team.Away);
        sim.Players[(int)Team.Away].Hp = 500f;
        float before = enemy.Hp;
        sim.Defense.Tower(Team.Away, Column.Center).Alive = false; // 타워 감쇄 없이 재기
        // 상대 발판 = 깊이 1, 가운데 열
        Cast(sim, Team.Home, SpellIds.S03_Fireball, Column.Center, 1f);
        for (int i = 0; i < 90; i++) sim.Tick(rules.LocalTickInterval);
        Check(enemy.Hp < before, $"파이어볼: 맵 밖에서 날아와 조준 지점 상대에게 피해 ({before:0} → {enemy.Hp:0})");
    }

    /// <summary>토네이도는 맵 밖에서 레인 축을 따라 들어와 조준한 레인만 쓴다 (가로지르지 않는다)</summary>
    private static void TornadoFollowsLane()
    {
        foreach (var arena in new[] { Duel(), Ffa.Arena() })
        {
            string mode = arena.IsDuel ? "1:1" : "개인전";
            var sim = Start(arena, SpellIds.S05_Tornado, out var rules);
            float dt = rules.LocalTickInterval;
            // 타워 사격이 섞이면 누가 맞혔는지 알 수 없다 — 피해는 토네이도 것만 남긴다
            for (int t = 0; t < arena.TeamCount; t++)
                for (int c = 0; c < TeamUtil.ColumnCount; c++) sim.Defense.Tower((Team)t, (Column)c).Alive = false;
            var aimed = arena.ResolveLane(Team.Home, Column.Left);
            var other = arena.ResolveLane(Team.Home, Column.Right);
            var lane = arena.GetLane(Team.Home, aimed);
            var melee = sim.GetMinionDefinition(MinionKind.Melee);
            float depth = 0.6f;
            float along = lane.Length * depth;
            var enemy = arena.LaneTarget(Team.Home, aimed);
            sim.SpawnUnit(enemy, melee, aimed, lane.Point(along));
            var near = arena.GetLane(Team.Home, other);
            sim.SpawnUnit(arena.LaneTarget(Team.Home, other), melee, other, near.Point(near.Length * depth));
            UnitState onLane = null, offLane = null;
            foreach (var u in sim.Units)
            {
                if (!u.Active) continue;
                if (u.Lane == aimed) onLane = u; else offLane = u;
            }
            float hpOn = onLane.Hp, hpOff = offLane.Hp;
            Cast(sim, Team.Home, SpellIds.S05_Tornado, Column.Left, 0f);
            sim.Tick(dt);
            var shot = FirstProjectile(sim);
            Check(shot != null && lane.Along(shot.Position) < -5f, $"토네이도 ({mode}): 시전자 뒤 맵 밖에서 출발 (레인 {(shot != null ? lane.Along(shot.Position) : 0f):0.0}m)");
            float worst = 0f;
            int id = shot != null ? shot.Id : 0;
            for (int i = 0; i < 100; i++)
            {
                sim.Tick(dt);
                if (shot != null && shot.Active && shot.Id == id) worst = Mathf.Max(worst, Mathf.Abs(lane.Lateral(shot.Position)));
            }
            Check(worst < 0.1f, $"토네이도 ({mode}): 레인 가운데를 따라간다 (옆으로 벗어난 거리 {worst:0.00}m)");
            Check(onLane.Hp < hpOn && offLane.Hp >= hpOff, $"토네이도 ({mode}): 조준한 레인의 유닛만 맞는다 ({hpOn:0}→{onLane.Hp:0} / 옆 레인 {hpOff:0}→{offLane.Hp:0})");
        }
    }

    private sealed class FxLog : IMatchEventSink
    {
        public readonly System.Collections.Generic.List<(int spell, VfxPart part, Vector2 at, int lane)> Fx = new System.Collections.Generic.List<(int, VfxPart, Vector2, int)>();
        public void OnSpellFx(int spellId, VfxPart part, Vector3 p, int worldColumn) => Fx.Add((spellId, part, new Vector2(p.x, p.z), worldColumn));
        public void OnCastAccepted(Team team, int slotIndex, int spellId) { }
        public void OnCastRejected(Team team, int slotIndex, CastRejectReason reason) { }
        public void OnMoveRejected(Team team, int confirmedWorldColumn) { }
        public void OnHit(int targetId, Vector3 arenaPosition, int amount, HitResultKind kind) { }
    }

    /// <summary>4인전: 스킬이 터지는 곳과 날아오는 길(위에서 본 경로)이 모두 조준한 레인 폭 안에 있어야 한다</summary>
    private static void FfaSkillsStayInLane(int spellId, string name, Column column, float depth)
    {
        var arena = Ffa.Arena();
        var sim = Start(arena, spellId, out var rules);
        var log = new FxLog();
        sim.SetEventSink(log);
        float dt = rules.LocalTickInterval;
        int aimed = arena.ResolveLane(Team.Home, column);
        var lane = arena.GetLane(Team.Home, aimed);
        float half = arena.LaneHalfWidth;
        Cast(sim, Team.Home, spellId, column, depth);
        float worstFlight = 0f;
        for (int i = 0; i < 150; i++)
        {
            sim.Tick(dt);
            foreach (var p in sim.Projectiles.Pool)
                if (p.Active) worstFlight = Mathf.Max(worstFlight, Mathf.Abs(lane.Lateral(p.Position)));
        }
        float worstFx = 0f;
        int fx = 0;
        foreach (var f in log.Fx)
        {
            if (f.part == VfxPart.Cast || f.spell != spellId) continue;
            fx++;
            worstFx = Mathf.Max(worstFx, Mathf.Abs(lane.Lateral(f.at)));
        }
        Check(worstFlight <= half && worstFx <= half, $"개인전 {name}: 레인 안 (반폭 {half:0.0}m · 경로 최대 {worstFlight:0.0}m · 효과 {fx}개 최대 {worstFx:0.0}m)");
    }

    private static void FfaCometsLandOnLanes()
    {
        var arena = Ffa.Arena();
        var sim = Start(arena, SpellIds.H01_Freeze, out var rules);
        Cast(sim, Team.Home, SpellIds.H01_Freeze, Column.Center, 0f);
        sim.Tick(rules.LocalTickInterval);
        int onLane = 0, comets = 0;
        foreach (var p in sim.Projectiles.Pool)
        {
            if (!p.Active) continue;
            comets++;
            for (int c = 0; c < 3; c++)
            {
                var lane = arena.GetLane(Team.Home, arena.ResolveLane(Team.Home, (Column)c));
                if (Mathf.Abs(lane.Lateral(p.TargetPoint)) < 0.1f) { onLane++; break; }
            }
        }
        Check(comets == 6 && onLane == 6, $"개인전 프리즈: 혜성 {onLane}/{comets}개가 내 레인 위로 떨어진다");
    }

    /// <summary>
    /// 웹캠으로 그린 궤적 흉내: 사람마다 다른 기울임 · 비율 · 느린 흔들림 + 손떨림, 평활, 좌표는 16:9 화면 비율.
    /// 가로세로를 맞춘 것과 맞추지 않은 것을 실제 인식기(19종 전부 후보)로 비교한다.
    /// </summary>
    private static void WebcamRunes()
    {
        const float aspect = 16f / 9f;
        const float size = 0.55f;     // 룬 높이 = 화면 높이의 55%
        const int trials = 40;
        var recognizer = new RuneRecognizer();
        foreach (var d in RuneDesigns.All)
            recognizer.AddTemplates(RuneRecognizer.BuildTemplateFeatures(d.SpellId, d.Name, RuneDesigns.Samples(d)));
        var settings = new GestureValidationSettings();
        foreach (float wobble in new[] { 0.02f, 0.04f, 0.06f })
        {
            var rng = new Random(4242);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int okFixed = 0, okRaw = 0, wrongFixed = 0, wrongRaw = 0, total = 0;
            foreach (var d in RuneDesigns.All)
            {
                for (int t = 0; t < trials; t++)
                {
                    total++;
                    float rot = Range(-8f, 8f) * Mathf.Deg2Rad, shear = Range(-0.12f, 0.12f), stretch = Range(0.88f, 1.14f);
                    float phase = Range(0f, 6f), freq = Range(4f, 9f);
                    float cos = Mathf.Cos(rot), sin = Mathf.Sin(rot);
                    var raw = new Vector2[d.Strokes.Length][];
                    var corrected = new Vector2[d.Strokes.Length][];
                    for (int s = 0; s < d.Strokes.Length; s++)
                    {
                        var stroke = d.Strokes[s];
                        raw[s] = new Vector2[stroke.Length];
                        corrected[s] = new Vector2[stroke.Length];
                        Vector2 smooth = Vector2.zero;
                        for (int i = 0; i < stroke.Length; i++)
                        {
                            float u = (float)i / Mathf.Max(1, stroke.Length - 1);
                            float x = (stroke[i].x - 0.5f) * stretch + shear * (stroke[i].y - 0.5f);
                            float y = stroke[i].y - 0.5f;
                            var p = new Vector2(x * cos - y * sin + wobble * Mathf.Sin(u * freq + phase) + Range(-0.012f, 0.012f),
                                                x * sin + y * cos + wobble * Mathf.Cos(u * freq * 1.3f + phase) + Range(-0.012f, 0.012f));
                            // 화면 비율 좌표: 가로는 화면 폭 기준이라 aspect 로 나눈 만큼 좁게 찍힌다
                            var screen = new Vector2(p.x * size / aspect + 0.5f, p.y * size + 0.5f);
                            smooth = i == 0 ? screen : smooth * 0.5f + screen * 0.5f;
                            raw[s][i] = smooth;
                            corrected[s][i] = new Vector2(smooth.x * aspect, smooth.y);
                        }
                    }
                    var a = recognizer.Recognize(raw);
                    if (GestureValidator.ValidateMatch(a, settings) == GestureRejectReason.None)
                    {
                        if (a.SpellId == d.SpellId) okRaw++;
                        else wrongRaw++;
                    }
                    var b = recognizer.Recognize(corrected);
                    if (GestureValidator.ValidateMatch(b, settings) == GestureRejectReason.None)
                    {
                        if (b.SpellId == d.SpellId) okFixed++;
                        else wrongFixed++;
                    }
                }
            }
            Console.WriteLine($"    웹캠 궤적 {total}개 (흔들림 {wobble:0.00}): 보정 전 정답 {100f * okRaw / total:0.0}% · 오인 {100f * wrongRaw / total:0.0}%  →  보정 후 정답 {100f * okFixed / total:0.0}% · 오인 {100f * wrongFixed / total:0.0}%");
            if (wobble < 0.03f) Check(okFixed >= okRaw && okFixed >= total * 0.8f, "웹캠 룬: 화면비를 맞춘 궤적을 알아본다");
        }
    }

    private static void SummonLands()
    {
        var arena = Duel();
        var sim = Start(arena, SpellIds.S11_AxeWarrior, out var rules);
        float dt = rules.LocalTickInterval;
        Cast(sim, Team.Home, SpellIds.S11_AxeWarrior, Column.Center, 0f);
        sim.Tick(dt);
        Check(sim.FindTeamUnit(Team.Home, MinionKind.AxeOrc) == 0, "도끼 전사: 날아오는 동안에는 아직 유닛이 아니다");
        var shot = FirstProjectile(sim);
        Check(shot != null && shot.Height > 10f, $"도끼 전사: 하늘 높이에서 출발 ({(shot != null ? shot.Height : 0f):0}m)");
        for (int i = 0; i < 60; i++) sim.Tick(dt);
        Check(sim.FindTeamUnit(Team.Home, MinionKind.AxeOrc) != 0, "도끼 전사: 착지하면 유닛이 된다");
    }

    private static void FreezeWaits()
    {
        var arena = Duel();
        var sim = Start(arena, SpellIds.H01_Freeze, out var rules);
        float dt = rules.LocalTickInterval;
        sim.SpawnUnit(Team.Away, sim.GetMinionDefinition(MinionKind.Melee), 1, new Vector2(0f, 20f));
        UnitState unit = null;
        foreach (var u in sim.Units) if (u.Active) unit = u;
        Cast(sim, Team.Home, SpellIds.H01_Freeze, Column.Center, 0f);
        sim.Tick(dt);
        int comets = 0;
        foreach (var p in sim.Projectiles.Pool) if (p.Active) comets++;
        float hp = unit.Hp;
        Check(comets == 6, $"프리즈: 혜성 6개가 사방에서 ({comets}개)");
        for (int i = 0; i < 10; i++) sim.Tick(dt);
        Check(unit.Hp == hp, "프리즈: 혜성이 닿기 전에는 아직 피해 없음");
        for (int i = 0; i < 40; i++) sim.Tick(dt);
        Check(unit.Hp < hp, $"프리즈: 혜성이 닿으면 피해 ({hp:0} → {unit.Hp:0})");
    }

    private static void LaneSpellsStayInLane()
    {
        var sim = Start(Duel(), SpellIds.S07_Gatling, out var rules);
        Cast(sim, Team.Home, SpellIds.S07_Gatling, Column.Center, 0f);
        for (int i = 0; i < 3; i++) sim.Tick(rules.LocalTickInterval);
        var shot = FirstProjectile(sim);
        Check(shot != null && !OutsideField(sim.Arena, shot.Position), "총난사: OffMap 없는 스킬은 예전처럼 손에서 나간다");
    }

    private static void BotShowsWindup()
    {
        var rules = MatchRuleConfig.CreateDefault();
        var catalog = SpellCatalog.CreateDefault();
        var cfg = Config(3f);
        var sim = new MatchSimulation(rules, catalog, Duel(), cfg, null, null, null, 77u) { LogCasts = false };
        sim.JoinHuman(Team.Home, new[] { SpellIds.S03_Fireball, 0, 0, 0, 0, 0, 0, 0 });
        sim.JoinBot(Team.Away, new OneCastBrain(sim.GetDeck(Team.Away)));
        float dt = rules.LocalTickInterval;
        for (int i = 0; i < 2000 && sim.Phase != MatchPhase.Playing; i++) sim.Tick(dt);
        var snap = new MatchSnapshot();
        float firstSeen = -1f, castAt = -1f;
        byte lastProgress = 0;
        bool grew = false;
        bool anyAim = false;
        for (int i = 0; i < 300 && castAt < 0f; i++)
        {
            sim.Tick(dt);
            sim.WriteSnapshot(snap);
            ref var p = ref snap.Players[(int)Team.Away];
            if (p.CastPhase == (byte)CastPreviewPhase.Preparing)
            {
                if (firstSeen < 0f)
                {
                    firstSeen = sim.MatchTime;
                    Check(p.CastSpellId == sim.GetDeck(Team.Away)[0], $"봇: 준비 중인 스킬이 보인다 ({SpellIds.Code(p.CastSpellId)})");
                }
                grew |= p.CastProgress > lastProgress;
                lastProgress = p.CastProgress;
                anyAim |= p.CastLane != PlayerSnap.NoCastLane;
            }
            if (sim.GetPlayer(Team.Away).Slots[0].State != SlotState.Ready) castAt = sim.MatchTime;
        }
        Check(firstSeen >= 0f, "봇: 시전 전에 준비 단계가 스냅샷에 나온다");
        Check(grew, $"봇: 진행률이 오른다 (마지막 {lastProgress}/255)");
        Check(anyAim, "봇: 노리는 레인이 보인다");
        Check(castAt - firstSeen > 2.8f, $"봇: 준비를 {castAt - firstSeen:0.00}초 보여 준 뒤 시전 (설정 3초)");
        for (int i = 0; i < 5; i++) sim.Tick(dt);
        sim.WriteSnapshot(snap);
        Check(snap.Players[(int)Team.Away].CastPhase == 0, "봇: 시전하면 준비 표시가 사라진다");
    }

    /// <summary>경기 시작 0.5초 뒤 슬롯 0 을 한 번 쓴다 (봇 덱은 랜덤)</summary>
    private sealed class OneCastBrain : IBotBrain
    {
        private readonly int[] _deck;
        private bool _done;

        public OneCastBrain(int[] deck) => _deck = deck;

        public void Tick(in BotObservation obs, ref BotCommand cmd)
        {
            if (_done || obs.MatchTimeSeconds < 0.5f) return;
            _done = true;
            cmd.HasCastRequest = true;
            cmd.CastRequest = new SpellCastRequest { SpellId = _deck[0], SlotIndex = 0, TargetColumn = Column.Center, TargetDepth = 0.5f };
        }
    }

    private static void HumanPreview()
    {
        var sim = Start(Duel(), SpellIds.S03_Fireball, out var rules);
        float dt = rules.LocalTickInterval;
        var snap = new MatchSnapshot();

        sim.SetCastPreview(Team.Home, new CastPreview { Phase = CastPreviewPhase.Drawing, PenX = 0.5f, PenY = -0.25f, Stroke = 2 });
        sim.WriteSnapshot(snap);
        ref var p = ref snap.Players[(int)Team.Home];
        Check(p.CastPhase == 1 && p.PenX == 50 && p.PenY == -25 && p.PenStroke == 2 && p.CastSpellId == 0, "사람: 그리는 손끝 궤적이 스킬 없이 전달된다");

        sim.SetCastPreview(Team.Home, new CastPreview { Phase = CastPreviewPhase.Preparing, SpellId = 9999, Aiming = true, TargetColumn = Column.Left, Progress = 3f });
        sim.WriteSnapshot(snap);
        Check(snap.Players[0].CastSpellId == 0 && snap.Players[0].CastProgress == 255, "사람: 없는 스킬 · 범위 밖 진행률은 걸러진다");

        sim.SetCastPreview(Team.Home, new CastPreview { Phase = CastPreviewPhase.Preparing, SpellId = SpellIds.S03_Fireball, Aiming = true, TargetColumn = Column.Left, Progress = 0.5f });
        sim.WriteSnapshot(snap);
        Check(snap.Players[0].CastLane == sim.Arena.ResolveLane(Team.Home, Column.Left), "사람: 조준 열이 월드 레인으로 바뀐다");

        for (int i = 0; i < 100; i++) sim.Tick(dt);
        sim.WriteSnapshot(snap);
        Check(snap.Players[0].CastPhase == 0, "사람: 보고가 끊기면(접속 끊김) 준비 표시가 저절로 사라진다");

        sim.SetCastPreview(Team.Home, new CastPreview { Phase = CastPreviewPhase.Preparing, SpellId = SpellIds.S03_Fireball });
        sim.GetPlayer(Team.Home).Hp = 0f;
        sim.Tick(dt);
        sim.WriteSnapshot(snap);
        Check(snap.Players[0].CastPhase == 0, "사람: 쓰러지면 준비 표시가 사라진다");
    }

    public static int Run()
    {
        CheckFlight(SpellIds.S03_Fireball, Column.Center, 0.8f, "파이어볼");
        CheckFlight(SpellIds.S02_PoisonField, Column.Left, 0.5f, "독병");
        CheckFlight(SpellIds.S04_ArrowRain, Column.Right, 0.6f, "화살");
        CheckFlight(SpellIds.S01_Smoke, Column.Center, 0f, "연막탄");
        CheckFlight(SpellIds.S12_Mage, Column.Center, 0f, "드래곤");
        FireballHits();
        TornadoFollowsLane();
        FfaSkillsStayInLane(SpellIds.S02_PoisonField, "독 장판", Column.Left, 0.5f);
        FfaSkillsStayInLane(SpellIds.S03_Fireball, "파이어볼", Column.Left, 0.5f);
        FfaSkillsStayInLane(SpellIds.S04_ArrowRain, "화살 세례", Column.Right, 0.5f);
        FfaSkillsStayInLane(SpellIds.S06_Lightning, "번개", Column.Left, 0.5f);
        FfaSkillsStayInLane(SpellIds.H05_Rage, "분노", Column.Center, 0.5f);
        FfaSkillsStayInLane(SpellIds.S12_Mage, "드래곤", Column.Left, 0f);
        FfaCometsLandOnLanes();
        WebcamRunes();
        SummonLands();
        FreezeWaits();
        LaneSpellsStayInLane();
        BotShowsWindup();
        HumanPreview();
        Console.WriteLine(s_failed == 0 ? "■ 맵 밖 스킬 · 시전 준비 검사 통과" : $"■ 실패 {s_failed}개");
        return s_failed == 0 ? 0 : 1;
    }
}
