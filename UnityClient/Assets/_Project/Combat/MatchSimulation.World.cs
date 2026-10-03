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
                if (!u.Active || u.Dead || u.Team != enemy) continue;
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
                if (!u.Active || u.Dead || u.Team != enemy) continue;
                float r = radius + u.Def.BodyRadius;
                if ((u.Position - center).sqrMagnitude <= r * r) results.Add(u.Id);
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
            if (!TryGetTargetInfo(targetId, out Team team, out _, out _)) return 0f;
            return Resolver.ResolveHeal(targetId, amount, team);
        }

        public void PublishFx(int spellId, VfxPart part, Vector2 arenaPosition, float height, int worldColumn)
        {
            _sink.OnSpellFx(spellId, part, new Vector3(arenaPosition.x, height, arenaPosition.y), worldColumn);
        }
    }
}
