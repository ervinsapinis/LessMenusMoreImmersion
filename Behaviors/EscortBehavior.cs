using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Settlements;
using SandBox;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using SandBox.Missions.AgentBehaviors;
using SandBox.Missions.MissionLogics;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Logging;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Manages NPC escort behavior using the vanilla EscortAgentBehavior.
    /// NPCs walk to destinations and wait for player. Feature unlocks only when player reaches destination.
    /// </summary>
    public class EscortBehavior
    {
        private Agent? _npcAgent;
        private Vec3? _destination;
        private string? _feature;
        private Settlement? _settlement;
        private bool _escortActive;
        private Action<string, Settlement>? _onFeatureUnlocked;

        public event Action<string, Settlement>? OnFeatureUnlocked
        {
            add => _onFeatureUnlocked += value;
            remove => _onFeatureUnlocked -= value;
        }

        public bool IsActive => _escortActive;

        /// <summary>
        /// Start escorting the player to the given destination.
        /// Uses vanilla EscortAgentBehavior with Vec3 target — the position-based Initialize overload.
        /// </summary>
        public void StartEscort(Agent npcAgent, Vec3 destination, string feature, Settlement settlement)
        {
            if (_escortActive)
            {
                LmmiLog.Warning("StartEscort: Already active, ignoring new request.");
                Cancel();
            }

            _npcAgent = npcAgent;
            _destination = destination;
            _feature = feature;
            _settlement = settlement;
            _escortActive = true;

            LmmiLog.Info($"StartEscort: NPC (id={npcAgent.Index}, name={npcAgent.Name}) walking to {destination} for '{feature}'");

            try
            {
                // Remove any existing escort behavior first
                EscortAgentBehavior.RemoveEscortBehaviorOfAgent(_npcAgent);

                // Stop NPC from using any game object
                if (_npcAgent.IsUsingGameObject)
                    _npcAgent.StopUsingGameObject(true);

                // Use vanilla AddPositionEscort helper
                AddPositionEscort(_npcAgent, destination, OnTargetReached);
            }
            catch (Exception ex)
            {
                LmmiLog.Error("StartEscort: Exception during setup", ex);
                Cleanup();
            }
        }

        /// <summary>
        /// Mirrors the vanilla EscortAgentBehavior.AddEscortAgentBehavior pattern
        /// but uses the Vec3? Initialize overload for position-based escorts.
        /// </summary>
        private static void AddPositionEscort(Agent npcAgent, Vec3 targetPosition,
            EscortAgentBehavior.OnTargetReachedDelegate onTargetReached)
        {
            var navigator = npcAgent.GetComponent<CampaignAgentComponent>()?.AgentNavigator;
            var group = navigator?.GetBehaviorGroup<SandBox.Missions.AgentBehaviors.InterruptingBehaviorGroup>();

            if (group == null)
            {
                LmmiLog.Warning($"AddPositionEscort: No InterruptingBehaviorGroup on agent {npcAgent.Name}");
                return;
            }

            bool isNew = group.GetBehavior<EscortAgentBehavior>() == null;
            var escort = group.GetBehavior<EscortAgentBehavior>()
                ?? group.AddBehavior<EscortAgentBehavior>();

            if (isNew)
            {
                group.SetScriptedBehavior<EscortAgentBehavior>();
            }

            // Vec3 Initialize — NPC escorts Agent.Main to the position
            escort.Initialize(Agent.Main, targetPosition, onTargetReached);

            LmmiLog.Info($"AddPositionEscort: Behavior added/initialized. Scripted group={isNew}");
        }

        /// <summary>
        /// Called when the NPC reaches the target position and player is nearby.
        /// </summary>
        private bool OnTargetReached(Agent agent, ref Agent escortedAgent,
            ref Agent targetAgent, ref UsableMachine targetMachine, ref Vec3? targetPosition)
        {
            LmmiLog.Info("OnTargetReached: Player reached destination — unlocking feature.");

            var feature = _feature;
            var settlement = _settlement;

            Cleanup();

            if (settlement != null && !string.IsNullOrEmpty(feature))
            {
                _onFeatureUnlocked?.Invoke(feature, settlement);
            }

            return false; // Don't restart escort
        }

        public void Cancel()
        {
            if (_npcAgent != null)
            {
                try
                {
                    EscortAgentBehavior.RemoveEscortBehaviorOfAgent(_npcAgent);
                    _npcAgent.DisableScriptedMovement();
                }
                catch (Exception ex)
                {
                    LmmiLog.Warning($"Cancel: Exception while cleaning up agent: {ex.Message}");
                }
            }
            Cleanup();
            LmmiLog.Info("Escort cancelled.");
        }

        private void Cleanup()
        {
            _npcAgent = null;
            _destination = null;
            _feature = null;
            _settlement = null;
            _escortActive = false;
        }

        /// <summary>
        /// Find the passage position for a given location ID.
        /// Looks in both TownPassageProps and DisabledPassages.
        /// </summary>
        public static Vec3? FindPassagePosition(string locationId)
        {
            var agentHandler = Mission.Current?.GetMissionBehavior<MissionAgentHandler>();
            if (agentHandler == null) return null;

            Vec3? TryFind(List<UsableMachine> passages)
            {
                if (passages == null) return null;
                foreach (var um in passages)
                {
                    if (um is SandBox.Objects.Usables.Passage p && p.ToLocation?.StringId == locationId)
                    {
                        var frame = p.PilotStandingPoint.GameEntity.GetGlobalFrame();
                        // Step slightly past the passage door in the direction of travel
                        return frame.origin + frame.rotation.f * 1f;
                    }
                }
                return null;
            }

            return TryFind(agentHandler.TownPassageProps)
                ?? TryFind(agentHandler.DisabledPassages);
        }

        /// <summary>
        /// Find a merchant agent in the current mission, optionally excluding one.
        /// </summary>
        public static Agent? FindMerchantAgent(Agent? excludeAgent)
        {
            if (Mission.Current == null) return null;

            foreach (Agent a in Mission.Current.Agents)
            {
                try
                {
                    if (!a.IsActive() || a == excludeAgent || a == Agent.Main) continue;
                    if (a.Character is CharacterObject co &&
                        (co.Occupation == Occupation.Merchant ||
                         co.Occupation == Occupation.GoodsTrader))
                    {
                        return a;
                    }
                }
                catch { continue; }
            }
            return null;
        }

        /// <summary>
        /// Find the nearest agent to a position, optionally excluding certain agents.
        /// Used when we need an Agent target instead of a position target.
        /// </summary>
        public static Agent? FindNearestAgent(Vec3 position, float maxDistance, Agent? excludeAgent)
        {
            if (Mission.Current == null) return null;

            Agent? nearest = null;
            float nearestDistSq = maxDistance * maxDistance;

            foreach (Agent a in Mission.Current.Agents)
            {
                if (!a.IsActive() || a == excludeAgent || a == Agent.Main) continue;
                if (!a.IsHuman) continue;

                float distSq = a.Position.DistanceSquared(position);
                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = a;
                }
            }
            return nearest;
        }
    }
}
