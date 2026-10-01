using System.Collections.Generic;
using SandBox.View;
using SandBox.View.Missions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// The camp's screen: vanilla's village view set (conversation, escape menu, Tab to leave, the agent HUD), without
    /// the settlement's name markers and barter. Found by the game's view scanner (it looks for [ViewCreatorModule]).
    /// Only [ViewMethod] methods may live in this class.
    /// </summary>
    [ViewCreatorModule]
    public class CampMissionViews
    {
        [ViewMethod(CampBehavior.MissionName)]
        public static MissionView[] OpenCamp(Mission mission)
        {
            return new List<MissionView>
            {
                new MissionCampaignView(),
                new MissionConversationCameraView(),
                SandBoxViewCreator.CreateMissionConversationView(mission),
                ViewCreator.CreateMissionSingleplayerEscapeMenu(CampaignOptions.IsIronmanMode),
                ViewCreator.CreateOptionsUIHandler(),
                ViewCreator.CreateMissionMainAgentEquipDropView(mission),
                new MissionSingleplayerViewHandler(),
                ViewCreator.CreateMissionAgentStatusUIHandler(mission),
                ViewCreator.CreateMissionMainAgentEquipmentController(mission),
                ViewCreator.CreateMissionAgentLockVisualizerView(mission),
                ViewCreator.CreateMissionBoundaryCrossingView(),
                ViewCreator.CreateMissionLeaveView(),
                new MissionBoundaryWallView(),
                new MissionItemContourControllerView(),
                new MissionAgentContourControllerView(),
                new MissionCampaignBattleSpectatorView(),
                ViewCreator.CreatePhotoModeView(),
                ViewCreator.CreateSingleplayerMissionKillNotificationUIHandler(),
            }.ToArray();
        }
    }
}
