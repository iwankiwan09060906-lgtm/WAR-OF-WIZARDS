// §14.1 모든 스킬은 SpellDefinition SO로 데이터화한다.
// §14.2 실행 코드는 Executor 7종 + Behavior 조합으로 재사용한다 (Rule 11).
// 수치 기본값은 "추후 설정"(§3.3)을 위한 임시값이며 에셋에서 조정한다.

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Spells
{
    public enum SpellExecutorType : byte
    {
        Projectile,
        Area,
        Beam,
        Summon,
        Structure,
        Buff,
        Global,
    }

    public enum SpellBehaviorType : byte
    {
        None,
        /// <summary>S07 — 열 안 적을 거리순 정렬 후 한 발씩, 앞에 유닛이 없으면 플레이어</summary>
        MultiShotFrontFirst,
        /// <summary>S03 — 지점 주변 범위 피해</summary>
        Splash,
        /// <summary>S08 — 피해 흡수 + 무피격 시 HP 회복</summary>
        ShieldRegen,
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

        [Header("상태 효과")]
        public string StatusEffectId = "";
        public float EffectDuration = 0f;
        public float ShieldAmount = 0f;
        [Tooltip("이 시간 동안 피격이 없으면 HP 회복 시작 (초)")]
        public float RegenDelay = 3f;
        public float RegenPerSecond = 0f;

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

        // ── 구현 대상 3종의 기본값 (§14.7 W2: S03, S07, S08) ─────────────────────

        public static SpellDefinition CreateFireballDefault()
        {
            var d = Create(SpellIds.S03_Fireball, "Fireball", "Fireball", new Color(1f, 0.45f, 0.1f));
            d.Targeting = SpellTargeting.Area;
            d.Executor = SpellExecutorType.Area;
            d.Behavior = SpellBehaviorType.Splash;
            d.Dodgeable = false;
            d.BaseDamage = 120f;
            d.Radius = 2.6f;
            d.MaxDepth = 1f;
            return d;
        }

        public static SpellDefinition CreateGatlingDefault()
        {
            var d = Create(SpellIds.S07_Gatling, "Gatling", "Gatling", new Color(1f, 0.9f, 0.3f));
            d.Targeting = SpellTargeting.Column;
            d.Executor = SpellExecutorType.Projectile;
            d.Behavior = SpellBehaviorType.MultiShotFrontFirst;
            d.Dodgeable = true;
            d.BaseDamage = 22f;
            d.ProjectileSpeed = 12f;
            d.ShotCount = 8;
            d.ShotInterval = 0.12f;
            d.Radius = 0.4f;
            return d;
        }

        public static SpellDefinition CreateShieldDefault()
        {
            var d = Create(SpellIds.S08_Shield, "Shield", "Shield", new Color(0.35f, 0.75f, 1f));
            d.Targeting = SpellTargeting.Self;
            d.Executor = SpellExecutorType.Buff;
            d.Behavior = SpellBehaviorType.ShieldRegen;
            d.StatusEffectId = "S08_Shield";
            d.EffectDuration = 8f;
            d.ShieldAmount = 180f;
            d.RegenDelay = 3f;
            d.RegenPerSecond = 12f;
            return d;
        }
    }
}
