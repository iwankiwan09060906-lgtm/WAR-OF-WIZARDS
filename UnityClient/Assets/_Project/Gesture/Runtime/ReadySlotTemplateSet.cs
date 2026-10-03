// §9 / §13.3 — $P+ 후보는 "쿨타임이 끝났고 소진 · 비활성이 아닌 슬롯"의 룬만 (최대 8개)

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Deck;

namespace SpellboundVR.Gesture
{
    public sealed class ReadySlotTemplateSet
    {
        private readonly List<PointCloud> _candidates = new List<PointCloud>(64);
        private readonly PDollarPlusRecognizer _recognizer = new PDollarPlusRecognizer();

        public IReadOnlyList<PointCloud> Candidates => _candidates;

        /// <summary>현재 Ready 슬롯의 템플릿으로 후보 목록을 다시 만든다.</summary>
        public void Rebuild(DeckData deck, System.Func<int, SlotState> slotState, RuneTemplateLibrary library)
        {
            _candidates.Clear();
            if (deck == null || library == null) return;
            for (int slot = 0; slot < DeckData.SlotCount; slot++)
            {
                int spellId = deck[slot];
                if (spellId <= 0 || slotState(slot) != SlotState.Ready) continue;
                var template = library.Find(spellId);
                if (template == null) continue;
                var clouds = template.GetClouds();
                for (int i = 0; i < clouds.Count; i++) _candidates.Add(clouds[i]);
            }
        }

        public GestureMatch Recognize(UnityEngine.Vector2[] raw)
        {
            if (_candidates.Count == 0 || raw == null || raw.Length < 2)
                return new GestureMatch { HasMatch = false, Distance = float.MaxValue, SecondDistance = float.MaxValue };
            var cloud = PointCloud.FromStroke(0, "candidate", raw);
            return _recognizer.Recognize(cloud, _candidates);
        }
    }
}
