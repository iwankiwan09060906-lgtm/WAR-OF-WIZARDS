// §9 / §13.3 — 룬 인식 후보는 "쿨타임이 끝났고 소진 · 비활성이 아닌 슬롯"의 룬만 (최대 8개)
// 인식기는 RuneRecognizer (v2: $P+ + 획 방향 · 획 수 + 증강 템플릿).

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;

namespace SpellboundVR.Gesture
{
    public sealed class ReadySlotTemplateSet
    {
        private readonly RuneRecognizer _recognizer = new RuneRecognizer();
        private readonly HashSet<int> _spellIds = new HashSet<int>();

        /// <summary>지금 후보인 스킬 수</summary>
        public int CandidateCount => _spellIds.Count;

        public IReadOnlyCollection<int> CandidateSpellIds => _spellIds;

        /// <summary>현재 Ready 슬롯의 템플릿으로 후보 목록을 다시 만든다.</summary>
        public void Rebuild(DeckData deck, System.Func<int, SlotState> slotState, RuneTemplateLibrary library)
        {
            Clear();
            if (deck == null || library == null) return;
            for (int slot = 0; slot < DeckData.SlotCount; slot++)
            {
                int spellId = deck[slot];
                if (spellId <= 0 || slotState(slot) != SlotState.Ready) continue;
                Add(library.Find(spellId));
            }
        }

        /// <summary>룬 인식 테스트용: 덱 · 쿨타임과 상관없이 라이브러리의 모든 룬을 후보로</summary>
        public void RebuildAll(RuneTemplateLibrary library)
        {
            Clear();
            if (library == null) return;
            foreach (var template in library.Templates) Add(template);
        }

        private void Clear()
        {
            _recognizer.Clear();
            _spellIds.Clear();
        }

        private void Add(RuneTemplate template)
        {
            if (template == null || !_spellIds.Add(template.SpellId)) return;
            _recognizer.AddTemplates(template.GetFeatures());
        }

        /// <summary>룬 하나 (한 획 이상, 같은 그리기 평면 좌표)를 인식한다.</summary>
        public GestureMatch Recognize(IReadOnlyList<UnityEngine.Vector2[]> strokes)
        {
            int total = 0;
            for (int i = 0; strokes != null && i < strokes.Count; i++)
                if (strokes[i] != null) total += strokes[i].Length;
            if (_spellIds.Count == 0 || total < 2)
                return new GestureMatch { HasMatch = false, Distance = float.MaxValue, SecondDistance = float.MaxValue };
            return _recognizer.Recognize(strokes);
        }
    }
}
