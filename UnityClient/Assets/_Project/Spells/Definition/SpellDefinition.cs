// §14.1 모든 스킬은 SpellDefinition SO로 데이터화한다.
// §14.2 실행 코드는 Executor 7종 + Behavior 조합으로 재사용한다 (Rule 11).
// 수치 기본값은 "추후 설정"(§3.3)을 위한 임시값이며 에셋에서 조정한다.
// 19종 기본값(Create*Default)은 Spellbound > Skills > Rebuild Spell Definitions 가 에셋으로 덮어쓴다.

using SpellboundVR.Combat;
using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Spells
{
    public enum SpellExecutorType : byte
    {
        /// <summary>열 지정 투사체 — S01, S07</summary>
        Projectile,
        /// <summary>열 + 깊이 지점 — S02, S03, S04</summary>
        Area,
        /// <summary>열(레인) 전체 — S05, S06, H02</summary>
        Beam,
        /// <summary>소환 라인 — S11, S12</summary>
        Summon,
        /// <summary>설치 구역(타워 앞 → 넥서스 앞) — S13, H06</summary>
        Structure,
        /// <summary>자기 · 타워 · 다음 스킬 — S08, S09, S10, H04</summary>
        Buff,
        /// <summary>조준 없음, 전장 · 상대 대상 — H01, H03, H05</summary>
        Global,
    }

    // 에셋에 정수로 저장되므로 기존 값(0~3)의 순서는 바꾸지 않는다
    public enum SpellBehaviorType : byte
    {
        None,
        /// <summary>S07 — 열 안 적을 거리순 정렬 후 한 발씩, 앞에 유닛이 없으면 플레이어</summary>
        MultiShotFrontFirst,
        /// <summary>지점 주변 범위 피해 (즉발)</summary>
        Splash,
        /// <summary>S08 — 피해 흡수 + 무피격 시 HP 회복</summary>
        ShieldRegen,
        /// <summary>S01 — 상대 플레이어에게 연막탄, 명중 시 시야 암전</summary>
        BlindShot,
        /// <summary>S02 — 3열을 가로지르는 넓은 띠 장판, 틱마다 피해 누적 증가</summary>
        PoisonField,
        /// <summary>S03 — 지점으로 포물선 비행 후 폭발</summary>
        FireballLob,
        /// <summary>S04 — 한 열의 지점 주변에 화살 여러 발이 포물선으로 낙하</summary>
        ArrowVolley,
        /// <summary>S05 — 레인 위 회전 장판, 안의 적 유닛 저지 + 틱 피해</summary>
        TornadoLane,
        /// <summary>S06 — 하늘에서 레인 앞쪽 적에게 낙뢰 여러 번</summary>
        LightningStrikes,
        /// <summary>S09 — 본인 타워 방어력 · 공격력 증가</summary>
        TowerBuff,
        /// <summary>S10 — 다음 스킬 1회 강화</summary>
        Focus,
        /// <summary>S11 · S12 — 소환수 생성</summary>
        SummonUnit,
        /// <summary>S13 · H06 — 설치물(터렛 · 소환탑) 생성</summary>
        PlaceStructure,
        /// <summary>H01 — 상대 진영 유닛 · 구조물 정지</summary>
        FreezeAll,
        /// <summary>H02 — 레인 전체를 지속 빔으로 태움</summary>
        LaserBeam,
        /// <summary>H03 — 상대 플레이어 피해 → 생명력이 시전자에게 날아와 회복</summary>
        DrainLife,
        /// <summary>H04 — 스킬 피해 무효 + 일부 반사</summary>
        ReflectBarrier,
        /// <summary>H05 — 전장 장판, 아군 유닛 강화</summary>
        RageField,
    }

    /// <summary>S10 집중이 다음 스킬에 주는 보너스 종류</summary>
    public enum SpellFocusCategory : byte
    {
        /// <summary>피해 × 집중 배율</summary>
        Attack,
        /// <summary>지속 시간 × 집중 배율</summary>
        Defense,
        /// <summary>소환수 · 설치물 체력 × 집중 배율</summary>
        Summon,
    }

    [CreateAssetMenu(menuName = "Spellbound/Spell Definition", fileName = "Spell_Sxx_Name")]
    public sealed class SpellDefinition : ScriptableObject
    {
        [Header("식별")]
        [Tooltip("S01~S13 → 1~13, H01~H06 → 101~106")]
        public int SpellId;
        public string DisplayName = "Spell";
        [Tooltip("VFX 프리팹 이름 규칙의 {Name} 부분 (§25.3). 예) Fireball → VFX_S03_Fireball_Impact")]
        public string VfxName = "Spell";
        public Color ThemeColor = Color.white;

        [Header("분류 (§14.3)")]
        public SpellTier Tier = SpellTier.Normal;
        public SpellTargeting Targeting = SpellTargeting.Column;
        public SpellExecutorType Executor = SpellExecutorType.Projectile;
        public SpellBehaviorType Behavior = SpellBehaviorType.None;
        [Tooltip("S10 집중을 받았을 때 강화되는 항목")]
        public SpellFocusCategory FocusCategory = SpellFocusCategory.Attack;
        [Tooltip("§11.2 회피 가능 여부 (열 지정 투사체 · 레이저 · 장판)")]
        public bool Dodgeable;

        [Header("피해")]
        public float BaseDamage = 50f;
        [Tooltip("범위 반경 (m)")]
        public float Radius = 2f;
        [Tooltip("Area 조준 최대 사거리 (깊이 0~1)")]
        [Range(0.1f, 1f)] public float MaxDepth = 1f;

        [Header("투사체")]
        public float ProjectileSpeed = 20f;
        public int ShotCount = 1;
        public float ShotInterval = 0.1f;
        [Tooltip("포물선 꼭대기 높이 (m, 0 = 직선)")]
        public float ArcHeight = 0f;
        [Tooltip("예고 표시 후 실제 타격까지 (초)")]
        public float TelegraphDelay = 0f;

        [Header("지속 피해 (장판 · 빔)")]
        [Tooltip("틱 간격 (초)")]
        public float TickInterval = 0.5f;
        [Tooltip("틱마다 피해 증가량 (S02 누적 증가)")]
        public float TickDamageRamp = 0f;
        [Tooltip("틱 피해 상한 (0 = 없음)")]
        public float MaxTickDamage = 0f;
        [Tooltip("장판 · 띠 깊이 방향 반폭 (m)")]
        public float FieldHalfDepth = 2.5f;
        [Tooltip("레인 장판 시작 · 끝 깊이 (시전자 기준 0~1)")]
        [Range(0f, 1f)] public float LaneStartDepth = 0f;
        [Range(0f, 1f)] public float LaneEndDepth = 1f;

        [Header("상태 효과")]
        public string StatusEffectId = "";
        public float EffectDuration = 0f;
        public float ShieldAmount = 0f;
        [Tooltip("이 시간 동안 피격이 없으면 HP 회복 시작 (초)")]
        public float RegenDelay = 3f;
        public float RegenPerSecond = 0f;

        [Header("강화 · 약화 배율")]
        [Tooltip("공격력 배율 (S09 타워 · H05 아군 유닛)")]
        public float AttackMultiplier = 1f;
        [Tooltip("공격 속도 배율 (H05)")]
        public float AttackSpeedMultiplier = 1f;
        [Tooltip("이동 속도 배율 (H05)")]
        public float MoveSpeedMultiplier = 1f;
        [Tooltip("받는 피해 배율 (S09 타워 · H05 아군 유닛, 낮을수록 단단함)")]
        public float IncomingDamageMultiplier = 1f;
        [Tooltip("H04 반사 비율 · H03 회복 비율 · S10 집중 배율")]
        public float EffectRatio = 1f;

        [Header("소환 · 설치")]
        public MinionKind SummonKind = MinionKind.Melee;

        public string Code => SpellIds.Code(SpellId);

        /// <summary>§6.3 조준 생략 — 주먹을 쥐는 순간 즉시 시전</summary>
        public bool RequiresAim =>
            Targeting != SpellTargeting.Self &&
            Targeting != SpellTargeting.Tower &&
            Targeting != SpellTargeting.Global;

        /// <summary>§25.3 VFX_{SkillId}_{Name}_{Part}</summary>
        public string GetVfxPrefabName(VfxPart part)
        {
            return "VFX_" + Code + "_" + VfxName + "_" + part;
        }

        public static SpellDefinition Create(int spellId, string displayName, string vfxName, Color color)
        {
            var def = CreateInstance<SpellDefinition>();
            def.SpellId = spellId;
            def.DisplayName = displayName;
            def.VfxName = vfxName;
            def.ThemeColor = color;
            def.Tier = SpellIds.IsHighPowerId(spellId) ? SpellTier.HighPower : SpellTier.Normal;
            def.name = "Spell_" + SpellIds.Code(spellId) + "_" + vfxName;
            return def;
        }

        /// <summary>19종 기본값 (밸런스 임시값 — Docs 마스터 프롬프트 §14.3 수치표와 같다)</summary>
        public static SpellDefinition CreateDefault(int spellId)
        {
            switch (spellId)
            {
                case SpellIds.S01_Smoke: return CreateSmokeDefault();
                case SpellIds.S02_PoisonField: return CreatePoisonFieldDefault();
                case SpellIds.S03_Fireball: return CreateFireballDefault();
                case SpellIds.S04_ArrowRain: return CreateArrowRainDefault();
                case SpellIds.S05_Tornado: return CreateTornadoDefault();
                case SpellIds.S06_Lightning: return CreateLightningDefault();
                case SpellIds.S07_Gatling: return CreateGatlingDefault();
                case SpellIds.S08_Shield: return CreateShieldDefault();
                case SpellIds.S09_TowerBuff: return CreateTowerBuffDefault();
                case SpellIds.S10_Focus: return CreateFocusDefault();
                case SpellIds.S11_AxeWarrior: return CreateAxeWarriorDefault();
                case SpellIds.S12_Mage: return CreateDragonDefault();
                case SpellIds.S13_Turret: return CreateTurretDefault();
                case SpellIds.H01_Freeze: return CreateFreezeDefault();
                case SpellIds.H02_Laser: return CreateLaserDefault();
                case SpellIds.H03_Drain: return CreateDrainDefault();
                case SpellIds.H04_ReflectBarrier: return CreateReflectDefault();
                case SpellIds.H05_Rage: return CreateRageDefault();
                case SpellIds.H06_SummonTower: return CreateSummonTowerDefault();
                default: return null;
            }
        }

        public static readonly int[] AllIds =
        {
            SpellIds.S01_Smoke, SpellIds.S02_PoisonField, SpellIds.S03_Fireball, SpellIds.S04_ArrowRain, SpellIds.S05_Tornado,
            SpellIds.S06_Lightning, SpellIds.S07_Gatling, SpellIds.S08_Shield, SpellIds.S09_TowerBuff, SpellIds.S10_Focus,
            SpellIds.S11_AxeWarrior, SpellIds.S12_Mage, SpellIds.S13_Turret,
            SpellIds.H01_Freeze, SpellIds.H02_Laser, SpellIds.H03_Drain, SpellIds.H04_ReflectBarrier, SpellIds.H05_Rage, SpellIds.H06_SummonTower,
        };

        // ── 일반 13종 ────────────────────────────────────────

        public static SpellDefinition CreateSmokeDefault()
        {
            var d = Create(SpellIds.S01_Smoke, "Smoke", "Smoke", new Color(0.45f, 0.45f, 0.5f));
            d.Targeting = SpellTargeting.Column;
            d.Executor = SpellExecutorType.Projectile;
            d.Behavior = SpellBehaviorType.BlindShot;
            d.Dodgeable = true;
            d.BaseDamage = 40f;
            d.ProjectileSpeed = 14f;
            d.ArcHeight = 1.5f;
            d.StatusEffectId = "S01_Blind";
            d.EffectDuration = 5f;
            return d;
        }

        public static SpellDefinition CreatePoisonFieldDefault()
        {
            var d = Create(SpellIds.S02_PoisonField, "Poison Field", "PoisonField", new Color(0.45f, 1f, 0.25f));
            d.Targeting = SpellTargeting.Area;
            d.Executor = SpellExecutorType.Area;
            d.Behavior = SpellBehaviorType.PoisonField;
            d.Dodgeable = false;
            d.BaseDamage = 12f;          // 첫 틱
            d.TickInterval = 0.5f;
            d.TickDamageRamp = 2f;       // 머무를수록 +2
            d.MaxTickDamage = 30f;       // 틱 상한 → 5초 최대 210
            d.EffectDuration = 5f;
            d.FieldHalfDepth = 2.5f;     // 깊이 5m × 3열 전체
            d.Radius = 2.5f;
            d.MaxDepth = 1f;
            return d;
        }

        public static SpellDefinition CreateFireballDefault()
        {
            var d = Create(SpellIds.S03_Fireball, "Fireball", "Fireball", new Color(1f, 0.45f, 0.1f));
            d.Targeting = SpellTargeting.Area;
            d.Executor = SpellExecutorType.Area;
            d.Behavior = SpellBehaviorType.FireballLob;
            d.Dodgeable = false;
            d.BaseDamage = 160f;
            d.Radius = 2.6f;
            d.MaxDepth = 1f;
            d.ProjectileSpeed = 26f;
            d.ArcHeight = 4f;
            return d;
        }

        public static SpellDefinition CreateArrowRainDefault()
        {
            var d = Create(SpellIds.S04_ArrowRain, "Arrow Rain", "ArrowRain", new Color(0.85f, 0.75f, 0.5f));
            d.Targeting = SpellTargeting.Area;
            d.Executor = SpellExecutorType.Area;
            d.Behavior = SpellBehaviorType.ArrowVolley;
            d.Dodgeable = false;
            d.BaseDamage = 32f;          // 화살 1발
            d.ShotCount = 10;
            d.ShotInterval = 0.05f;
            d.ProjectileSpeed = 22f;
            d.ArcHeight = 7f;
            d.Radius = 1.2f;             // 화살 1발 착탄 반경
            d.FieldHalfDepth = 3f;       // 낙하 범위: 레인 폭 × 깊이 ±3m
            d.MaxDepth = 1f;
            return d;
        }

        public static SpellDefinition CreateTornadoDefault()
        {
            var d = Create(SpellIds.S05_Tornado, "Tornado", "Tornado", new Color(0.7f, 0.72f, 0.75f));
            d.Targeting = SpellTargeting.Beam;
            d.Executor = SpellExecutorType.Beam;
            d.Behavior = SpellBehaviorType.TornadoLane;
            d.Dodgeable = false;
            d.BaseDamage = 15f;          // 틱 피해
            d.TickInterval = 0.5f;
            d.EffectDuration = 5f;       // 5초 저지
            d.LaneStartDepth = 0.2f;
            d.LaneEndDepth = 0.8f;
            d.ShotCount = 3;             // 회오리 기둥 수 (표시)
            return d;
        }

        public static SpellDefinition CreateLightningDefault()
        {
            var d = Create(SpellIds.S06_Lightning, "Lightning", "Lightning", new Color(1f, 0.85f, 0.35f));
            d.Targeting = SpellTargeting.Beam;
            d.Executor = SpellExecutorType.Beam;
            d.Behavior = SpellBehaviorType.LightningStrikes;
            d.Dodgeable = true;          // 플레이어는 낙뢰 순간 위치로 판정
            d.BaseDamage = 75f;          // 1회
            d.ShotCount = 3;
            d.ShotInterval = 0.55f;
            d.TelegraphDelay = 0.45f;
            d.Radius = 2f;
            return d;
        }

        public static SpellDefinition CreateGatlingDefault()
        {
            var d = Create(SpellIds.S07_Gatling, "Gatling", "Gatling", new Color(1f, 0.6f, 0.15f));
            d.Targeting = SpellTargeting.Column;
            d.Executor = SpellExecutorType.Projectile;
            d.Behavior = SpellBehaviorType.MultiShotFrontFirst;
            d.Dodgeable = true;
            d.BaseDamage = 17f;          // 15발 × 17 = 255
            d.ProjectileSpeed = 24f;
            d.ShotCount = 15;
            d.ShotInterval = 0.135f;     // 약 2초
            d.Radius = 0.4f;
            return d;
        }

        public static SpellDefinition CreateShieldDefault()
        {
            var d = Create(SpellIds.S08_Shield, "Shield", "Shield", new Color(0.35f, 0.75f, 1f));
            d.Targeting = SpellTargeting.Self;
            d.Executor = SpellExecutorType.Buff;
            d.Behavior = SpellBehaviorType.ShieldRegen;
            d.FocusCategory = SpellFocusCategory.Defense;
            d.StatusEffectId = "S08_Shield";
            d.EffectDuration = 8f;
            d.ShieldAmount = 180f;
            d.RegenDelay = 3f;
            d.RegenPerSecond = 12f;
            return d;
        }

        public static SpellDefinition CreateTowerBuffDefault()
        {
            var d = Create(SpellIds.S09_TowerBuff, "Tower Buff", "TowerBuff", new Color(1f, 0.85f, 0.3f));
            d.Targeting = SpellTargeting.Tower;
            d.Executor = SpellExecutorType.Buff;
            d.Behavior = SpellBehaviorType.TowerBuff;
            d.FocusCategory = SpellFocusCategory.Defense;
            d.StatusEffectId = "S09_TowerBuff";
            d.EffectDuration = 5f;
            d.AttackMultiplier = 1.6f;
            d.IncomingDamageMultiplier = 0.5f;
            return d;
        }

        public static SpellDefinition CreateFocusDefault()
        {
            var d = Create(SpellIds.S10_Focus, "Focus", "Focus", new Color(0.75f, 0.55f, 1f));
            d.Targeting = SpellTargeting.Self;
            d.Executor = SpellExecutorType.Buff;
            d.Behavior = SpellBehaviorType.Focus;
            d.FocusCategory = SpellFocusCategory.Defense;
            d.StatusEffectId = "S10_Focus";
            d.EffectRatio = 1.5f;        // 다음 스킬 ×1.5
            return d;
        }

        public static SpellDefinition CreateAxeWarriorDefault()
        {
            var d = Create(SpellIds.S11_AxeWarrior, "Axe Warrior", "AxeWarrior", new Color(0.5f, 0.85f, 0.35f));
            d.Targeting = SpellTargeting.Summon;
            d.Executor = SpellExecutorType.Summon;
            d.Behavior = SpellBehaviorType.SummonUnit;
            d.FocusCategory = SpellFocusCategory.Summon;
            d.SummonKind = MinionKind.AxeOrc;
            return d;
        }

        public static SpellDefinition CreateDragonDefault()
        {
            var d = Create(SpellIds.S12_Mage, "Dragon", "Mage", new Color(0.95f, 0.3f, 0.2f));
            d.Targeting = SpellTargeting.Summon;
            d.Executor = SpellExecutorType.Summon;
            d.Behavior = SpellBehaviorType.SummonUnit;
            d.FocusCategory = SpellFocusCategory.Summon;
            d.SummonKind = MinionKind.Dragon;
            return d;
        }

        public static SpellDefinition CreateTurretDefault()
        {
            var d = Create(SpellIds.S13_Turret, "Turret", "Turret", new Color(0.6f, 0.75f, 0.9f));
            d.Targeting = SpellTargeting.Structure;
            d.Executor = SpellExecutorType.Structure;
            d.Behavior = SpellBehaviorType.PlaceStructure;
            d.FocusCategory = SpellFocusCategory.Summon;
            d.SummonKind = MinionKind.Turret;
            return d;
        }

        // ── 고위력 6종 ───────────────────────────────────────

        public static SpellDefinition CreateFreezeDefault()
        {
            var d = Create(SpellIds.H01_Freeze, "Freeze", "Freeze", new Color(0.45f, 0.85f, 1f));
            d.Targeting = SpellTargeting.Global;
            d.Executor = SpellExecutorType.Global;
            d.Behavior = SpellBehaviorType.FreezeAll;
            d.FocusCategory = SpellFocusCategory.Defense;
            d.BaseDamage = 40f;
            d.EffectDuration = 5f;
            return d;
        }

        public static SpellDefinition CreateLaserDefault()
        {
            var d = Create(SpellIds.H02_Laser, "Laser Beam", "Laser", new Color(0.55f, 0.9f, 1f));
            d.Targeting = SpellTargeting.Beam;
            d.Executor = SpellExecutorType.Beam;
            d.Behavior = SpellBehaviorType.LaserBeam;
            d.Dodgeable = true;
            d.BaseDamage = 30f;          // 0.2초마다 × 10틱 = 300
            d.TickInterval = 0.2f;
            d.EffectDuration = 2f;
            d.TelegraphDelay = 0.5f;     // §11.2 예고선
            d.Radius = 1.2f;             // 빔 반폭
            return d;
        }

        public static SpellDefinition CreateDrainDefault()
        {
            var d = Create(SpellIds.H03_Drain, "Drain", "Drain", new Color(0.85f, 0.1f, 0.25f));
            d.Targeting = SpellTargeting.Global;
            d.Executor = SpellExecutorType.Global;
            d.Behavior = SpellBehaviorType.DrainLife;
            d.Dodgeable = false;         // 무조건 타겟팅 (방어 효과로만 막힘)
            d.BaseDamage = 200f;
            d.EffectDuration = 2f;       // 생명력이 돌아오는 시간
            d.EffectRatio = 1.5f;        // 실제 피해 × 1.5 회복
            return d;
        }

        public static SpellDefinition CreateReflectDefault()
        {
            var d = Create(SpellIds.H04_ReflectBarrier, "Reflect Barrier", "ReflectBarrier", new Color(0.8f, 0.4f, 1f));
            d.Targeting = SpellTargeting.Self;
            d.Executor = SpellExecutorType.Buff;
            d.Behavior = SpellBehaviorType.ReflectBarrier;
            d.FocusCategory = SpellFocusCategory.Defense;
            d.StatusEffectId = "H04_Reflect";
            d.EffectDuration = 6f;
            d.EffectRatio = 0.8f;        // 피해의 80% 반사
            d.ProjectileSpeed = 30f;
            return d;
        }

        public static SpellDefinition CreateRageDefault()
        {
            var d = Create(SpellIds.H05_Rage, "Rage", "Rage", new Color(1f, 0.25f, 0.15f));
            d.Targeting = SpellTargeting.Global;
            d.Executor = SpellExecutorType.Global;
            d.Behavior = SpellBehaviorType.RageField;
            d.FocusCategory = SpellFocusCategory.Defense;
            d.EffectDuration = 5f;
            d.AttackMultiplier = 1.5f;
            d.AttackSpeedMultiplier = 1.5f;
            d.MoveSpeedMultiplier = 1.4f;
            d.IncomingDamageMultiplier = 0.7f;
            return d;
        }

        public static SpellDefinition CreateSummonTowerDefault()
        {
            var d = Create(SpellIds.H06_SummonTower, "Summon Tower", "SummonTower", new Color(0.65f, 0.55f, 0.4f));
            d.Targeting = SpellTargeting.Structure;
            d.Executor = SpellExecutorType.Structure;
            d.Behavior = SpellBehaviorType.PlaceStructure;
            d.FocusCategory = SpellFocusCategory.Summon;
            d.SummonKind = MinionKind.SummonHut;
            return d;
        }
    }
}
