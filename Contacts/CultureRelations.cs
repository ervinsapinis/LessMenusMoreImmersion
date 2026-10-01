using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.ModuleManager;

namespace LessMenusMoreImmersion.Contacts
{
    /// <summary>
    /// How strongly people of one culture resent another, read from ModuleData/lmmi_cultures.xml.
    /// The multiplier scales a notable's prejudice: same culture 0, kin 0.5, neutral 1.0, rival 1.5.
    /// Pairs are directional (the notable's culture towards the player's); unlisted pairs are neutral.
    /// </summary>
    internal static class CultureRelations
    {
        private const string FileName = "lmmi_cultures.xml";
        private const float Neutral = 1f;

        private static Dictionary<string, float>? _pairs;

        public static float Multiplier(string? notableCulture, string? playerCulture)
        {
            if (string.IsNullOrEmpty(notableCulture) || string.IsNullOrEmpty(playerCulture)) return Neutral;
            if (string.Equals(notableCulture, playerCulture, StringComparison.OrdinalIgnoreCase)) return 0f;

            var pairs = _pairs ??= Load();
            return pairs.TryGetValue(Key(notableCulture!, playerCulture!), out var value) ? value : Neutral;
        }

        private static string Key(string from, string to) => from.ToLowerInvariant() + ">" + to.ToLowerInvariant();

        private static Dictionary<string, float> Load()
        {
            var pairs = new Dictionary<string, float>();
            try
            {
                var path = Path.Combine(ModuleHelper.GetModuleFullPath("LessMenusMoreImmersion"), "ModuleData", FileName);
                if (!File.Exists(path))
                {
                    LmmiLog.Warning($"CultureRelations: {path} not found — all foreign cultures are neutral.");
                    return pairs;
                }

                var doc = new XmlDocument();
                doc.Load(path);
                foreach (XmlNode node in doc.SelectNodes("/CultureRelations/Pair"))
                {
                    var from = node.Attributes?["from"]?.Value;
                    var to = node.Attributes?["to"]?.Value;
                    var relation = node.Attributes?["relation"]?.Value;
                    if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || string.IsNullOrEmpty(relation)) continue;

                    if (!TryParseRelation(relation!, out var value))
                    {
                        LmmiLog.Warning($"CultureRelations: unknown relation '{relation}' for {from} → {to}.");
                        continue;
                    }

                    pairs[Key(from!, to!)] = value;
                    if (node.Attributes?["mutual"]?.Value == "true")
                        pairs[Key(to!, from!)] = value;
                }

                LmmiLog.Info($"CultureRelations: loaded {pairs.Count} directional culture pairs.");
            }
            catch (Exception ex)
            {
                LmmiLog.Error("CultureRelations: failed to load culture table — all foreign cultures are neutral", ex);
            }
            return pairs;
        }

        private static bool TryParseRelation(string relation, out float value)
        {
            switch (relation.ToLowerInvariant())
            {
                case "kin": value = 0.5f; return true;
                case "neutral": value = 1f; return true;
                case "rival": value = 1.5f; return true;
                default:
                    return float.TryParse(relation, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }
    }
}
