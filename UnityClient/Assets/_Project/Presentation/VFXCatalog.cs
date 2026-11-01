// §25.3 VFX 프리팹 규격 (ㅇㅎㅅ → ㅊㄱㅇ)
//   이름: VFX_{SkillId}_{Name}_{Part}  예) VFX_S03_Fireball_Impact
// 조회 순서: ① 이펙트 팩 대응표 → ② 이 카탈로그의 프리팹 목록 → ③ Resources/VFX/{이름} → ④ 없음(대체 연출 사용)
// 이펙트 팩(vfx.unitypackage, Assets/VFX)은 자기 번호 체계(그림 순서 S01~S20)를 쓴다. 파일 이름을 바꾸면 다시 임포트할 때
// 어긋나므로 그대로 두고 PackAliases 로 게임 이름에 잇는다. 대응표에 없는 부분은 지금까지의 프리팹을 쓴다.
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

        /// <summary>게임 프리팹 이름 → 이펙트 팩 프리팹 이름</summary>
        private static readonly Dictionary<string, string> PackAliases = new Dictionary<string, string>
        {
            { "VFX_S01_Smoke_Cast", "VFX_S01_LOS Blocker_Cast" },
            { "VFX_S02_PoisonField_Cast", "VFX_S02_poison_Cast" },
            { "VFX_S02_PoisonField_Projectile", "VFX_S02_poison_Projectile" },
            { "VFX_S03_Fireball_Cast", "VFX_S04_fireball_Cast" },
            { "VFX_S03_Fireball_Projectile", "VFX_S04_fireball_Projectile" },
            { "VFX_S03_Fireball_Impact", "VFX_S04_fireball_Impact" },
            { "VFX_S04_ArrowRain_Cast", "VFX_S05_arrow_Cast" },
            { "VFX_S04_ArrowRain_Projectile", "VFX_S05_arrow_Projectile" },
            { "VFX_S05_Tornado_Cast", "VFX_S06_tornado_Cast" },
            { "VFX_S05_Tornado_Projectile", "VFX_S06_tornado_Projectile" },
            { "VFX_S06_Lightning_Cast", "VFX_S07_thunde_Cast" },
            { "VFX_S06_Lightning_Impact", "VFX_S07_thunde_Impact" },
            { "VFX_S07_Gatling_Cast", "VFX_S09_shootingr_Cast" },
            { "VFX_S08_Shield_Cast", "VFX_S11_Shield_Cast" },
            { "VFX_S09_TowerBuff_Cast", "VFX_S15_tower Buff_Cast" },
            { "VFX_S10_Focus_Cast", "VFX_S16_player Buff_Cast" },
            { "VFX_S11_AxeWarrior_Cast", "VFX_S17_Melee Minion_Cast" },
            { "VFX_S12_Mage_Cast", "VFX_S18_Caster Minion_Cast" },
            { "VFX_S13_Turret_Cast", "VFX_S19_turret tower_Cast" },
            { "VFX_H01_Freeze_Cast", "VFX_S03_timestop_Cast" },
            { "VFX_H02_Laser_Cast", "VFX_S10_laser_Cast" },
            { "VFX_H03_Drain_Telegraph", "VFX_S08_Lifesteal_Cast" },
            { "VFX_H04_ReflectBarrier_Cast", "VFX_S11_Shield_Cast" },
            { "VFX_H05_Rage_Cast", "VFX_S14_Minon Buff_Cast" },
            { "VFX_H06_SummonTower_Cast", "VFX_S20_turret Minion_Cast" },
        };

        private Dictionary<string, GameObject> _byName;
        private readonly HashSet<string> _missing = new HashSet<string>();

        public bool TryGet(string prefabName, out GameObject prefab)
        {
            if (PackAliases.TryGetValue(prefabName, out string packName) && TryGetDirect(packName, out prefab)) return true;
            return TryGetDirect(prefabName, out prefab);
        }

        private bool TryGetDirect(string prefabName, out GameObject prefab)
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
