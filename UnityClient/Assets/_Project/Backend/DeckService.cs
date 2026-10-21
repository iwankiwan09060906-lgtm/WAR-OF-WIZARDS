// C09 마법서(덱 빌더) (§13.1, §23.1) — 19종 중 8장, 중복 불가, 고위력(H) 최대 2장. 서버도 같은 규칙으로 다시 검사한다.
// 서버는 스킬 코드("S03", "H02")로, 게임(DeckData · SpellIds)은 정수 SpellId(3, 102)로 다룬다 → ToSpellIds / ToCodes 로 변환.

using System;
using System.Collections.Generic;

namespace SpellboundVR.Backend
{
    public static class DeckService
    {
        public const int DeckSize = 8;
        public const int MaxHighPower = 2;

        public static void GetMine(Action<string[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.Get<DeckDto>("/api/decks/me", res => onSuccess?.Invoke(res.skills ?? new string[0]), onError);

        /// <summary>규칙 위반이면 서버에 보내지 않고 바로 onError</summary>
        public static void Save(string[] codes, Action<string[]> onSuccess, Action<string> onError = null)
        {
            string problem = Validate(codes);
            if (problem != null)
            {
                onError?.Invoke(problem);
                return;
            }
            BackendClient.Instance.Put<DeckRequestDto, DeckDto>("/api/decks/me", new DeckRequestDto { skills = codes },
                res => onSuccess?.Invoke(res.skills), onError);
        }

        /// <summary>문제가 없으면 null, 있으면 한글 이유</summary>
        public static string Validate(string[] codes)
        {
            if (codes == null || codes.Length != DeckSize) return $"덱은 정확히 {DeckSize}장이어야 합니다. (현재 {codes?.Length ?? 0}장)";
            var seen = new HashSet<string>();
            int highPower = 0;
            foreach (string code in codes)
            {
                if (ToSpellId(code) == 0) return $"존재하지 않는 스킬입니다: {code}";
                if (!seen.Add(code.ToUpperInvariant())) return "같은 스킬을 중복해서 넣을 수 없습니다.";
                if (IsHighPower(code)) highPower++;
            }
            if (highPower > MaxHighPower) return $"고위력 스킬은 최대 {MaxHighPower}장까지만 포함할 수 있습니다. (현재 {highPower}장)";
            return null;
        }

        public static bool IsHighPower(string code) => !string.IsNullOrEmpty(code) && char.ToUpperInvariant(code[0]) == 'H';

        /// <summary>"S03" → 3, "H02" → 102, 잘못된 코드 → 0 (SpellIds 와 같은 규칙)</summary>
        public static int ToSpellId(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length != 3 || !int.TryParse(code.Substring(1), out int n)) return 0;
            switch (char.ToUpperInvariant(code[0]))
            {
                case 'S': return n >= 1 && n <= 13 ? n : 0;
                case 'H': return n >= 1 && n <= 6 ? 100 + n : 0;
                default: return 0;
            }
        }

        /// <summary>3 → "S03", 102 → "H02"</summary>
        public static string ToCode(int spellId) => spellId > 100 ? "H" + (spellId - 100).ToString("00") : "S" + spellId.ToString("00");

        /// <summary>서버 덱 → DeckData 에 넣을 SpellId 배열 (new DeckData(ids))</summary>
        public static int[] ToSpellIds(string[] codes)
        {
            if (codes == null) return new int[0];
            var ids = new int[codes.Length];
            for (int i = 0; i < codes.Length; i++) ids[i] = ToSpellId(codes[i]);
            return ids;
        }

        /// <summary>DeckData.ToArray() → 서버에 저장할 코드 배열</summary>
        public static string[] ToCodes(int[] spellIds)
        {
            if (spellIds == null) return new string[0];
            var codes = new string[spellIds.Length];
            for (int i = 0; i < spellIds.Length; i++) codes[i] = ToCode(spellIds[i]);
            return codes;
        }
    }

    /// <summary>19종 스킬 목록 (서버 skills 테이블 — 이름 · 등급 · 조준 · 쿨타임)</summary>
    public static class SkillService
    {
        public static void GetAll(Action<SkillDto[]> onSuccess, Action<string> onError = null)
            => BackendClient.Instance.GetArray("/api/skills", onSuccess, onError);
    }
}
