// MatchSimulation — ICombatWorld(데미지 리졸버용) · ISpellWorld(스킬 코드용) 구현
// HP는 ApplyHpChange 한 곳에서만 바뀌며, 그 함수는 ServerDamageResolver만 호출한다(Rule 8).

using System.Collections.Generic;
using SpellboundVR.Arena;
using SpellboundVR.Contracts;
using SpellboundVR.Core;
using SpellboundVR.Network;
using SpellboundVR.Spells;
using SpellboundVR.Spells.Effects;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Combat
{
    public sealed partial class MatchSimulation : ICombatWorld, ISpellWorld
    {
        private static readonly float[] s_sortKeys = new float[MatchSnapshot.MaxUnits];

        // ── ICombatWorld ─────────────────────────────────────

        uint ICombatWorld.Tick => _tick;

        float ICombatWorld.Time => _matchTime;

        public bool TryGetTargetInfo(int targetId, out Team team, out TargetKind kind, out Vector2 position)
        {
            if (TeamUtil.IsPlayerId(targetId))
            {
                team = TeamUtil.TeamOfPlayerId(targetId);
                kind = TargetKind.Player;
                var p = Players[(int)team];
                position = Arena.PlayerPosition(team, p.WorldColumn);
                return p.Present && p.Alive;
            }
            if (TeamUtil.IsStructureId(targetId))
            {
                var s = GetStructureById(targetId);
                team = s != null ? s.Team : Team.Home;
                kind = s != null ? s.Kind : TargetKind.Tower;
                position = s != null ? s.Position : Vector2.zero;
                return s != null && s.Alive;
            }
            var u = FindUnit(targetId);
            if (u != null)
            {
                team = u.Team;
                kind = u.TargetKindSelf;
                position = u.Position;
                return true;
            }
            team = Team.Home;
            kind = TargetKind.Minion;
            position = Vector2.zero;
            return false;
        }

        public int GetPlayerWorldColumn(Team team) => Players[(int)team].WorldColumn;

        public float GetAccuracy(Team attackerTeam) => Players[(int)attackerTeam].Accuracy;

        public float GetAttackMultiplier(Team team) => TeamAttackMultiplier.Compute(team, Defense, _scaling.Stage, Rules);

        public float GetIncomingDamageMultiplier(int targetId)
        {
            if (TeamUtil.IsStructureId(targetId))
            {
                var s = GetStructureById(targetId);
                return s != null && !s.IsNexus && s.IsBuffed(_matchTime) ? s.BuffDefense : 1f;
            }
            var u = FindUnit(targetId);
            return u != null && u.IsRaged(_matchTime) ? u.RageDefense : 1f;
        }

        public float ApplyHpChange(int targetId, float delta, Team sourceTeam)
        {
            if (TeamUtil.IsPlayerId(targetId))
            {
                var p = Players[(int)TeamUtil.TeamOfPlayerId(targetId)];
                if (!p.Alive) return 0f;
                float old = p.Hp;
                p.Hp = Mathf.Clamp(p.Hp + delta, 0f, p.MaxHp);
                if (p.Hp <= 0f) p.DeathPending = true;
                return Mathf.Abs(p.Hp - old);
            }
            if (TeamUtil.IsStructureId(targetId))
            {
                var s = GetStructureById(targetId);
                if (s == null || !s.Alive) return 0f;
                float old = s.Hp;
                s.Hp = Mathf.Clamp(s.Hp + delta, 0f, s.MaxHp);
                if (s.Hp <= 0f) OnStructureDestroyed(s);
                return Mathf.Abs(s.Hp - old);
            }
            var u = FindUnit(targetId);
            if (u == null) return 0f;
            float before = u.Hp;
            u.Hp = Mathf.Clamp(u.Hp + delta, 0f, u.MaxHp);
            if (u.Hp <= 0f) u.Dead = true;
            return Mathf.Abs(u.Hp - before);
        }

        public void ReportHit(int targetId, Vector2 position, float amount, HitResultKind kind, in DamageContext context)
        {
            // 데미지 텍스트 · MISS 표시는 플레이어 스킬 결과 + 플레이어 피격만 보낸다 (대역폭 절약)
            if (!context.IsPlayerSkill && !TeamUtil.IsPlayerId(targetId)) return;
            float height = TeamUtil.IsPlayerId(targetId) ? 1.6f : 1.0f;
            _sink.OnHit(targetId, new Vector3(position.x, height, position.y), Mathf.RoundToInt(amount), kind);
        }

        private void OnStructureDestroyed(StructureState s)
        {
            s.Alive = false;
            s.Hp = 0f;
            Team enemy = TeamUtil.Opponent(s.Team);
            if (s.IsNexus)
                Debug.Log($"[MatchSimulation] {s.Team} 넥서스 파괴 → {enemy} 공격력 +{Rules.NexusDestroyedAttackBonus * 100f:0}%, {s.Team} 회복 중단, 돌파 시작");
            else
                Debug.Log($"[MatchSimulation] {s.Team} 타워 파괴 → {enemy} 공격력 +{Rules.TowerDestroyedAttackBonus * 100f:0}%, {s.Team} 받는 스킬 피해 100%");

            // §14.4 S09 타워 버프: 타워 파괴 후 슬롯 Disabled
            if (!s.IsNexus)
            {
                var owner = Players[(int)s.Team];
                for (int i = 0; i < owner.Slots.Length; i++)
                    if (owner.Slots[i].SpellId == SpellIds.S09_TowerBuff) SlotCooldownService.SetDisabled(owner, i, true);
            }
        }

        // ── ISpellWorld ──────────────────────────────────────

        float ISpellWorld.Time => _matchTime;

        ArenaGeometry ISpellWorld.Arena => Arena;

        IDamageResolver ISpellWorld.Damage => Resolver;

        IStatusEffectHost ISpellWorld.StatusEffects => _status;

        public float Time => _matchTime;

        public int GetPlayerId(Team team) => TeamUtil.PlayerId(team);

        public bool IsPlayerAlive(Team team) => Players[(int)team].Alive;

        public Vector2 GetPlayerPosition(Team team) => Arena.PlayerPosition(team, Players[(int)team].WorldColumn);

        public int CollectEnemyUnitsInColumn(Team casterTeam, int worldColumn, List<int> results)
        {
            results.Clear();
            Team enemy = TeamUtil.Opponent(casterTeam);
            float laneU = Arena.ColumnU(worldColumn);
            float half = Arena.LaneHalfWidth;
            float baseV = Arena.BaseV(casterTeam);

            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Dead || u.Team != enemy || u.IsStructure) continue;
                if (Mathf.Abs(u.Position.x - laneU) > half) continue;
                // 거리순 삽입 정렬 (시전자 발판에서 가까운 순)
                float key = Mathf.Abs(u.Position.y - baseV);
                int idx = results.Count;
                results.Add(u.Id);
                s_sortKeys[idx] = key;
                while (idx > 0 && s_sortKeys[idx - 1] > key)
                {
                    s_sortKeys[idx] = s_sortKeys[idx - 1];
                    results[idx] = results[idx - 1];
                    idx--;
                }
                s_sortKeys[idx] = key;
                results[idx] = u.Id;
            }
            return results.Count;
        }

        public int CollectEnemyUnitsInRadius(Team casterTeam, Vector2 center, float radius, List<int> results)
        {
            results.Clear();
            Team enemy = TeamUtil.Opponent(casterTeam);
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Dead || u.Team != enemy || u.IsStructure) continue;
                float r = radius + u.Def.BodyRadius;
                if ((u.Position - center).sqrMagnitude <= r * r) results.Add(u.Id);
            }
            return results.Count;
        }

        public int CollectEnemyUnitsInRect(Team casterTeam, float uMin, float uMax, float vMin, float vMax, List<int> results)
        {
            results.Clear();
            Team enemy = TeamUtil.Opponent(casterTeam);
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Dead || u.Team != enemy || u.IsStructure) continue;
                float r = u.Def.BodyRadius;
                if (u.Position.x < uMin - r || u.Position.x > uMax + r) continue;
                if (u.Position.y < vMin - r || u.Position.y > vMax + r) continue;
                results.Add(u.Id);
            }
            return results.Count;
        }

        public int CollectTeamUnits(Team team, bool includeStructures, List<int> results)
        {
            results.Clear();
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (!u.Active || u.Dead || u.Team != team) continue;
                if (!includeStructures && u.IsStructure) continue;
                results.Add(u.Id);
            }
            return results.Count;
        }

        public bool TryGetUnitInfo(int unitId, out Vector2 position, out TargetKind kind, out float hp)
        {
            var u = FindUnit(unitId);
            if (u == null)
            {
                position = Vector2.zero;
                kind = TargetKind.Minion;
                hp = 0f;
                return false;
            }
            position = u.Position;
            kind = u.TargetKindSelf;
            hp = u.Hp;
            return true;
        }

        public void LaunchProjectile(in ProjectileLaunch launch)
        {
            _projectiles.Launch(launch);
        }

        public void StartRoutine(ISpellRoutine routine)
        {
            if (routine != null) _routines.Add(routine);
        }

        public void AddStatusEffect(int targetId, IStatusEffect effect)
        {
            _status.Add(targetId, effect);
        }

        public float Heal(int targetId, float amount, int sourceSpellId)
        {
            if (!TryGetTargetInfo(targetId, out Team team, out _, out Vector2 pos)) return 0f;
            float healed = Resolver.ResolveHeal(targetId, amount, team);
            // 큰 회복(H03 흡혈 등)만 +HP 표시 — 쉴드 재생 같은 틱 회복은 표시하지 않는다
            if (healed >= 5f) ReportHit(targetId, pos, healed, HitResultKind.Heal, new DamageContext { SpellId = sourceSpellId, IsPlayerSkill = true });
            return healed;
        }

        public void PublishFx(int spellId, VfxPart part, Vector2 arenaPosition, float height, int worldColumn)
        {
            _sink.OnSpellFx(spellId, part, new Vector3(arenaPosition.x, height, arenaPosition.y), worldColumn);
        }

        // ── ISpellWorld: 상태 제어 ───────────────────────────────

        float ISpellWorld.CastFocus => _castFocus;

        public void FreezeUnit(int unitId, float duration)
        {
            var u = FindUnit(unitId);
            if (u != null) u.FrozenUntil = Mathf.Max(u.FrozenUntil, _matchTime + duration);
        }

        public void FreezeTower(Team team, float duration)
        {
            var tower = Defense.Tower(team);
            if (tower.Alive) tower.FrozenUntil = Mathf.Max(tower.FrozenUntil, _matchTime + duration);
        }

        public void StunUnit(int unitId, float duration)
        {
            var u = FindUnit(unitId);
            if (u != null) u.StunnedUntil = Mathf.Max(u.StunnedUntil, _matchTime + duration);
        }

        public void RageUnit(int unitId, float duration, float attack, float attackSpeed, float move, float incomingDamage)
        {
            var u = FindUnit(unitId);
            if (u == null) return;
            u.RageUntil = Mathf.Max(u.RageUntil, _matchTime + duration);
            u.RageAttack = attack;
            u.RageAttackSpeed = Mathf.Max(0.1f, attackSpeed);
            u.RageMove = move;
            u.RageDefense = incomingDamage;
        }

        public bool IsTowerAlive(Team team) => Defense.TowerAlive(team);

        public void BuffTower(Team team, float duration, float attack, float incomingDamage)
        {
            var tower = Defense.Tower(team);
            if (!tower.Alive) return;
            tower.BuffUntil = Mathf.Max(tower.BuffUntil, _matchTime + duration);
            tower.BuffAttack = attack;
            tower.BuffDefense = incomingDamage;
        }

        public void BlindPlayer(Team team, float duration)
        {
            var p = Players[(int)team];
            p.BlindUntil = Mathf.Max(p.BlindUntil, _matchTime + duration);
        }

        public void SetPendingFocus(Team team, float multiplier)
        {
            Players[(int)team].PendingFocus = Mathf.Max(1f, multiplier);
        }

        bool ISpellWorld.SpawnUnit(Team team, MinionKind kind, int lane, Vector2 position, float hpMultiplier, out int unitId)
        {
            unitId = 0;
            if (!AliveUnitLimiter.CanSpawn(this, team)) return false;
            return SpawnUnit(team, GetMinionDefinition(kind), lane, position, hpMultiplier, out unitId);
        }

        public int FindTeamUnit(Team team, MinionKind kind)
        {
            for (int i = 0; i < Units.Length; i++)
            {
                var u = Units[i];
                if (u.Active && !u.Dead && u.Team == team && u.Kind == kind) return u.Id;
            }
            return 0;
        }

        public void RemoveUnit(int unitId)
        {
            var u = FindUnit(unitId);
            if (u == null) return;
            u.Dead = true;
            _status.ClearTarget(u.Id);
            u.Deactivate();
        }

        public Vector2 GetPlacementPosition(Team team, int worldColumn)
        {
            float depth = (Defense.TowerAlive(team) ? Arena.TowerDepth : Arena.NexusDepth) + Rules.PlacementOffset;
            return new Vector2(Arena.ColumnU(worldColumn), Arena.BaseV(team) + Arena.ForwardSign(team) * depth);
        }
    }
}
