// C08 랭킹 (§23.1) — 매칭 대전 레이팅 기준 순위 (친선전 · 봇 연습은 레이팅에 반영되지 않는다)

using System;

namespace SpellboundVR.Backend
{
    public static class RankingService
    {
        public static void GetTop(int limit, Action<RankEntryDto[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.GetArray($"/api/players/ranking?limit={limit}", onSuccess, onError);
    }
}
