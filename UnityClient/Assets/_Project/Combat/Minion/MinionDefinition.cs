// §15 미니언 웨이브 · §14.5 소환수 · 설치물 — 모든 수치는 MinionDefinition SO, 추후 설정
// 소환수(S11 · S12 · H06 해골)와 설치물(S13 터렛 · H06 소환탑)도 같은 유닛 풀 · AI를 쓴다.
//   · 설치물(IsStructure): 이동하지 않음, 타겟 종류 = Structure (타워의 대상은 아니지만 플레이어 스킬에는 맞는다, §11)
//   · SpawnInterval > 0 이면 그 간격으로 SpawnKind 유닛을 앞쪽에 소환한다 (H06 소환탑)

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Combat
{
    // 스냅샷 · 에셋에 정수로 저장되므로 기존 값(0~2)의 순서는 바꾸지 않는다
    public enum MinionKind : byte
    {
        Melee = 0,
        Ranged = 1,
        Brute = 2,
        /// <summary>S11 도끼 전사 (오크) — 넓은 근접 범위 공격, 탱커</summary>
        AxeOrc = 3,
        /// <summary>S12 드래곤 — 비행, 원거리 화염</summary>
        Dragon = 4,
        /// <summary>H06 소환탑이 3초마다 내보내는 해골</summary>
        Skeleton = 5,
        /// <summary>S13 터렛 (돌탑 + 궁수)</summary>
        Turret = 6,
        /// <summary>H06 소환탑 (오두막)</summary>
        SummonHut = 7,
    }

    [CreateAssetMenu(menuName = "Spellbound/Minion Definition", fileName = "Minion_Melee")]
    public sealed class MinionDefinition : ScriptableObject
    {
        public const int KindCount = 8;

        public MinionKind Kind = MinionKind.Melee;
        public float MaxHp = 180f;
        public float Damage = 15f;
        [Tooltip("공격 간격 (초)")]
        public float AttackInterval = 1f;
        [Tooltip("공격 사거리 (대상 반경 제외, m)")]
        public float AttackRange = 1.0f;
        public float MoveSpeed = 1.6f;
        [Tooltip("탐지 반경 (m)")]
        public float DetectRange = 6f;
        public float BodyRadius = 0.35f;
        [Tooltip("구조물 대상 피해 배율 (Brute 구조물 추가 피해)")]
        public float StructureDamageMultiplier = 1f;
        [Tooltip("Brute: 구조물 우선 타격")]
        public bool PrefersStructures;
        [Tooltip("타워 타겟 우선순위 (낮을수록 먼저 노림, §18: Brute → 근접 → 원거리)")]
        public int TowerTargetPriority = 1;

        [Header("소환수 · 설치물")]
        [Tooltip("설치물: 이동하지 않고 타워의 대상이 아니다 (플레이어 스킬에는 맞는다, §11)")]
        public bool IsStructure;
        [Tooltip("소환수 (미니언과 같은 규칙, 타겟 종류만 Summon)")]
        public bool IsSummon;
        [Tooltip("공격 시 대상 주변 이 반경의 적 유닛도 함께 피해 (0 = 단일)")]
        public float SplashRadius;
        [Tooltip("표시용 비행 높이 (m)")]
        public float FlightHeight;
        [Tooltip("최대 생존 시간 (초, 0 = 규칙의 미니언 최대 생존)")]
        public float Lifetime;
        [Tooltip("이 간격(초)마다 SpawnKind 유닛을 소환 (0 = 소환 안 함)")]
        public float SpawnInterval;
        public MinionKind SpawnKind = MinionKind.Skeleton;

        public TargetKind TargetKindSelf => IsStructure ? TargetKind.Structure : (IsSummon ? TargetKind.Summon : TargetKind.Minion);

        public bool CanAttack => Damage > 0f && AttackInterval > 0f;

        public static MinionDefinition CreateDefault(MinionKind kind)
        {
            var d = CreateInstance<MinionDefinition>();
            d.Kind = kind;
            d.name = "Minion_" + kind + " (Runtime Default)";
            switch (kind)
            {
                case MinionKind.Melee:
                    d.MaxHp = 180f; d.Damage = 14f; d.AttackInterval = 1f; d.AttackRange = 0.9f;
                    d.MoveSpeed = 1.6f; d.DetectRange = 6f; d.BodyRadius = 0.35f;
                    d.StructureDamageMultiplier = 1f; d.PrefersStructures = false; d.TowerTargetPriority = 1;
                    break;
                case MinionKind.Ranged:
                    d.MaxHp = 120f; d.Damage = 11f; d.AttackInterval = 1.2f; d.AttackRange = 4f;
                    d.MoveSpeed = 1.5f; d.DetectRange = 7f; d.BodyRadius = 0.3f;
                    d.StructureDamageMultiplier = 1f; d.PrefersStructures = false; d.TowerTargetPriority = 2;
                    break;
                case MinionKind.Brute:
                    d.MaxHp = 630f; d.Damage = 30f; d.AttackInterval = 1.4f; d.AttackRange = 1.1f;
                    d.MoveSpeed = 1.2f; d.DetectRange = 6f; d.BodyRadius = 0.55f;
                    d.StructureDamageMultiplier = 2f; d.PrefersStructures = true; d.TowerTargetPriority = 0;
                    break;
                case MinionKind.AxeOrc:
                    // 미니언 3~4기를 한 번에 베는 탱커형 근접 (도끼 휘두르기 반경 2.4m)
                    d.IsSummon = true;
                    d.MaxHp = 1100f; d.Damage = 65f; d.AttackInterval = 1.6f; d.AttackRange = 1.5f;
                    d.MoveSpeed = 1.5f; d.DetectRange = 7f; d.BodyRadius = 0.8f;
                    d.SplashRadius = 2.4f; d.StructureDamageMultiplier = 1.5f; d.TowerTargetPriority = 0;
                    d.Lifetime = 40f;
                    break;
                case MinionKind.Dragon:
                    // 공중 원거리 화염 (작은 범위)
                    d.IsSummon = true;
                    d.MaxHp = 450f; d.Damage = 45f; d.AttackInterval = 2.0f; d.AttackRange = 6f;
                    d.MoveSpeed = 1.8f; d.DetectRange = 9f; d.BodyRadius = 0.8f;
                    d.SplashRadius = 1.5f; d.FlightHeight = 3.5f; d.TowerTargetPriority = 1;
                    d.Lifetime = 40f;
                    break;
                case MinionKind.Skeleton:
                    // §14.3 근접 미니언 체력의 절반
                    d.IsSummon = true;
                    d.MaxHp = 90f; d.Damage = 10f; d.AttackInterval = 1f; d.AttackRange = 0.8f;
                    d.MoveSpeed = 1.8f; d.DetectRange = 6f; d.BodyRadius = 0.3f; d.TowerTargetPriority = 1;
                    d.Lifetime = 30f;
                    break;
                case MinionKind.Turret:
                    // 타워(초당 88, 사거리 7)보다 약간 약한 고정 포탑
                    d.IsStructure = true;
                    d.MaxHp = 900f; d.Damage = 55f; d.AttackInterval = 1f; d.AttackRange = 7f;
                    d.MoveSpeed = 0f; d.DetectRange = 7f; d.BodyRadius = 0.7f;
                    d.Lifetime = 45f;
                    break;
                case MinionKind.SummonHut:
                    d.IsStructure = true;
                    // 고위력(게임당 3회) — 단단하고 오래 버티며 해골을 빠르게 내보낸다
                    d.MaxHp = 1400f; d.Damage = 0f; d.AttackInterval = 0f; d.AttackRange = 0f;
                    d.MoveSpeed = 0f; d.DetectRange = 0f; d.BodyRadius = 0.8f;
                    d.SpawnInterval = 2.5f; d.SpawnKind = MinionKind.Skeleton;
                    d.Lifetime = 40f;
                    break;
            }
            return d;
        }
    }
}
