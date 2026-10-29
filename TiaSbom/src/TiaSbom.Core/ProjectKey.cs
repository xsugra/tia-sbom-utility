using System.Text;
using System.Text.RegularExpressions;

namespace TiaSbom.Core
{
    /// <summary>
    /// The stable project key = Gitea repository name. Comes from the TIA project property "Family"
    /// (copied with "Save as", archives and new versions); project names and paths change per version.
    /// </summary>
    public static class ProjectKey
    {
        private const int MaxLength = 100; // Gitea repository name limit

        /// <summary>Gitea repo names allow [A-Za-z0-9_.-]. Everything else becomes '_'. Returns null if nothing usable remains.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            var sb = new StringBuilder();
            foreach (char c in raw.Trim())
                sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') ? c : '_');

            string key = Regex.Replace(sb.ToString(), "_{2,}", "_").Trim('_', '.', '-');
            if (key.Length > MaxLength) key = key.Substring(0, MaxLength);
            return key.Length == 0 ? null : key;
        }

        /// <summary>
        /// Suggestion for the dialog when Family is empty: a leading job number ("24337_20261002_v0_493_V21" -> "24337"),
        /// otherwise the normalized project name. The user always confirms.
        /// </summary>
        public static string Suggest(string projectName)
        {
            Match job = Regex.Match(projectName ?? string.Empty, @"^\d{3,}");
            return job.Success ? job.Value : Normalize(projectName);
        }

        /// <summary>Git tag / folder name for one project version (the TIA project name).</summary>
        public static string VersionName(string projectName)
        {
            string name = Normalize(projectName) ?? "unnamed";
            return name.EndsWith(".lock") ? name + "_" : name; // git refuses refs ending in .lock
        }
    }
}
