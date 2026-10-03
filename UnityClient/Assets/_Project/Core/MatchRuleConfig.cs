// §3.3 수치 원칙 — 확정 수치는 기본값으로 고정, 그 외는 "추후 설정" 값을 SO로 노출한다.
// 확정 수치: 쿨타임 5/20, 고위력 3회·덱 2장, 구조물 보너스 +20%/+40%, 타워 생존 시 피해 20%,
//            넥서스 회복 10초, 강화 5분, 기권 30초, 조준 1초, 웨이브 10초(근접2+원거리1, 3웨이브마다 Brute)

using UnityEngine;

namespace SpellboundVR.Core
{
    [CreateAssetMenu(menuName = "Spellbound/Match Rule Config", fileName = "MatchRuleConfig")]
    public sealed class MatchRuleConfig : ScriptableObject
    {
        [Header("경기 흐름")]
        [Tooltip("두 플레이어가 준비된 뒤 3-2-1 카운트다운 (초)")]
        public float CountdownSeconds = 3f;
        [Tooltip("경기 종료 후 다음 경기 자동 시작까지 (초). 0 이하면 자동 재시작 안 함")]
        public float AutoRestartDelay = 10f;
        [Tooltip("서버 모드: 첫 사람 입장 후 이 시간 안에 두 번째 사람이 없으면 봇 매칭 (§22.1)")]
        public float BotFillWaitSeconds = 10f;
        [Tooltip("서버 시뮬레이션 고정 틱 간격 (로컬 모드)")]
        public float LocalTickInterval = 1f / 30f;

        [Header("플레이어")]
        public float PlayerMaxHp = 1000f;

        [Header("쿨타임 · 고위력 (§13) — 확정")]
        public float NormalCooldown = 5f;
        public float HighPowerCooldown = 20f;
        public int HighPowerUsesPerGame = 3;
        public int MaxHighPowerInDeck = 2;
        [Tooltip("임시: 19종 미완성 동안 8장 미만 덱(빈 슬롯) 허용")]
        public bool AllowPartialDeck = true;

        [Header("방어 계층 · 배율 (§12) — 확정")]
        public float TowerAliveSkillDamageRatio = 0.2f;
        public float TowerDestroyedAttackBonus = 0.2f;
        public float NexusDestroyedAttackBonus = 0.4f;

        [Header("넥서스 회복 (§19)")]
        public float NexusRegenInterval = 10f;
        public float NexusRegenAmount = 25f;

        [Header("5분 강화 (§12.3)")]
        public float ScalingInterval = 300f;
        public float ScalingAttackPerStage = 0.15f;
        [Tooltip("강화 단계당 쿨타임 감소 비율")]
        public float ScalingCooldownReductionPerStage = 0.1f;
        [Tooltip("쿨타임 감소 하한 (기본 쿨타임 대비 비율)")]
        public float MinCooldownRatio = 0.5f;

        [Header("기권 (§10)")]
        public float AfkForfeitSeconds = 30f;
        public float AfkWarningSeconds = 20f;

        [Header("입력 (§6) — 조준 1초 확정")]
        public float AimDurationSeconds = 1f;
        public float RuneReadyTimeoutSeconds = 5f;
        [Tooltip("이동 재입력 제한 (초)")]
        public float MoveCooldownSeconds = 0.35f;

        [Header("명중률 (§12.4)")]
        public float BaseAccuracy = 1f;

        [Header("웨이브 (§15) — 확정")]
        public float WaveInterval = 10f;
        public float FirstWaveDelay = 5f;
        public int MeleePerWave = 2;
        public int RangedPerWave = 1;
        public int BruteEveryNWaves = 3;
        [Tooltip("진영당 동시 생존 상한 (§15.1)")]
        public int MaxAliveUnitsPerTeam = 20;
        public float MinionMaxLifetime = 90f;

        [Header("타워 (§18)")]
        public float TowerMaxHp = 1500f;
        public float TowerDamage = 35f;
        [Tooltip("초당 약 1.2회")]
        public float TowerAttacksPerSecond = 1.2f;
        public float TowerRange = 7f;
        [Tooltip("같은 대상 연속 타격 1회당 피해 증가 비율")]
        public float TowerComboBonusPerHit = 0.1f;
        public int TowerComboMaxStacks = 5;
        public float StructureRadius = 1.0f;

        [Header("넥서스 (§19)")]
        public float NexusMaxHp = 2000f;

        [Header("돌파 (§11.1)")]
        [Tooltip("돌파 지점: 상대 Commander 발판 앞 거리 (m)")]
        public float BreachPointOffset = 1.5f;

        public float GetBaseCooldown(Contracts.SpellTier tier)
        {
            return tier == Contracts.SpellTier.HighPower ? HighPowerCooldown : NormalCooldown;
        }

        public float GetCooldown(Contracts.SpellTier tier, int scalingStage)
        {
            float baseCd = GetBaseCooldown(tier);
            float ratio = 1f - ScalingCooldownReductionPerStage * Mathf.Max(0, scalingStage);
            return baseCd * Mathf.Max(MinCooldownRatio, ratio);
        }

        public static MatchRuleConfig CreateDefault()
        {
            var cfg = CreateInstance<MatchRuleConfig>();
            cfg.name = "MatchRuleConfig (Runtime Default)";
            return cfg;
        }
    }
}
