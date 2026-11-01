// 서버 투사체 상태 (§11.2 열 지정 투사체: 도착 시점 서버 위치로 회피 판정)

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class ProjectileState
    {
        public bool Active;
        public int Id;
        public int SpellId;
        public Team Team;
        /// <summary>날아가는 레인 (Impact 표시 · 1:1 은 조준한 열과 같다)</summary>
        public int WorldColumn;
        /// <summary>플레이어 대상일 때 그 상대</summary>
        public Team TargetTeam;
        /// <summary>플레이어 대상일 때 상대 발판의 어느 칸(월드 열)으로 가나 — 도착 때 그 칸에 없으면 회피</summary>
        public int AimColumn;
        public Vector2 Position;
        public float Height;
        public float Speed;
        public float Damage;
        public int TargetUnitId;
        public Vector2 LastKnownTarget;
        public bool Dodgeable;
        public bool IsPlayerSkill;
        public float FocusBonus;

        public ProjectileMode Mode;
        public Vector2 TargetPoint;
        public float StartHeight;
        public float TargetHeight;
        public float ArcHeight;
        public float StartDistance;
        public bool IsReflected;
        public bool SuppressImpactFx;
        public IProjectileHitHandler OnHit;

        public bool TargetsPlayer => Mode == ProjectileMode.Auto && TargetUnitId == 0;
    }
}
