// SpellDefinition → Executor 인스턴스 매핑 (Rule 11: 데이터 + Behavior 조합)
// Executor 7종 전부 구현 (Projectile / Area / Beam / Summon / Structure / Buff / Global).
// 매핑이 없는 정의는 등록되지 않으며, 서버가 NotImplemented로 거절한다 (쿨타임 미소모).

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Spells.Executor
{
    public sealed class SpellExecutorRegistry : ISpellExecutorLookup
    {
        private readonly Dictionary<int, ISpellExecutor> _executors = new Dictionary<int, ISpellExecutor>();

        public SpellExecutorRegistry(SpellCatalog catalog, ISpellWorld world)
        {
            var all = catalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null) continue;
                var exec = Create(def, world);
                if (exec == null)
                {
                    Debug.LogWarning($"[SpellExecutorRegistry] {def.Code} ({def.Executor}) Executor 미구현 — 시전 시 거절됨");
                    continue;
                }
                _executors[def.SpellId] = exec;
            }
        }

        public bool Has(int spellId) => _executors.ContainsKey(spellId);

        public bool TryGet(int spellId, out ISpellExecutor executor) => _executors.TryGetValue(spellId, out executor);

        private static ISpellExecutor Create(SpellDefinition def, ISpellWorld world)
        {
            switch (def.Executor)
            {
                case SpellExecutorType.Projectile: return new ProjectileExecutor(def, world);
                case SpellExecutorType.Area: return new AreaExecutor(def, world);
                case SpellExecutorType.Beam: return new BeamExecutor(def, world);
                case SpellExecutorType.Summon: return new SummonExecutor(def, world);
                case SpellExecutorType.Structure: return new StructureExecutor(def, world);
                case SpellExecutorType.Buff: return new BuffExecutor(def, world);
                case SpellExecutorType.Global: return new GlobalExecutor(def, world);
                default: return null;
            }
        }
    }
}
