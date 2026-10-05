// §18 타워 · §19 넥서스 서버 상태

using SpellboundVR.Contracts;
using SpellboundVR.Core;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class StructureState
    {
        public readonly Team Team;
        public readonly bool IsNexus;
        public readonly int Id;

        public float Hp;
        public float MaxHp;
        public bool Alive;
        public Vector2 Position;
        public float Radius;

        // 타워 공격 상태 (§18)
        public float AttackTimer;
        public int LastTargetId;
        public int ComboCount;
        /// <summary>누적 발사 횟수 (표시용 — 클라이언트 총알)</summary>
        public int ShotCount;

        public StructureState(Team team, bool isNexus)
        {
            Team = team;
            IsNexus = isNexus;
            Id = TeamUtil.StructureId(team, isNexus);
        }

        public TargetKind Kind => IsNexus ? TargetKind.Nexus : TargetKind.Tower;

        public void Reset(float maxHp, Vector2 position, float radius)
        {
            MaxHp = maxHp;
            Hp = maxHp;
            Alive = true;
            Position = position;
            Radius = radius;
            AttackTimer = 0f;
            LastTargetId = 0;
            ComboCount = 0;
            ShotCount = 0;
        }
    }
}
