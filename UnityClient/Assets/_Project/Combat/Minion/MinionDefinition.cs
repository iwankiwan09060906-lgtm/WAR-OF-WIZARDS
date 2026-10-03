// §15 미니언 웨이브 — 모든 수치는 MinionDefinition SO, 추후 설정

using UnityEngine;

namespace SpellboundVR.Combat
{
    public enum MinionKind : byte
    {
        Melee = 0,
        Ranged = 1,
        Brute = 2,
    }

    [CreateAssetMenu(menuName = "Spellbound/Minion Definition", fileName = "Minion_Melee")]
    public sealed class MinionDefinition : ScriptableObject
    {
        public MinionKind Kind = MinionKind.Melee;
        public float MaxHp = 120f;
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

        public static MinionDefinition CreateDefault(MinionKind kind)
        {
            var d = CreateInstance<MinionDefinition>();
            d.Kind = kind;
            d.name = "Minion_" + kind + " (Runtime Default)";
            switch (kind)
            {
                case MinionKind.Melee:
                    d.MaxHp = 120f; d.Damage = 14f; d.AttackInterval = 1f; d.AttackRange = 0.9f;
                    d.MoveSpeed = 1.6f; d.DetectRange = 6f; d.BodyRadius = 0.35f;
                    d.StructureDamageMultiplier = 1f; d.PrefersStructures = false; d.TowerTargetPriority = 1;
                    break;
                case MinionKind.Ranged:
                    d.MaxHp = 80f; d.Damage = 11f; d.AttackInterval = 1.2f; d.AttackRange = 4f;
                    d.MoveSpeed = 1.5f; d.DetectRange = 7f; d.BodyRadius = 0.3f;
                    d.StructureDamageMultiplier = 1f; d.PrefersStructures = false; d.TowerTargetPriority = 2;
                    break;
                default:
                    d.MaxHp = 420f; d.Damage = 30f; d.AttackInterval = 1.4f; d.AttackRange = 1.1f;
                    d.MoveSpeed = 1.2f; d.DetectRange = 6f; d.BodyRadius = 0.55f;
                    d.StructureDamageMultiplier = 2f; d.PrefersStructures = true; d.TowerTargetPriority = 0;
                    break;
            }
            return d;
        }
    }
}
