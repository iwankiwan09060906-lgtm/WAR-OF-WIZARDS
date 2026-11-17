// War of Wizards BotLab — 실제 MatchSimulation(서버 판정 코드 그대로)을 Unity 없이 돌리는 검사 · 측정기.
//
//   dotnet run -c Release -- demotest            # 전시 시연: 6인 고정 · 부활 · 스킬 로테이션 · AI 자리 넘겨받기 · 채운 육각형 레인
//   dotnet run -c Release -- polytest | ffatest  # 다인전 · 4인 개인전 규칙 검사
//   dotnet run -c Release -- poly [판] [초] | ffa [판] [초] | ffatrace [시드] [초]
//   dotnet run -c Release -- skilltest           # 스킬 19종 · 룬 인식
// (VLM · 로컬 모델 AI 전략 두뇌 측정(diag · tune · vlm)은 AI 두뇌와 함께 삭제)
using System;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        string cmd = args.Length > 0 ? args[0] : "demotest";
        switch (cmd)
        {
            case "demotest": return Demo.Test();
            case "ffa": return Ffa.Measure(args.Length > 1 ? int.Parse(args[1]) : 200, args.Length > 2 ? float.Parse(args[2]) : 900f);
            case "ffatest": return Ffa.Test();
            case "ffatrace": return Ffa.Trace(args.Length > 1 ? uint.Parse(args[1]) : 1u, args.Length > 2 ? float.Parse(args[2]) : 900f);
            case "polytest": return Polygon.Test();
            case "poly": return Polygon.Measure(args.Length > 1 ? int.Parse(args[1]) : 40, args.Length > 2 ? float.Parse(args[2]) : 1200f);
            case "skilltest": return SkillTest.Run();
            case "balance": return Balance.Run();
            default:
                Console.WriteLine("사용법: demotest | polytest | ffatest | poly [판] [초] | ffa [판] [초] | ffatrace [시드] [초] | skilltest");
                return 1;
        }
    }
}
