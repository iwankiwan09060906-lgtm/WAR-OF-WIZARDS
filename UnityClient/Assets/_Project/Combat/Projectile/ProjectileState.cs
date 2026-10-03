// 서버 투사체 상태 (§11.2 열 지정 투사체: 도착 시점 서버 위치로 회피 판정)

using SpellboundVR.Contracts;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class ProjectileState
    {
        public bool Active;
        public int Id;
        public int SpellId;
        public Team Team;
        public int WorldColumn;
        public Vector2 Position;
        public float Height;
        public float Speed;
        public float Damage;
        public int TargetUnitId;
        public Vector2 LastKnownTarget;
        public bool Dodgeable;
        public bool IsPlayerSkill;
        public float FocusBonus;

        public bool TargetsPlayer => TargetUnitId == 0;
    }
}
