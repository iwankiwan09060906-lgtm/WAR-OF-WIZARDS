// §25.3 VFX 프리팹 규격 (ㅇㅎㅅ → ㅊㄱㅇ)
//   이름: VFX_{SkillId}_{Name}_{Part}  예) VFX_S03_Fireball_Impact
// 조회 순서: ① 이 카탈로그의 프리팹 목록 → ② Resources/VFX/{이름} → ③ 없음(대체 연출 사용)
// 모델도 같은 방식으로 찾는다: MDL_{Category}_{Name} (§25.4)

using System.Collections.Generic;
using UnityEngine;

namespace SpellboundVR.Presentation
{
    [CreateAssetMenu(menuName = "Spellbound/VFX Catalog", fileName = "VFXCatalog")]
    public sealed class VFXCatalog : ScriptableObject
    {
        [Tooltip("VFX_* / MDL_* 프리팹. 이름으로 조회한다.")]
        public List<GameObject> Prefabs = new List<GameObject>();

        private Dictionary<string, GameObject> _byName;
        private readonly HashSet<string> _missing = new HashSet<string>();

        public bool TryGet(string prefabName, out GameObject prefab)
        {
            if (_byName == null) Rebuild();
            if (_byName.TryGetValue(prefabName, out prefab) && prefab != null) return true;
            if (_missing.Contains(prefabName))
            {
                prefab = null;
                return false;
            }

            prefab = Resources.Load<GameObject>("VFX/" + prefabName);
            if (prefab == null) prefab = Resources.Load<GameObject>("Models/" + prefabName);
            if (prefab != null)
            {
                _byName[prefabName] = prefab;
                return true;
            }
            _missing.Add(prefabName);
            return false;
        }

        private void Rebuild()
        {
            _byName = new Dictionary<string, GameObject>();
            for (int i = 0; i < Prefabs.Count; i++)
            {
                var p = Prefabs[i];
                if (p != null && !_byName.ContainsKey(p.name)) _byName.Add(p.name, p);
            }
        }

        private void OnEnable()
        {
            _byName = null;
            _missing.Clear();
        }

        public static VFXCatalog CreateDefault()
        {
            var c = CreateInstance<VFXCatalog>();
            c.name = "VFXCatalog (Runtime Default)";
            return c;
        }
    }
}
