// §16 미니언 · 소환수 · 설치물 서버 상태. 풀 배열에 미리 할당해 재사용한다(§15.1, §17).

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
        /// <summary>H01 프리즈 — 이동 · 공격 정지</summary>
        public float FrozenUntil;
        /// <summary>S05 토네이도 — 이동 · 공격 정지 (회오리에 붙잡힘)</summary>
        public float StunnedUntil;
        /// <summary>H05 분노 — 공격력 · 공격 속도 · 이동 속도 · 방어력 증가</summary>
        public float RageUntil;
        public float RageAttack = 1f;
        public float RageAttackSpeed = 1f;
        public float RageMove = 1f;
        public float RageDefense = 1f;
        public float AttackMultiplier = 1f;
        /// <summary>공격 횟수 (표시용 — 클라이언트 공격 이펙트)</summary>
        public int AttackCount;
        /// <summary>설치물 소환 타이머 (H06)</summary>
        public float SpawnTimer;

        /// <summary>적 타워 사거리에 처음 들어온 시각 (§18 Target Priority: 먼저 진입한 유닛), 미진입 = -1</summary>
        public float EnteredEnemyTowerRangeTime = -1f;

        public bool IsStructure => TargetKindSelf == TargetKind.Structure;

        public bool IsRaged(float now) => RageUntil > now;

        public void Activate(int id, Team team, MinionDefinition def, int lane, Vector2 position, float time, float hpMultiplier = 1f)
        {
            Active = true;
            Dead = false;
            Id = id;
            Team = team;
            Def = def;
            Kind = def.Kind;
            TargetKindSelf = def.TargetKindSelf;
            Lane = lane;
            Position = position;
            MaxHp = def.MaxHp * Mathf.Max(0.1f, hpMultiplier);
            Hp = MaxHp;
            AttackTimer = def.AttackInterval * 0.5f;
            RetargetTimer = 0f;
            TargetId = 0;
            SpawnTime = time;
            State = UnitVisualState.Moving;
            FrozenUntil = -1f;
            StunnedUntil = -1f;
            RageUntil = -1f;
            RageAttack = RageAttackSpeed = RageMove = RageDefense = 1f;
            AttackMultiplier = 1f;
            AttackCount = 0;
            SpawnTimer = def.SpawnInterval > 0f ? def.SpawnInterval * 0.5f : 0f;
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
