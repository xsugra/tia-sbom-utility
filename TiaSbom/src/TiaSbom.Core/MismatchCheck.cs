using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace TiaSbom.Core
{
    /// <summary>One project version the target repository already contains (read from versions/&lt;name&gt;/sbom.json).</summary>
    public sealed class PreviousSbom
    {
        /// <summary>Folder and tag name in the repository.</summary>
        public string VersionName { get; set; }
        public string ProjectName { get; set; }
        public string TiaVersion { get; set; }
        public int ModuleCount { get; set; }
        /// <summary>See <see cref="SbomSignature"/>.</summary>
        public string Signature { get; set; }
        public string ExportedBy { get; set; }
        public string Timestamp { get; set; }
        /// <summary>Time of the Gitea commit. Unlike <see cref="Timestamp"/>, it does not depend on the clock of the exporting PC.</summary>
        public DateTimeOffset? StoredAt { get; set; }
        /// <summary>"station|orderNumber" per hardware module — see <see cref="MismatchCheck.Fingerprints"/>.</summary>
        public HashSet<string> Fingerprints { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Guards against exporting project A into the repository of project B.
    /// A new version of the same machine keeps most stations and modules; a different project does not.
    /// One job can have several versions in use at once, so an export only has to match one of them.
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
        public static List<string> Evaluate(string key, ProjectSnapshot current, IEnumerable<PreviousSbom> versions, IEnumerable<string> repositories)
        {
            var warnings = new List<string>();
            List<PreviousSbom> known = versions.ToList();

            // 1) Job numbers: "Save as" copies Family, so a new job can arrive here with the key of the old one.
            string job = JobNumber(current.ProjectName);
            List<string> keyNumbers = Regex.Matches(key, @"\d{3,}").Cast<Match>().Select(m => m.Value).ToList();
            if (job.Length > 0 && keyNumbers.Count > 0 && !keyNumbers.Contains(job))
                warnings.Add($"Project name starts with job number {job}, but the target project key is {key}.");

            List<string> knownJobs = known.Select(v => JobNumber(v.ProjectName)).Where(j => j.Length > 0).Distinct().ToList();
            if (job.Length > 0 && knownJobs.Count > 0 && !knownJobs.Contains(job))
                warnings.Add($"Project name starts with job number {job}, but '{key}' holds job {string.Join(", ", knownJobs)}.");

            // 2) Family set in TIA but exporting elsewhere (only possible via CLI --key).
            string family = ProjectKey.Normalize(current.Family);
            if (family != null && !string.Equals(family, key, StringComparison.OrdinalIgnoreCase))
                warnings.Add($"TIA project property Family is '{family}', but the target project key is '{key}'.");

            // 3) A new repository whose name is close to an existing one is most likely a typing mistake.
            List<string> names = repositories.ToList();
            if (!names.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                List<string> similar = ProjectKey.Similar(key, names);
                if (similar.Count > 0)
                    warnings.Add($"Gitea project '{key}' does not exist yet, but {string.Join(", ", similar)} does. Is this a typing mistake?");
            }

            // 4) Hardware overlap with the closest version the repository already holds.
            HashSet<string> now = Fingerprints(current);
            PreviousSbom closest = known
                .Where(v => v.Fingerprints.Count > 0)
                .OrderByDescending(v => Overlap(now, v))
                .FirstOrDefault();
            if (closest != null && Overlap(now, closest) < MinOverlap)
            {
                int total = Math.Max(now.Count, closest.Fingerprints.Count);
                warnings.Add(string.Format(CultureInfo.InvariantCulture,
                    "Only {0:P0} of the hardware matches the closest version in '{1}' ({2} of {3} modules).",
                    Overlap(now, closest), key, now.Count(closest.Fingerprints.Contains), total));
                warnings.Add($"Closest version: '{closest.ProjectName}' by {closest.ExportedBy}, {closest.Timestamp}.");
            }
            return warnings;
        }

        private static string JobNumber(string projectName) => Regex.Match(projectName ?? "", @"^\d{3,}").Value;

        private static double Overlap(HashSet<string> now, PreviousSbom version) =>
            (double)now.Count(version.Fingerprints.Contains) / Math.Max(now.Count, version.Fingerprints.Count);
    }
}
