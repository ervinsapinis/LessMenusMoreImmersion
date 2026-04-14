using System;
using System.IO;
using System.Text;

namespace LessMenusMoreImmersion.Logging
{
    /// <summary>
    /// Thread-safe file logger for LessMenusMoreImmersion.
    /// Writes to %USERPROFILE%\Documents\Mount and Blade II Bannerlord\Logs\lmmi.log.
    /// - Info / Warning / Error are always written.
    /// - Debug is only written when LmmiSettingsProvider.VerboseLogging is true.
    /// - The file auto-rotates to .old when it grows past 1 MB so it cannot bloat indefinitely.
    /// - All write paths are wrapped in try/catch so the logger cannot crash the game.
    /// </summary>
    public static class LmmiLog
    {
        private static readonly object _lock = new object();
        private static string? _logFilePath;
        private static bool _initialized;
        private static bool _initTried;

        /// <summary>
        /// Delegate set by LmmiSettingsProvider to query whether verbose (Debug) logging is on.
        /// Using a delegate avoids a hard compile-time dependency from the Logging layer on the
        /// Settings layer (and, transitively, on MCM) which keeps this logger completely standalone.
        /// </summary>
        public static Func<bool>? VerboseLoggingQuery { get; set; }

        /// <summary>
        /// Maximum log file size in bytes before rotation kicks in (1 MB).
        /// </summary>
        private const long MaxLogSizeBytes = 1024L * 1024L;

        /// <summary>
        /// Initializes the logger. Safe to call multiple times.
        /// Must be called before any logging; failing to initialize is non-fatal
        /// but subsequent calls will simply be no-ops.
        /// </summary>
        public static void Initialize()
        {
            if (_initTried) return;
            _initTried = true;

            try
            {
                var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(docs)) return;

                var logDir = Path.Combine(docs, "Mount and Blade II Bannerlord", "Logs");
                Directory.CreateDirectory(logDir);

                _logFilePath = Path.Combine(logDir, "lmmi.log");

                // Rotate if too big so we never pull the harmony.log.txt stunt.
                if (File.Exists(_logFilePath))
                {
                    try
                    {
                        var size = new FileInfo(_logFilePath).Length;
                        if (size > MaxLogSizeBytes)
                        {
                            var backup = _logFilePath + ".old";
                            if (File.Exists(backup)) File.Delete(backup);
                            File.Move(_logFilePath, backup);
                        }
                    }
                    catch
                    {
                        // If rotation fails, fall through and keep appending.
                    }
                }

                _initialized = true;
                Info($"=== LessMenusMoreImmersion log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            }
            catch
            {
                // Logging must never crash the game.
                _initialized = false;
            }
        }

        public static void Info(string message) => Write("INFO", message);

        public static void Warning(string message) => Write("WARN", message);

        public static void Error(string message) => Write("ERROR", message);

        public static void Error(string message, Exception? ex)
        {
            if (ex == null) { Write("ERROR", message); return; }
            Write("ERROR", message + Environment.NewLine + ex);
        }

        /// <summary>
        /// Writes a debug line, but only if verbose logging is enabled via the settings delegate.
        /// Cheap when disabled (one delegate invoke + bool check).
        /// </summary>
        public static void Debug(string message)
        {
            if (!IsVerboseEnabled()) return;
            Write("DEBUG", message);
        }

        private static bool IsVerboseEnabled()
        {
            var query = VerboseLoggingQuery;
            if (query == null) return false;
            try { return query(); }
            catch { return false; }
        }

        private static void Write(string level, string message)
        {
            if (!_initialized || _logFilePath == null) return;

            try
            {
                var line = new StringBuilder()
                    .Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ")
                    .Append('[').Append(level).Append("] ")
                    .Append(message)
                    .Append(Environment.NewLine)
                    .ToString();

                lock (_lock)
                {
                    File.AppendAllText(_logFilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Swallow — logger is best-effort.
            }
        }
    }
}
