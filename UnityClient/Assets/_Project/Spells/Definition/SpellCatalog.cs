// 사용 가능한 SpellDefinition 목록. 서버(검증 · 실행)와 클라이언트(룬 후보 · HUD)가 같은 에셋을 쓴다.

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Spells
{
    [CreateAssetMenu(menuName = "Spellbound/Spell Catalog", fileName = "SpellCatalog")]
    public sealed class SpellCatalog : ScriptableObject
    {
        public List<SpellDefinition> Spells = new List<SpellDefinition>();

        private Dictionary<int, SpellDefinition> _byId;

        public IReadOnlyList<SpellDefinition> All => Spells;

        public bool TryGet(int spellId, out SpellDefinition definition)
        {
            if (_byId == null || _byId.Count != CountValid()) Rebuild();
            return _byId.TryGetValue(spellId, out definition);
        }

        public SpellDefinition Get(int spellId)
        {
            return TryGet(spellId, out var d) ? d : null;
        }

        public bool Contains(int spellId) => TryGet(spellId, out _);

        private int CountValid()
        {
            int n = 0;
            for (int i = 0; i < Spells.Count; i++) if (Spells[i] != null) n++;
            return n;
        }

        private void Rebuild()
        {
            _byId = new Dictionary<int, SpellDefinition>();
            for (int i = 0; i < Spells.Count; i++)
            {
                var s = Spells[i];
                if (s == null) continue;
                if (_byId.ContainsKey(s.SpellId))
                {
                    Debug.LogWarning($"[SpellCatalog] 중복 SpellId {s.SpellId} ({s.name}) 무시");
                    continue;
                }
                _byId.Add(s.SpellId, s);
            }
        }

        private void OnEnable() => _byId = null;

        private void OnValidate() => _byId = null;

        /// <summary>에셋이 없을 때 쓰는 런타임 기본 카탈로그 (S03 / S07 / S08)</summary>
        public static SpellCatalog CreateDefault()
        {
            var cat = CreateInstance<SpellCatalog>();
            cat.name = "SpellCatalog (Runtime Default)";
            cat.Spells.Add(SpellDefinition.CreateFireballDefault());
            cat.Spells.Add(SpellDefinition.CreateGatlingDefault());
            cat.Spells.Add(SpellDefinition.CreateShieldDefault());
            return cat;
        }
    }
}
