// 스킬 Executor · Behavior · Effect가 서버 시뮬레이션에 접근하는 유일한 창구.
// §14.1 스킬 코드는 PC 서버에서 실행되며 플레이어와 봇이 같은 코드를 쓴다.
// §12.5 데미지는 반드시 Damage(IDamageResolver)를 통과한다 — HP 직접 변경 API는 제공하지 않는다.
// 좌표는 아레나 공간(u, v)이다 (ArenaGeometry).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Spells.Effects;
using UnityEngine;

namespace SpellboundVR.Spells.Executor
{
    public interface ISpellWorld
    {
        /// <summary>경기 시간 (초)</summary>
        float Time { get; }

        ArenaGeometry Arena { get; }

        IDamageResolver Damage { get; }

        IStatusEffectHost StatusEffects { get; }

        int GetPlayerId(Team team);

        int GetPlayerWorldColumn(Team team);

        bool IsPlayerAlive(Team team);

        /// <summary>플레이어 몸 중심 위치 (아레나 공간)</summary>
        Vector2 GetPlayerPosition(Team team);

        /// <summary>
        /// 시전자 기준 적 유닛(미니언 · 소환수) 중 해당 열에 있는 것을 시전자에 가까운 순서로 채운다.
        /// 구조물은 포함하지 않는다(§11 플레이어 스킬은 구조물 공격 불가).
        /// </summary>
        int CollectEnemyUnitsInColumn(Team casterTeam, int worldColumn, List<int> results);

        /// <summary>시전자 기준 적 유닛 중 반경 안에 있는 것을 채운다 (구조물 제외).</summary>
        int CollectEnemyUnitsInRadius(Team casterTeam, Vector2 center, float radius, List<int> results);

        bool TryGetUnitInfo(int unitId, out Vector2 position, out TargetKind kind, out float hp);

        void LaunchProjectile(in ProjectileLaunch launch);

        /// <summary>여러 틱에 걸친 스킬 동작(연사 등)을 등록한다.</summary>
        void StartRoutine(ISpellRoutine routine);

        /// <summary>동작을 가진 상태 효과를 붙인다 (같은 EffectId면 갱신).</summary>
        void AddStatusEffect(int targetId, IStatusEffect effect);

        /// <summary>회복도 서버 리졸버를 통과한다. 실제 회복량을 반환한다.</summary>
        float Heal(int targetId, float amount, int sourceSpellId);

        void PublishFx(int spellId, VfxPart part, Vector2 arenaPosition, float height, int worldColumn);
    }

    /// <summary>여러 틱에 걸쳐 실행되는 스킬 동작. Tick이 false를 반환하면 종료.</summary>
    public interface ISpellRoutine
    {
        bool Tick(float deltaTime, ISpellWorld world);
    }

    /// <summary>서버 투사체 발사 정보</summary>
    public struct ProjectileLaunch
    {
        public int SpellId;
        public Team CasterTeam;
        public int WorldColumn;
        public Vector2 From;
        public float Height;
        public float Speed;
        public float Damage;
        /// <summary>0 이면 상대 플레이어(열 기준 회피 판정)</summary>
        public int TargetUnitId;
        public bool Dodgeable;
        public bool IsPlayerSkill;
        public float FocusBonus;
    }
}
