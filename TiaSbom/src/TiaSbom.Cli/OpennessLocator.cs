using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using TiaSbom.Core;

namespace TiaSbom.Cli
{
    /// <summary>
    /// Finds the Openness assemblies of the matching TIA Portal in the registry. Must not reference Siemens.Engineering types.
    /// Key: HKLM\SOFTWARE\Siemens\Automation\Openness\{portal}\PublicAPI\{api}, with \net48 below it from V21.
    /// {portal} is "V16.0" in V16, "17.0" from V17; an installation also lists the older APIs it ships.
    /// </summary>
    internal static class OpennessLocator
    {
        private const string OpennessKey = @"SOFTWARE\Siemens\Automation\Openness";

        public static string FindApiDir(string[] args)
        {
            string overrideDir = Environment.GetEnvironmentVariable("TIASBOM_API_DIR");
            if (!string.IsNullOrEmpty(overrideDir)) return overrideDir;

            int api = Major(TiaBuild.Api).Value;
            Dictionary<int, string> installs = ReadInstalls(api);
            if (installs.Count == 0)
                throw new InvalidOperationException($"No installed TIA Portal provides Openness API {TiaBuild.Api}.");

            int? portal = WantedPortal(args);
            if (portal == null) return installs[installs.Keys.Max()];

            if (installs.TryGetValue(portal.Value, out string dir)) return dir;
            throw new InvalidOperationException(
                $"This tiasbom build uses Openness API {TiaBuild.Api}, but TIA Portal V{portal} with this API was not found. " +
                $"Available here: {string.Join(", ", installs.Keys.OrderBy(k => k).Select(k => "V" + k))}.");
        }

        private static int? WantedPortal(string[] args)
        {
            int? explicitPortal = Major(Value(args, "--portal"));
            if (explicitPortal != null) return explicitPortal;

            // A project opens only in its own TIA version, so the file decides.
            string file = Value(args, "--project") ?? Value(args, "--archive");
            if (file != null) return Major(TiaBuild.VersionFromPath(file));

            List<int> running = RunningPortals();
            if (running.Count > 1)
                throw new InvalidOperationException(
                    $"Several TIA Portal versions are running ({string.Join(", ", running.Select(v => "V" + v))}). Choose one with --portal.");
            return running.Count == 1 ? running[0] : (int?)null;
        }

        private static List<int> RunningPortals()
        {
            var versions = new List<int>();
            foreach (Process process in Process.GetProcessesByName("Siemens.Automation.Portal"))
            {
                try
                {
                    Match m = Regex.Match(process.MainModule.FileName, @"Portal V(\d+)", RegexOptions.IgnoreCase);
                    if (m.Success) versions.Add(int.Parse(m.Groups[1].Value));
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
                {
                    // Process exited or runs with other rights: its version stays unknown.
                }
            }
            return versions.Distinct().ToList();
        }

        /// <summary>TIA major version -> folder with the assemblies of this API.</summary>
        private static Dictionary<int, string> ReadInstalls(int api)
        {
            var installs = new Dictionary<int, string>();
            using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey openness = hklm.OpenSubKey(OpennessKey))
            {
                if (openness == null) return installs;
                foreach (string portalName in openness.GetSubKeyNames())
                {
                    int? portal = Major(portalName);
                    if (portal == null) continue; // "AllowList" and similar
                    using (RegistryKey publicApi = openness.OpenSubKey(portalName + @"\PublicAPI"))
                    {
                        string name = publicApi?.GetSubKeyNames().FirstOrDefault(n => Major(n) == api);
                        if (name == null) continue;
                        using (RegistryKey key = publicApi.OpenSubKey(name))
                        {
                            string dir = AssemblyDir(key);
                            if (dir != null) installs[portal.Value] = dir;
                        }
                    }
                }
            }
            return installs;
        }

        private static string AssemblyDir(RegistryKey api)
        {
            using (RegistryKey net48 = api.OpenSubKey("net48"))
            {
                string dll = (net48 ?? api).GetValue("Siemens.Engineering.Base") as string
                    ?? (net48 ?? api).GetValue("Siemens.Engineering") as string;
                return dll != null && File.Exists(dll) ? Path.GetDirectoryName(dll) : null;
            }
        }

        /// <summary>"V17", "17.0", "V16.0.0.0" -> 17, 17, 16. Null when it is not a version.</summary>
        private static int? Major(string version)
        {
            Match m = Regex.Match(version ?? string.Empty, @"^V?(\d+)", RegexOptions.IgnoreCase);
            return m.Success ? int.Parse(m.Groups[1].Value) : (int?)null;
        }

        private static string Value(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
