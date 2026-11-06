using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaSbom.Core
{
    /// <summary>Reasons not to archive a snapshot at all.</summary>
    public static class ExportGuard
    {
        public static List<string> Problems(ProjectSnapshot snapshot)
        {
            var problems = new List<string>();
            if (snapshot.HasUnsavedChanges)
                problems.Add("The project has unsaved changes. Save it first, so that the SBOM describes the stored project.");
            if (!snapshot.Items.Any(i => i.IsProduct))
                problems.Add("The project contains no hardware modules.");
            return problems;
        }
    }

    /// <summary>
    /// What makes two exports the same content. Time, author, path and serial number differ on every export,
    /// so they are left out. Built from a snapshot and from a stored sbom.json, both must produce the same lines.
    /// </summary>
    public static class SbomSignature
    {
        public static string Tia(string version) => "tia|" + version;

        public static string Software(string name, string version) =>
            "sw|" + name + "|" + (string.IsNullOrWhiteSpace(version) ? SbomBuilder.Unknown : version);

        public static string Hardware(string path, string orderNumber, string firmware) =>
            "hw|" + path + "|" + (orderNumber ?? "") + "|" + (firmware ?? "");

        public static string Join(IEnumerable<string> lines) =>
            string.Join("\n", lines.OrderBy(l => l, StringComparer.Ordinal));

        public static string Of(ProjectSnapshot snapshot)
        {
            var lines = new List<string> { Tia(snapshot.TiaVersion) };
            lines.AddRange(snapshot.UsedProducts.Select(p => Software(p.Name, p.Version)));
            lines.AddRange(snapshot.Items.Where(i => i.IsProduct).Select(i =>
                Hardware(i.Path, i.OrderNumber, SbomBuilder.HasFirmware(i.FirmwareVersion) ? i.FirmwareVersion : null)));
            return Join(lines);
        }
    }

    /// <summary>
    /// Where an export goes. A stored version is the archive record of what was delivered, so it is never replaced:
    /// changed content under the same name becomes name_2, name_3, and unchanged content is not written again.
    /// </summary>
    public sealed class ExportPlan
    {
        private ExportPlan(string versionName, bool alreadyStored)
        {
            VersionName = versionName;
            AlreadyStored = alreadyStored;
        }

        public string VersionName { get; }

        /// <summary>The same content already exists as <see cref="VersionName"/>, nothing has to be written.</summary>
        public bool AlreadyStored { get; }

        public static ExportPlan For(string projectName, string signature, IEnumerable<PreviousSbom> versions)
        {
            string baseName = ProjectKey.VersionName(projectName);
            var numbered = new Regex("^" + Regex.Escape(baseName) + @"_(\d{1,6})$", RegexOptions.IgnoreCase);

            int highest = 0;
            foreach (PreviousSbom version in versions)
            {
                bool isBase = string.Equals(version.VersionName, baseName, StringComparison.OrdinalIgnoreCase);
                Match match = numbered.Match(version.VersionName ?? string.Empty);
                if (!isBase && !match.Success) continue;

                if (version.Signature == signature) return new ExportPlan(version.VersionName, true);
                highest = Math.Max(highest, isBase ? 1 : int.Parse(match.Groups[1].Value));
            }
            return new ExportPlan(highest == 0 ? baseName : baseName + "_" + (highest + 1), false);
        }
    }
}
