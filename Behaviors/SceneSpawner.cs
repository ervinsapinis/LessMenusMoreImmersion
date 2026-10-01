using System;
using LessMenusMoreImmersion.Logging;
using SandBox;
using SandBox.Missions.MissionLogics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Brings someone new into a settlement scene the way vanilla brings in its own extras (MissionAgentHandler): they get
    /// the usual campaign agent components and behavior groups, so the street-scene director can move them like anyone.
    /// </summary>
    internal static class SceneSpawner
    {
        /// <param name="civilian">Their town clothes rather than their kit.</param>
        /// <param name="noRanged">A street, not a battlefield: no bows, crossbows, javelins or stones.</param>
        public static Agent? Spawn(Mission mission, CharacterObject character, Vec3 at, float facing, bool civilian, bool noRanged = true)
            => Spawn(mission, character, at, facing, civilian, null, noRanged);

        /// <param name="origin">Where they come from — e.g. a <see cref="PartyAgentOrigin"/> for the player's own troops, so
        /// that (in a scene with vanilla's BattleAgentLogic, as towns and villages have) a man killed or knocked out is
        /// dead or wounded in the party roster too. Null: a nobody (SimpleAgentOrigin), whatever happens to them.</param>
        public static Agent? Spawn(Mission mission, CharacterObject character, Vec3 at, float facing, bool civilian, IAgentOriginBase? origin, bool noRanged = true)
        {
            try
            {
                var handler = mission.GetMissionBehavior<MissionAgentHandler>();
                if (handler == null) return null;
                origin ??= new SimpleAgentOrigin(character, -1, null, default(UniqueTroopDescriptor));
                var data = new AgentData(origin).Monster(TaleWorlds.Core.FaceGen.GetBaseMonsterFromRace(character.Race)).NoHorses(true);
                var lc = new LocationCharacter(data, SandBoxManager.Instance.AgentBehaviorManager.AddFixedCharacterBehaviors,
                    null, true, LocationCharacter.CharacterRelations.Neutral, null, civilian, false, null, false, false, true, null, false);
                var dir = Vec2.FromRotation(facing);
                var rot = Mat3.Identity;
                rot.f = new Vec3(dir.x, dir.y, 0f);
                rot.u = new Vec3(0f, 0f, 1f);
                rot.s = Vec3.CrossProduct(rot.f, rot.u);
                var agent = handler.SpawnWanderingAgentWithInitialFrame(lc, new MatrixFrame(in rot, in at), WeakGameEntity.Invalid, true, false);
                if (agent == null) return null;
                if (noRanged)
                {
                    for (var i = EquipmentIndex.WeaponItemBeginSlot; i < EquipmentIndex.NumAllWeaponSlots; i++)
                    {
                        var w = agent.Equipment[i];
                        if (!w.IsEmpty && (w.CurrentUsageItem?.IsRangedWeapon == true || w.CurrentUsageItem?.IsAmmo == true))
                            agent.RemoveEquippedWeapon(i);
                    }
                }
                return agent;
            }
            catch (Exception ex)
            {
                LmmiLog.Error($"SceneSpawner: couldn't bring {character.Name} into the scene", ex);
                return null;
            }
        }

        /// <summary>Somewhere to stand near a point: on the ground, a little apart from the others.</summary>
        public static Vec3 Around(Mission mission, Vec3 center, int index, int count, float spacing = 0.9f, Vec2? facing = null)
        {
            var f = facing ?? Vec2.Forward;
            var side = new Vec2(-f.y, f.x);
            var at2 = center.AsVec2 + side * ((index - (count - 1) / 2f) * spacing) - f * (index % 2) * 0.8f;
            var at = new Vec3(at2.x, at2.y, center.z);
            at.z = mission.Scene.GetGroundHeightAtPosition(at + new Vec3(0f, 0f, 1.5f));
            return at;
        }
    }
}
