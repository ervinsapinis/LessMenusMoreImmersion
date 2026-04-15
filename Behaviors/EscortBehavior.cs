using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using SandBox;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using SandBox.Missions.AgentBehaviors;
using SandBox.Objects.AreaMarkers;
using SandBox.Conversation.MissionLogics;
using SandBox.Missions.MissionLogics;
using LessMenusMoreImmersion.Constants;
using LessMenusMoreImmersion.Logging;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Manages NPC escort behavior using the vanilla EscortAgentBehavior.
    /// NPCs walk to destinations and wait for player. Feature unlocks when player reaches destination
    /// and confirms via arrival dialog.
    /// </summary>
    public class EscortBehavior
    {
        private Agent? _npcAgent;
        private Vec3? _destination;
        private string? _feature;
        private Settlement? _settlement;
        private bool _escortActive;
        private bool _escortArrived;
        private Action<string, Settlement>? _onFeatureUnlocked;

        public event Action<string, Settlement>? OnFeatureUnlocked
        {
            add => _onFeatureUnlocked += value;
            remove => _onFeatureUnlocked -= value;
        }

        /// <summary>Is an escort currently active (NPC is walking to destination)?</summary>
        public bool IsActive => _escortActive;

        /// <summary>Has the escort reached the destination (arrival dialog should show)?</summary>
        public bool IsArrived => _escortArrived;

        /// <summary>The NPC agent performing the escort.</summary>
        public Agent? NpcAgent => _npcAgent;

        /// <summary>The feature being escorted to (e.g. "backstreet", "trade").</summary>
        public string? TargetFeature => _feature;

        /// <summary>
        /// Start escorting the player to the given destination.
        /// Uses vanilla EscortAgentBehavior with Vec3 target — the position-based Initialize overload.
        /// </summary>
        public void StartEscort(Agent npcAgent, Vec3 destination, string feature, Settlement settlement)
        {
            if (_escortActive)
            {
                LmmiLog.Warning("StartEscort: Already active, cancelling previous.");
                Cancel();
            }

            _npcAgent = npcAgent;
            _destination = destination;
            _feature = feature;
            _settlement = settlement;
            _escortActive = true;
            _escortArrived = false;

            LmmiLog.Info($"StartEscort: NPC (id={npcAgent.Index}, name={npcAgent.Name}) walking to {destination} for '{feature}'");

            try
            {
                EscortAgentBehavior.RemoveEscortBehaviorOfAgent(_npcAgent);

                if (_npcAgent.IsUsingGameObject)
                    _npcAgent.StopUsingGameObject(true);

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

            LmmiLog.Info($"AddPositionEscort: Behavior added/initialized. isNew={isNew}");
        }

        /// <summary>
        /// Called by EscortAgentBehavior when NPC and player are both within range of destination.
        /// Auto-triggers a conversation so the "Here we are!" arrival dialog can fire.
        /// </summary>
        private bool OnTargetReached(Agent agent, ref Agent escortedAgent,
            ref Agent targetAgent, ref UsableMachine targetMachine, ref Vec3? targetPosition)
        {
            LmmiLog.Info("OnTargetReached: NPC and player reached destination.");

            if (_npcAgent == null || !_npcAgent.IsActive())
            {
                LmmiLog.Warning("OnTargetReached: NPC agent null/inactive — unlocking feature directly.");
                var f = _feature;
                var s = _settlement;
                Cleanup();
                if (s != null && !string.IsNullOrEmpty(f))
                    _onFeatureUnlocked?.Invoke(f, s);
                return false;
            }

            // Mark as arrived — our dialog condition checks this
            _escortArrived = true;

            // Auto-start conversation so arrival dialog fires (same pattern as vanilla hero escort)
            try
            {
                var conversationLogic = MissionConversationLogic.Current;
                if (conversationLogic != null && !Campaign.Current.ConversationManager.IsConversationFlowActive)
                {
                    LmmiLog.Info("OnTargetReached: Starting arrival conversation.");
                    conversationLogic.StartConversation(_npcAgent, false, false);
                }
                else
                {
                    LmmiLog.Warning("OnTargetReached: Cannot start conversation — unlocking directly.");
                    var f = _feature;
                    var s = _settlement;
                    Cleanup();
                    if (s != null && !string.IsNullOrEmpty(f))
                        _onFeatureUnlocked?.Invoke(f, s);
                }
            }
            catch (Exception ex)
            {
                LmmiLog.Error("OnTargetReached: Exception starting conversation", ex);
                var f = _feature;
                var s = _settlement;
                Cleanup();
                if (s != null && !string.IsNullOrEmpty(f))
                    _onFeatureUnlocked?.Invoke(f, s);
            }

            return false; // Don't restart escort
        }

        /// <summary>
        /// Called from the "Here we are!" arrival dialog consequence.
        /// Unlocks the feature and cleans up the escort state.
        /// </summary>
        public void CompleteEscortArrival()
        {
            var feature = _feature;
            var settlement = _settlement;

            LmmiLog.Info($"CompleteEscortArrival: Unlocking '{feature}' in {settlement?.Name}.");

            Cleanup();

            if (settlement != null && !string.IsNullOrEmpty(feature))
            {
                _onFeatureUnlocked?.Invoke(feature, settlement);
            }
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
            _escortArrived = false;
        }

        /// <summary>
        /// Find the passage position for a given location ID.
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
                        return frame.origin + frame.rotation.f * 1f;
                    }
                }
                return null;
            }

            bool isCommonAreaDestination = locationId.StartsWith("alley", StringComparison.OrdinalIgnoreCase)
                || string.Equals(locationId, "waterfront", StringComparison.OrdinalIgnoreCase)
                || string.Equals(locationId, "clearing", StringComparison.OrdinalIgnoreCase);

            // PASS 1: For common area destinations, try finding the area marker first.
            // This avoids escorting to overlapping passage doors (e.g., tavern entrance near an alley).
            if (isCommonAreaDestination)
            {
                var commonAreas = Mission.Current.ActiveMissionObjects.FindAllWithType<SandBox.Objects.AreaMarkers.CommonAreaMarker>();
                foreach (var area in commonAreas)
                {
                    // CommonAreaMarker.Tag returns the Settlement.Alley.Tag (e.g. alley_1, waterfront, clearing)
                    var tag = area.Tag;
                    if (!string.IsNullOrEmpty(tag) && string.Equals(tag, locationId, StringComparison.OrdinalIgnoreCase))
                    {
                        return area.GameEntity.GlobalPosition;
                    }
                }

                var commonAreasList = commonAreas as IList<CommonAreaMarker> ?? commonAreas.ToList();
                if (commonAreasList.Count > 0)
                {
                    var summary = string.Join(", ", commonAreasList.Select(area => $"{area.Tag ?? "<null>"}/{area.GameEntity.Name}"));
                    LmmiLog.Warning($"FindPassagePosition: No CommonAreaMarker matched '{locationId}'. Available markers: {summary}");
                }
                else
                {
                    LmmiLog.Warning($"FindPassagePosition: No CommonAreaMarkers found for '{locationId}'.");
                }

                // Don't fall through to passage search — the passage door may overlap with the tavern entrance,
                // causing the player to accidentally enter the tavern and discover backstreet.
                return null;
            }

            // PASS 2: Try finding a passage (works for tavern, arena, smithy, keep, prison).
            var pos = TryFind(agentHandler.TownPassageProps) ?? TryFind(agentHandler.DisabledPassages);
            if (pos.HasValue) return pos;

            return null;
        }

        /// <summary>
        /// Find a shop worker (trader stall NPC) in the current mission.
        /// Avoids Notable heroes — we want the actual shop NPCs at market stalls.
        /// </summary>
        public static Agent? FindMerchantAgent(Agent? excludeAgent)
        {
            if (Mission.Current == null) return null;

            foreach (Agent a in Mission.Current.Agents)
            {
                try
                {
                    if (!a.IsActive() || a == excludeAgent || a == Agent.Main) continue;
                    if (a.IsHero) continue; // skip Notable heroes, we want shop stall workers
                    if (a.Character is CharacterObject co &&
                        (co.Occupation == Occupation.ShopWorker ||
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
        /// Find the barber agent in the current mission using the settlement's Culture.Barber.
        /// Used as destination for the barber escort.
        /// </summary>
        public static Agent? FindBarberAgent(Agent? excludeAgent)
        {
            if (Mission.Current == null) return null;

            var settlement = Settlement.CurrentSettlement;
            var barberChar = settlement?.Culture?.Barber;
            if (barberChar == null) return null;

            foreach (Agent a in Mission.Current.Agents)
            {
                try
                {
                    if (!a.IsActive() || a == excludeAgent || a == Agent.Main) continue;
                    if (a.Character is CharacterObject co && co == barberChar)
                    {
                        return a;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Find a blacksmith/weaponsmith agent in the current mission.
        /// Used as destination for the smithy escort.
        /// </summary>
        public static Agent? FindBlacksmithAgent(Agent? excludeAgent)
        {
            if (Mission.Current == null) return null;

            // PASS 1: Strict search for Occupation.Blacksmith
            foreach (Agent a in Mission.Current.Agents)
            {
                try
                {
                    if (!a.IsActive() || a == excludeAgent || a == Agent.Main) continue;

                    if (a.Character is CharacterObject co &&
                        co.Occupation == Occupation.Blacksmith)
                    {
                        return a;
                    }
                }
                catch
                {
                    continue;
                }
            }

            // PASS 2: Fallback to broader logic
            foreach (Agent a in Mission.Current.Agents)
            {
                try
                {
                    if (!a.IsActive() || a == excludeAgent || a == Agent.Main) continue;

                    if (a.Character is CharacterObject co &&
                        (co.Occupation == Occupation.Weaponsmith ||
                         co.Occupation == Occupation.Armorer ||
                         co.StringId.Contains("blacksmith") ||
                         co.StringId.Contains("smith")))
                    {
                        return a;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return null;
        }

        /// <summary>
        /// Find the nearest agent to a position.
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