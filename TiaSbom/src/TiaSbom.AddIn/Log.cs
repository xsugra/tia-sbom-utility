using System;
using System.IO;
using System.Threading;

namespace TiaSbom.AddIn
{
    /// <summary>
    /// Diagnostic log: addin.log in %APPDATA%\TiaSbom, or in %TEMP% when that fails. It is the only trace when
    /// the Add-In host swallows an error or a window never appears. Never write the token here.
    /// </summary>
    internal static class Log
    {
        private const string FileName = "addin.log";

        /// <summary>Where the last entry went, or why it could not be written.</summary>
        public static string Status { get; private set; } = "nothing written yet";

        public static void Write(string message)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            if (TryAppend(AppDataFolder, line)) return;
            TryAppend(Path.GetTempPath, line);
        }

        public static void WriteEnvironment()
        {
            try
            {
                Write($"Start: apartment={Thread.CurrentThread.GetApartmentState()}, " +
                      $"interactive={Environment.UserInteractive}, " +
                      $"fullTrust={AppDomain.CurrentDomain.IsFullyTrusted}");
            }
            catch (Exception ex)
            {
                Write("Start: environment not readable: " + ex.Message);
            }
        }

        private static string AppDataFolder() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiaSbom");

        // Logging must never break the export, so a failure only ends up in Status.
        private static bool TryAppend(Func<string> folder, string line)
        {
            try
            {
                string path = folder();
                Directory.CreateDirectory(path);
                string file = Path.Combine(path, FileName);
                File.AppendAllText(file, line);
                Status = "log: " + file;
                return true;
            }
            catch (Exception ex)
            {
                Status = $"log failed: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }
    }
}
