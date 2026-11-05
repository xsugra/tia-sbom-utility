using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaSbom.Core
{
    /// <summary>What the target repository already contains (read from its latest.json).</summary>
    public sealed class PreviousSbom
    {
        public string ProjectName { get; set; }
        public string ExportedBy { get; set; }
        public string Timestamp { get; set; }
        /// <summary>"station|orderNumber" per hardware module — see <see cref="MismatchCheck.Fingerprints"/>.</summary>
        public HashSet<string> Fingerprints { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Guards against exporting project A into the repository of project B.
    /// A new version of the same machine keeps most stations and modules; a different project does not.
    /// </summary>
    public static class MismatchCheck
    {
        /// <summary>Below this share of common modules the target is considered a different project.</summary>
        public const double MinOverlap = 0.5;

        public static HashSet<string> Fingerprints(ProjectSnapshot s) =>
            new HashSet<string>(
                s.Items.Where(i => i.IsProduct).Select(i => Fingerprint(i.Station, i.OrderNumber)),
                StringComparer.OrdinalIgnoreCase);

        public static string Fingerprint(string station, string orderNumber) => (station ?? "") + "|" + (orderNumber ?? "");

        /// <summary>Returns human-readable warnings; empty list = looks like the same project.</summary>
        public static List<string> Evaluate(string key, ProjectSnapshot current, PreviousSbom previous)
        {
            var warnings = new List<string>();

            // 1) Job numbers: project name starts with a number that differs from a numeric key.
            string job = Regex.Match(current.ProjectName ?? "", @"^\d{3,}").Value;
            if (job.Length > 0 && Regex.IsMatch(key, @"^\d+$") && job != key)
                warnings.Add($"Project name starts with job number {job}, but the target project key is {key}.");

            // 2) Family set in TIA but exporting elsewhere (only possible via CLI --key).
            string family = ProjectKey.Normalize(current.Family);
            if (family != null && !string.Equals(family, key, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"TIA project property Family is '{family}', but the target project key is '{key}'.");

            // 3) Hardware overlap with what the repository already holds.
            if (previous != null && previous.Fingerprints.Count > 0)
            {
                HashSet<string> now = Fingerprints(current);
                int common = now.Count(previous.Fingerprints.Contains);
                double overlap = (double)common / Math.Max(now.Count, previous.Fingerprints.Count);
                if (overlap < MinOverlap)
                {
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "Only {0:P0} of the hardware matches what '{1}' already contains ({2} of {3} modules).",
                        overlap, key, common, Math.Max(now.Count, previous.Fingerprints.Count)));
                    warnings.Add($"Last export there: '{previous.ProjectName}' by {previous.ExportedBy}, {previous.Timestamp}.");
                }
            }
            return warnings;
        }
    }
}
