// S07 총난사 Behavior (§14.4)
//   "열 안 적을 거리순 정렬 후 한 발씩, 앞에 유닛이 없으면 플레이어"
//   · ShotInterval 간격으로 ShotCount 발 발사
//   · 매 발마다 열 안 적 유닛을 시전자에 가까운 순으로 보고, 이미 배정된 탄으로 죽을 유닛은 건너뛴다
//   · 남은 유닛이 없으면 상대 플레이어를 향해 발사 (열 지정 투사체 → 도착 시점 회피 판정)

using System.Collections.Generic;
using SpellboundVR.Contracts;
using SpellboundVR.Spells.Executor;
using UnityEngine;

namespace SpellboundVR.Spells.Behaviors
{
    public sealed class MultiShotFrontFirst : ISpellRoutine
    {
        private static readonly List<int> s_buffer = new List<int>(32);

        private readonly SpellDefinition _def;
        private readonly Team _team;
        private readonly int _worldColumn;
        private readonly int[] _assignedIds;
        private readonly float[] _assignedDamage;
        private int _assignedCount;
        private int _shotsLeft;
        private float _timer;
        private bool _castFxSent;

        /// <param name="shotCountOverride">0 이하이면 definition.ShotCount 사용</param>
        public MultiShotFrontFirst(SpellDefinition definition, Team casterTeam, int worldColumn, int shotCountOverride = 0)
        {
            _def = definition;
            _team = casterTeam;
            _worldColumn = worldColumn;
            _shotsLeft = Mathf.Max(1, shotCountOverride > 0 ? shotCountOverride : definition.ShotCount);
            _assignedIds = new int[_shotsLeft];
            _assignedDamage = new float[_shotsLeft];
            _timer = 0f;
        }

        public bool Tick(float deltaTime, ISpellWorld world)
        {
            if (!world.IsPlayerAlive(_team)) return false;

            if (!_castFxSent)
            {
                _castFxSent = true;
                world.PublishFx(_def.SpellId, VfxPart.Cast, MuzzlePosition(world), 1.5f, _worldColumn);
            }

            _timer -= deltaTime;
            while (_timer <= 0f && _shotsLeft > 0)
            {
                FireOne(world);
                _shotsLeft--;
                _timer += Mathf.Max(0.02f, _def.ShotInterval);
            }
            return _shotsLeft > 0;
        }

        private Vector2 MuzzlePosition(ISpellWorld world)
        {
            Vector2 p = world.GetPlayerPosition(_team);
            p.y += world.Arena.ForwardSign(_team) * 0.6f;
            return p;
        }

        private void FireOne(ISpellWorld world)
        {
            int targetUnit = 0;
            world.CollectEnemyUnitsInColumn(_team, _worldColumn, s_buffer);
            for (int i = 0; i < s_buffer.Count; i++)
            {
                int id = s_buffer[i];
                if (!world.TryGetUnitInfo(id, out _, out _, out float hp)) continue;
                float pending = PendingDamage(id);
                if (pending >= hp) continue;
                targetUnit = id;
                break;
            }

            if (targetUnit != 0) AddPending(targetUnit, _def.BaseDamage);

            var launch = new ProjectileLaunch
            {
                SpellId = _def.SpellId,
                CasterTeam = _team,
                WorldColumn = _worldColumn,
                From = MuzzlePosition(world),
                Height = 1.5f,
                Speed = Mathf.Max(1f, _def.ProjectileSpeed),
                Damage = _def.BaseDamage,
                TargetUnitId = targetUnit,
                Dodgeable = _def.Dodgeable,
                IsPlayerSkill = true,
                FocusBonus = 1f,
            };
            world.LaunchProjectile(launch);
        }

        private float PendingDamage(int id)
        {
            for (int i = 0; i < _assignedCount; i++)
                if (_assignedIds[i] == id) return _assignedDamage[i];
            return 0f;
        }

        private void AddPending(int id, float dmg)
        {
            for (int i = 0; i < _assignedCount; i++)
            {
                if (_assignedIds[i] == id)
                {
                    _assignedDamage[i] += dmg;
                    return;
                }
            }
            if (_assignedCount < _assignedIds.Length)
            {
                _assignedIds[_assignedCount] = id;
                _assignedDamage[_assignedCount] = dmg;
                _assignedCount++;
            }
        }
    }
}
