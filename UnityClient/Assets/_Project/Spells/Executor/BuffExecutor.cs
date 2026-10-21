// §14.2 BuffExecutor — 조준 없음(주먹 즉시 시전) / 스킬: S08 방어 쉴드, S09 타워 버프, S10 집중, H04 반사 결계
// S10 집중을 받은 방어 스킬은 지속 시간 × 집중 배율.

using SpellboundVR.Contracts;
using SpellboundVR.Spells.Behaviors;
using SpellboundVR.Spells.Effects;

namespace SpellboundVR.Spells.Executor
{
    public sealed class BuffExecutor : ISpellExecutor
    {
        private readonly SpellDefinition _def;
        private readonly ISpellWorld _world;

        public BuffExecutor(SpellDefinition definition, ISpellWorld world)
        {
            _def = definition;
            _world = world;
        }

        public void Execute(in SpellCastRequest request, Team casterTeam)
        {
            int playerId = _world.GetPlayerId(casterTeam);
            int column = _world.GetPlayerWorldColumn(casterTeam);
            var pos = _world.GetPlayerPosition(casterTeam);
            float durationScale = SpellDamage.DurationFocus(_def, _world.CastFocus);

            switch (_def.Behavior)
            {
                case SpellBehaviorType.ShieldRegen:
                    _world.AddStatusEffect(playerId, new ShieldRegenEffect(_def, durationScale));
                    break;

                case SpellBehaviorType.ReflectBarrier:
                    _world.AddStatusEffect(playerId, new ReflectBarrierEffect(_def, durationScale));
                    break;

                case SpellBehaviorType.TowerBuff:
                {
                    var tower = _world.Arena.StructurePosition(casterTeam, false);
                    _world.BuffTower(casterTeam, _def.EffectDuration * durationScale, _def.AttackMultiplier, _def.IncomingDamageMultiplier);
                    _world.PublishFx(_def.SpellId, VfxPart.Cast, tower, 0.05f, 1);
                    return;
                }

                case SpellBehaviorType.Focus:
                    _world.SetPendingFocus(casterTeam, _def.EffectRatio);
                    break;

                default:
                    if (!string.IsNullOrEmpty(_def.StatusEffectId) && _def.EffectDuration > 0f)
                        _world.StatusEffects.ApplyStatusEffect(playerId, _def.StatusEffectId, _def.EffectDuration * durationScale);
                    break;
            }

            _world.PublishFx(_def.SpellId, VfxPart.Cast, pos, 0.05f, column);
        }
    }
}
