// §16 미니언 · 소환수 서버 상태. 풀 배열에 미리 할당해 재사용한다(§15.1, §17).

using SpellboundVR.Contracts;
using SpellboundVR.Network;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed class UnitState
    {
        public bool Active;
        public bool Dead;
        public int Id;
        public Team Team;
        public MinionKind Kind;
        public MinionDefinition Def;
        public TargetKind TargetKindSelf;
        public int Lane;
        public Vector2 Position;
        public float Hp;
        public float MaxHp;
        public float AttackTimer;
        public float RetargetTimer;
        public int TargetId;
        public float SpawnTime;
        public UnitVisualState State;
        public float FrozenUntil;
        public float AttackMultiplier = 1f;

        /// <summary>적 타워 사거리에 처음 들어온 시각 (§18 Target Priority: 먼저 진입한 유닛), 미진입 = -1</summary>
        public float EnteredEnemyTowerRangeTime = -1f;

        public void Activate(int id, Team team, MinionDefinition def, int lane, Vector2 position, float time)
        {
            Active = true;
            Dead = false;
            Id = id;
            Team = team;
            Def = def;
            Kind = def.Kind;
            TargetKindSelf = TargetKind.Minion;
            Lane = lane;
            Position = position;
            MaxHp = def.MaxHp;
            Hp = def.MaxHp;
            AttackTimer = def.AttackInterval * 0.5f;
            RetargetTimer = 0f;
            TargetId = 0;
            SpawnTime = time;
            State = UnitVisualState.Moving;
            FrozenUntil = -1f;
            AttackMultiplier = 1f;
            EnteredEnemyTowerRangeTime = -1f;
        }

        public void Deactivate()
        {
            Active = false;
            Dead = false;
            Def = null;
            TargetId = 0;
        }
    }
}
