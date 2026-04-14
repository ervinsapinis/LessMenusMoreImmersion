namespace LessMenusMoreImmersion.McmBridge
{
    /// <summary>
    /// Static reflection surface that the main mod calls into via
    /// <c>Assembly.LoadFrom</c> + <c>MethodInfo.Invoke</c>. All getters are
    /// trivial forwards to <see cref="LmmiSettings.Instance"/>.
    ///
    /// The main mod never holds a reference to this type or to
    /// <see cref="LmmiSettings"/>; it looks up methods by name at runtime so
    /// that the main DLL's metadata never needs this assembly to resolve.
    /// </summary>
    public static class LmmiMcmBridge
    {
        // ---- Flags ----
        public static bool GetEnableDynamicDiscovery() => SafeBool(() => LmmiSettings.Instance?.EnableDynamicDiscovery ?? true, true);
        public static bool GetShowDiscoveryMessages()  => SafeBool(() => LmmiSettings.Instance?.ShowDiscoveryMessages  ?? true, true);
        public static bool GetVerboseLogging()         => SafeBool(() => LmmiSettings.Instance?.VerboseLogging         ?? false, false);

        // ---- Integers ----
        public static int GetGuideCostBase()          => SafeInt(() => LmmiSettings.Instance?.GuideCostBase          ?? 500, 500);
        public static int GetVillageArrangementCost() => SafeInt(() => LmmiSettings.Instance?.VillageArrangementCost ?? 200, 200);
        public static int GetFullAccessClanTier()     => SafeInt(() => LmmiSettings.Instance?.FullAccessClanTier     ?? 5,   5);
        public static int GetKingdomAccessClanTier()  => SafeInt(() => LmmiSettings.Instance?.KingdomAccessClanTier  ?? 3,   3);

        /// <summary>
        /// Touches <see cref="LmmiSettings.Instance"/> so MCM registers the
        /// settings screen as early as possible.
        /// </summary>
        public static void ForceInitialize()
        {
            try { var _ = LmmiSettings.Instance; }
            catch { /* MCM may not be ready yet — caller logs. */ }
        }

        // ---- Helpers ----
        private static bool SafeBool(System.Func<bool> f, bool fallback)
        {
            try { return f(); } catch { return fallback; }
        }
        private static int SafeInt(System.Func<int> f, int fallback)
        {
            try { return f(); } catch { return fallback; }
        }
    }
}
