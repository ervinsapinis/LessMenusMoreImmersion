using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LessMenusMoreImmersion.Logging;
using TaleWorlds.Library;

namespace LessMenusMoreImmersion.Behaviors
{
    /// <summary>
    /// Optional bridge to BlocTheWorker's Procedural Scenes (Nexus 14407, module id "ProceduralScenes"): when it is loaded,
    /// the camp is pitched on terrain generated from the exact spot on the campaign map instead of the vanilla battle scene
    /// for the map patch. Everything goes through reflection on its internal generator (no hard reference, no patch on its
    /// code); any failure falls back to the vanilla scene.
    /// Its generator: <c>ProceduralBattleSceneGenerator.TryGenerate(Vec2 campaignDirection, out string sceneName, out string error)</c>
    /// reading the static <c>EncounterCoordinates</c> / <c>EncounterDirection</c>, as its field-battle prefix sets them.
    /// The alternative "taleworlds" backend edits terrain inside a bound mission instead; with it active we stay vanilla.
    /// </summary>
    internal static class ProceduralScenesBridge
    {
        private const string AssemblyName = "ProceduralScenes";

        private static bool _resolved;
        private static MethodInfo? _tryGenerate;
        private static PropertyInfo? _coordinates, _direction;
        private static FieldInfo? _runtimeBackend;

        /// <summary>Procedural Scenes is loaded and its generator has the shape we expect.</summary>
        internal static bool Available
        {
            get { Resolve(); return _tryGenerate != null; }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            try
            {
                var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == AssemblyName);
                if (asm == null) return;
                var gen = asm.GetType("ProceduralScenes.ProceduralBattleSceneGenerator", false);
                if (gen == null) { LmmiLog.Warning("Procedural Scenes: generator type not found; camp uses vanilla scenes."); return; }
                var byRefString = typeof(string).MakeByRefType();
                _tryGenerate = AccessTools.Method(gen, "TryGenerate", new[] { typeof(Vec2), byRefString, byRefString });
                if (_tryGenerate == null || _tryGenerate.ReturnType != typeof(bool) || !_tryGenerate.IsStatic)
                {
                    _tryGenerate = null;
                    LmmiLog.Warning("Procedural Scenes: TryGenerate not found or changed; camp uses vanilla scenes.");
                    return;
                }
                _coordinates = AccessTools.Property(gen, "EncounterCoordinates");
                _direction = AccessTools.Property(gen, "EncounterDirection");
                var tw = asm.GetType("ProceduralScenes.TaleWorldsTerrainGenerator", false);
                if (tw != null) _runtimeBackend = AccessTools.Field(tw, "Enabled");
                LmmiLog.Info($"Procedural Scenes {asm.GetName().Version} detected: the camp will use generated terrain.");
            }
            catch (Exception ex)
            {
                _tryGenerate = null;
                LmmiLog.Error("Procedural Scenes: resolving the generator threw; camp uses vanilla scenes", ex);
            }
        }

        /// <summary>Generates a scene at <paramref name="position"/> (campaign map coordinates). Blocks for a few seconds.</summary>
        internal static bool TryGenerate(Vec2 position, Vec2 facing, out string scene)
        {
            scene = string.Empty;
            if (!Available) return false;
            try
            {
                if (_runtimeBackend?.GetValue(null) is bool runtime && runtime)
                {
                    LmmiLog.Info("Procedural Scenes: its in-mission terrain backend is active; camp uses the vanilla scene.");
                    return false;
                }
                if (facing.LengthSquared < 1e-6f) facing = new Vec2(0f, 1f);
                facing.Normalize();
                _coordinates?.SetValue(null, position);
                _direction?.SetValue(null, facing);

                var args = new object?[] { facing, null, null };
                var started = DateTime.UtcNow;
                bool ok = _tryGenerate!.Invoke(null, args) is bool b && b;
                scene = args[1] as string ?? string.Empty;
                string error = args[2] as string ?? string.Empty;
                double ms = (DateTime.UtcNow - started).TotalMilliseconds;
                if (!ok || string.IsNullOrEmpty(scene))
                {
                    LmmiLog.Warning($"Procedural Scenes: generation failed at ({position.x:F1}, {position.y:F1}) after {ms:F0} ms: {error}");
                    scene = string.Empty;
                    return false;
                }
                LmmiLog.Info($"Procedural Scenes: generated '{scene}' at ({position.x:F1}, {position.y:F1}) in {ms:F0} ms.");
                return true;
            }
            catch (Exception ex)
            {
                LmmiLog.Error("Procedural Scenes: generation threw; camp uses the vanilla scene", ex.InnerException ?? ex);
                scene = string.Empty;
                return false;
            }
        }
    }
}
