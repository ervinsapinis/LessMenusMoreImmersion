using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// Flavor lines: one of several ways of saying the same thing, picked at random — never the same line twice running
    /// from the same pool (a pool is known by its first line). For a dialog line, give it the text "{=!}{LMMI_SOMETHING}"
    /// and set the variable from its condition with <see cref="Say"/>.
    /// </summary>
    internal static class Flavor
    {
        private static readonly Dictionary<string, int> LastPicked = new Dictionary<string, int>();

        public static TextObject Pick(params string[] lines)
        {
            if (lines == null || lines.Length == 0) return TextObject.GetEmpty();
            int i = MBRandom.RandomInt(lines.Length);
            if (lines.Length > 1 && LastPicked.TryGetValue(lines[0], out int last) && last == i)
                i = (i + 1 + MBRandom.RandomInt(lines.Length - 1)) % lines.Length;
            LastPicked[lines[0]] = i;
            return new TextObject(lines[i]);
        }

        /// <summary>Set a text variable to one of the lines; always true, so it can be a dialog line's condition.</summary>
        public static bool Say(string variable, params string[] lines)
        {
            MBTextManager.SetTextVariable(variable, Pick(lines));
            return true;
        }
    }
}
