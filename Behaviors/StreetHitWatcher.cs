using System;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Tells the street events when you land a blow on someone, so wading into a scene with your fists — a brawl,
    /// the men around the girl, a fleeing thief — makes it your fight. Added to campaign missions by SubModule.
    /// </summary>
    public class StreetHitWatcher : MissionLogic
    {
        public override void OnAgentHit(Agent affectedAgent, Agent affectorAgent, in MissionWeapon affectorWeapon, in Blow blow, in AttackCollisionData attackCollisionData)
        {
            try
            {
                if (affectorAgent != null && affectorAgent == Agent.Main && affectedAgent != null && affectedAgent.IsHuman)
                    StreetEventsBehavior.OnPlayerHit(affectedAgent);
            }
            catch (Exception ex) { LmmiLog.Error("StreetHitWatcher.OnAgentHit threw", ex); }
        }

        /// <summary>Leaving the scene is always allowed; resisting arrest, whether you get away depends on who's close.</summary>
        public override InquiryData OnEndMissionRequest(out bool canPlayerLeave)
        {
            canPlayerLeave = true;
            try { StreetEventsBehavior.OnLeavingScene(); }
            catch (Exception ex) { LmmiLog.Error("StreetHitWatcher.OnEndMissionRequest threw", ex); }
            return null!;
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
            try
            {
                if (affectedAgent != null && affectedAgent.IsHuman)
                    StreetEventsBehavior.OnAgentRemoved(affectedAgent, affectorAgent, agentState);
            }
            catch (Exception ex) { LmmiLog.Error("StreetHitWatcher.OnAgentRemoved threw", ex); }
        }
    }
}
