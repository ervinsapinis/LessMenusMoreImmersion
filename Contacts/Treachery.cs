using LessMenusMoreImmersion.Behaviors;
using LessMenusMoreImmersion.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// Who will cheat you, and whether you can see it coming. Treachery comes from personality
    /// (dishonorable notables) and is never done by a genuine friend. Counterplay: a friend's warning
    /// (arrival scenes) or enough Roguery to smell a swindle — either turns the bait option off, with the reason.
    /// </summary>
    internal static class Treachery
    {
        public const int RogueryToSpot = 60;

        public static bool IsTreacherous(Hero notable, DispositionBreakdown? evaluation) =>
            LmmiSettingsProvider.EnableTreachery
            && notable.GetTraitLevel(DefaultTraits.Honor) <= -1
            && !(evaluation != null && evaluation.Genuine && evaluation.Level >= Disposition.Friendly);

        public static bool Detected(Hero notable, out TextObject reason)
        {
            var friend = ArrivalScenesBehavior.WarnedBy(notable);
            if (friend != null)
            {
                reason = new TextObject("{=lmmi_treachery_warned}{FRIEND} warned you about {NAME}. This smells like a swindle.");
                reason.SetTextVariable("FRIEND", friend.Name);
                reason.SetTextVariable("NAME", notable.Name);
                return true;
            }

            int roguery = Hero.MainHero?.GetSkillValue(DefaultSkills.Roguery) ?? 0;
            if (roguery >= RogueryToSpot)
            {
                reason = new TextObject("{=lmmi_treachery_roguery}You know a swindle when you see one. (Roguery {SKILL})");
                reason.SetTextVariable("SKILL", roguery);
                return true;
            }

            reason = TextObject.GetEmpty();
            return false;
        }
    }
}
