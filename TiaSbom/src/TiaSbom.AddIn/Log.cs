using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace TiaSbom.AddIn
{
    /// <summary>
    /// Diagnostic log in %APPDATA%\TiaSbom\addin.log. It is the only trace when the Add-In host swallows an error
    /// or a window never appears. Never write the token here.
    /// </summary>
    internal static class Log
    {
        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TiaSbom", "addin.log");

        public static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Logging must never break the export.
            }
        }

        public static void WriteEnvironment()
        {
            try
            {
                Write($"Start: process={Process.GetCurrentProcess().ProcessName}, " +
                      $"apartment={Thread.CurrentThread.GetApartmentState()}, " +
                      $"interactive={Environment.UserInteractive}, " +
                      $"fullTrust={AppDomain.CurrentDomain.IsFullyTrusted}");
            }
            catch (Exception ex)
            {
                Write("Start: environment not readable: " + ex.Message);
            }
        }
    }
}
