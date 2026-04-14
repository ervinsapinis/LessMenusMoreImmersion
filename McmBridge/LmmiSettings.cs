using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace LessMenusMoreImmersion.McmBridge
{
    /// <summary>
    /// MCM-backed settings for Less Menus More Immersion.
    ///
    /// This class lives in a SEPARATE assembly so that the main mod DLL has no
    /// metadata reference to MCM. Bannerlord calls <c>Assembly.GetTypes()</c> on
    /// the main DLL at module load, and if this class were in the main DLL, the
    /// CLR would try to resolve <c>AttributeGlobalSettings&lt;LmmiSettings&gt;</c>
    /// — which lives in MCMv5 — and throw <c>ReflectionTypeLoadException</c> on
    /// any machine without MCM installed.
    ///
    /// The main mod reaches this type only through runtime reflection, behind a
    /// runtime MCM-availability check. See <c>LmmiSettingsProvider</c> in the
    /// main assembly.
    /// </summary>
    public class LmmiSettings : AttributeGlobalSettings<LmmiSettings>
    {
        private const string GroupGeneral = "General";
        private const string GroupAutoAccess = "Automatic Access";
        private const string GroupDebug = "Debug";

        public override string Id => "LessMenusMoreImmersion_v1";
        public override string DisplayName => "Less Menus More Immersion";
        public override string FolderName => "LessMenusMoreImmersion";
        public override string FormatType => "json";

        // ---------- General ----------

        [SettingPropertyBool("Enable dynamic discovery",
            HintText = "When enabled, you unlock settlement menus by exploring (entering tavern/arena/keep scenes or talking to merchants/smiths). When disabled, only the local guide and clan tier grant access.")]
        [SettingPropertyGroup(GroupGeneral, GroupOrder = 0)]
        public bool EnableDynamicDiscovery { get; set; } = true;

        [SettingPropertyBool("Show discovery messages",
            HintText = "Display a message in the info panel when you discover a new part of a settlement.")]
        [SettingPropertyGroup(GroupGeneral, GroupOrder = 0)]
        public bool ShowDiscoveryMessages { get; set; } = true;

        [SettingPropertyInteger("Local guide base cost", 0, 5000,
            HintText = "Base cost paid to a local guide to reveal a full town at once. Scales with your clan tier (+1 multiplier per tier).")]
        [SettingPropertyGroup(GroupGeneral, GroupOrder = 0)]
        public int GuideCostBase { get; set; } = 500;

        [SettingPropertyInteger("Village arrangement cost", 0, 2000,
            HintText = "Fixed cost paid to a village trader to establish a trading arrangement and reveal the village.")]
        [SettingPropertyGroup(GroupGeneral, GroupOrder = 0)]
        public int VillageArrangementCost { get; set; } = 200;

        // ---------- Automatic Access ----------

        [SettingPropertyInteger("Full access clan tier", 0, 6,
            HintText = "At this clan tier and above, every settlement is automatically fully unlocked (map room fame). Set to 7 to disable.")]
        [SettingPropertyGroup(GroupAutoAccess, GroupOrder = 1)]
        public int FullAccessClanTier { get; set; } = 5;

        [SettingPropertyInteger("Same-kingdom access clan tier", 0, 6,
            HintText = "At this clan tier and above, settlements owned by clans of your kingdom are automatically fully unlocked. Set to 7 to disable.")]
        [SettingPropertyGroup(GroupAutoAccess, GroupOrder = 1)]
        public int KingdomAccessClanTier { get; set; } = 3;

        // ---------- Debug ----------

        [SettingPropertyBool("Verbose logging",
            HintText = "Enables detailed debug messages in Documents\\Mount and Blade II Bannerlord\\Logs\\lmmi.log. Leave off for normal play.")]
        [SettingPropertyGroup(GroupDebug, GroupOrder = 2)]
        public bool VerboseLogging { get; set; } = false;
    }
}
