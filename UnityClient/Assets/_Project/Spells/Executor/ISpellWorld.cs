// 스킬 Executor · Behavior · Effect가 서버 시뮬레이션에 접근하는 유일한 창구.
// §14.1 스킬 코드는 PC 서버에서 실행되며 플레이어와 봇이 같은 코드를 쓴다.
// §12.5 데미지는 반드시 Damage(IDamageResolver)를 통과한다 — HP 직접 변경 API는 제공하지 않는다.
// 좌표는 아레나 공간(u, v)이다 (ArenaGeometry).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Combat;
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

        /// <summary>
        /// 지금 실행 중인 시전의 S10 집중 배율 (1 = 없음). Executor.Execute 안에서만 유효하므로
        /// 여러 틱에 걸친 동작은 생성 시점에 값을 복사해 둔다.
        /// </summary>
        float CastFocus { get; }

        int GetPlayerId(Team team);

        int GetPlayerWorldColumn(Team team);

        bool IsPlayerAlive(Team team);

        /// <summary>플레이어 몸 중심 위치 (아레나 공간)</summary>
        Vector2 GetPlayerPosition(Team team);

        /// <summary>
        /// 시전자 기준 적 유닛(미니언 · 소환수) 중 해당 열에 있는 것을 시전자에 가까운 순서로 채운다.
        /// 구조물 · 설치물은 포함하지 않는다(§11 플레이어 스킬은 구조물 공격 불가).
        /// </summary>
        int CollectEnemyUnitsInColumn(Team casterTeam, int worldColumn, List<int> results);

        /// <summary>시전자 기준 적 유닛 중 반경 안에 있는 것을 채운다 (구조물 · 설치물 제외).</summary>
        int CollectEnemyUnitsInRadius(Team casterTeam, Vector2 center, float radius, List<int> results);

        /// <summary>시전자 기준 적 유닛 중 v가 [vMin, vMax], u가 [uMin, uMax] 안인 것 (설치물 제외)</summary>
        int CollectEnemyUnitsInRect(Team casterTeam, float uMin, float uMax, float vMin, float vMax, List<int> results);

        /// <summary>팀의 살아있는 모든 유닛 (includeStructures면 설치물 포함)</summary>
        int CollectTeamUnits(Team team, bool includeStructures, List<int> results);

        bool TryGetUnitInfo(int unitId, out Vector2 position, out TargetKind kind, out float hp);

        void LaunchProjectile(in ProjectileLaunch launch);

        /// <summary>여러 틱에 걸친 스킬 동작(연사 등)을 등록한다.</summary>
        void StartRoutine(ISpellRoutine routine);

        /// <summary>동작을 가진 상태 효과를 붙인다 (같은 EffectId면 갱신).</summary>
        void AddStatusEffect(int targetId, IStatusEffect effect);

        /// <summary>회복도 서버 리졸버를 통과한다. 실제 회복량을 반환한다.</summary>
        float Heal(int targetId, float amount, int sourceSpellId);

        void PublishFx(int spellId, VfxPart part, Vector2 arenaPosition, float height, int worldColumn);

        // ── 상태 제어 (스킬 효과) ─────────────────────────────

        /// <summary>H01 — 유닛 이동 · 공격 정지</summary>
        void FreezeUnit(int unitId, float duration);

        /// <summary>H01 — 그 팀의 타워 공격 정지</summary>
        void FreezeTower(Team team, float duration);

        /// <summary>S05 — 유닛 붙잡기 (이동 · 공격 정지)</summary>
        void StunUnit(int unitId, float duration);

        /// <summary>H05 — 유닛 강화</summary>
        void RageUnit(int unitId, float duration, float attack, float attackSpeed, float move, float incomingDamage);

        bool IsTowerAlive(Team team);

        /// <summary>S09 — 타워 공격력 · 받는 피해 배율</summary>
        void BuffTower(Team team, float duration, float attack, float incomingDamage);

        /// <summary>S01 — 상대 시야 암전</summary>
        void BlindPlayer(Team team, float duration);

        /// <summary>S10 — 다음 시전에 쓸 집중 배율 대기</summary>
        void SetPendingFocus(Team team, float multiplier);

        /// <summary>소환수 · 설치물 생성. 진영 생존 상한이면 false</summary>
        bool SpawnUnit(Team team, MinionKind kind, int lane, Vector2 position, float hpMultiplier, out int unitId);

        /// <summary>그 팀의 해당 종류 유닛 하나 (없으면 0) — 설치물 진영당 1기 규칙용</summary>
        int FindTeamUnit(Team team, MinionKind kind);

        /// <summary>유닛 즉시 제거 (설치물 교체)</summary>
        void RemoveUnit(int unitId);

        /// <summary>설치 구역 (§14.4): 타워가 있으면 타워 앞, 없으면 넥서스 앞</summary>
        Vector2 GetPlacementPosition(Team team, int worldColumn);
    }

    /// <summary>여러 틱에 걸쳐 실행되는 스킬 동작. Tick이 false를 반환하면 종료.</summary>
    public interface ISpellRoutine
    {
        bool Tick(float deltaTime, ISpellWorld world);
    }

    public enum ProjectileMode : byte
    {
        /// <summary>TargetUnitId가 0이면 상대 플레이어(열 기준 회피 판정), 아니면 그 유닛 추적</summary>
        Auto,
        /// <summary>지정 지점으로 비행 — 피해는 OnHit 처리기가 준다</summary>
        Point,
        /// <summary>시전자 자신에게 돌아옴 (H03 흡혈 회복) — OnHit 처리기만 호출</summary>
        OwnPlayer,
    }

    /// <summary>투사체 도착 정보 (OnHit 처리기에 전달)</summary>
    public struct ProjectileArrival
    {
        public int SpellId;
        public Team CasterTeam;
        public int WorldColumn;
        public Vector2 Position;
        public float Height;
        public float Damage;
        public float FocusBonus;
        public int TargetId;
        /// <summary>Auto 모드에서 리졸버가 실제로 적용한 피해 (빗나감 · 회피 · 흡수면 0)</summary>
        public float AppliedDamage;
    }

    /// <summary>투사체가 도착했을 때 스킬별 후처리 (연막 암전 · 지점 폭발 · 흡혈 회복)</summary>
    public interface IProjectileHitHandler
    {
        void OnProjectileArrived(ISpellWorld world, in ProjectileArrival arrival);
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
        /// <summary>Auto 모드: 0 이면 상대 플레이어(열 기준 회피 판정)</summary>
        public int TargetUnitId;
        public bool Dodgeable;
        public bool IsPlayerSkill;
        public float FocusBonus;

        public ProjectileMode Mode;
        /// <summary>Point 모드 도착 지점</summary>
        public Vector2 TargetPoint;
        /// <summary>도착 높이 (0 이하면 대상 종류별 기본값)</summary>
        public float TargetHeight;
        /// <summary>포물선 꼭대기 추가 높이</summary>
        public float ArcHeight;
        public bool IsReflected;
        /// <summary>도착 시 Impact VFX를 서버가 자동 발행하지 않는다 (처리기가 직접)</summary>
        public bool SuppressImpactFx;
        public IProjectileHitHandler OnHit;
    }
}
