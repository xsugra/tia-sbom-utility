using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private const int MaxTypoDistance = 2;

        /// <summary>Gitea repo names allow [A-Za-z0-9_.-]. Everything else becomes '_'. Returns null if nothing usable remains.</summary>
        public static string Normalize(string raw)
        {
            string key = Clean(raw);
            return key != null && key.Length > MaxLength ? key.Substring(0, MaxLength) : key;
        }

        private static string Clean(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Accents are dropped, not replaced: "Čiara" and "Šiara" must not both end up as "iara".
            var sb = new StringBuilder();
            foreach (char c in raw.Trim().Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(c < 128 && (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') ? c : '_');
            }

            // ".." is valid in a repository path, but not in a git tag.
            string key = Regex.Replace(Regex.Replace(sb.ToString(), "_{2,}", "_"), @"\.{2,}", ".").Trim('_', '.', '-');
            return key.Length == 0 ? null : key;
        }

        /// <summary>
        /// Existing repositories that look like a typing mistake of <paramref name="key"/>. Job numbers are skipped:
        /// neighbouring numbers are different jobs, not typos.
        /// </summary>
        public static List<string> Similar(string key, IEnumerable<string> repositories)
        {
            if (Regex.IsMatch(key, @"^\d+$")) return new List<string>();
            return repositories.Where(r => Distance(key.ToLowerInvariant(), r.ToLowerInvariant()) <= MaxTypoDistance).ToList();
        }

        private static int Distance(string a, string b)
        {
            int[] previous = Enumerable.Range(0, b.Length + 1).ToArray();
            for (int i = 1; i <= a.Length; i++)
            {
                int[] current = new int[b.Length + 1];
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int replace = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(replace, Math.Min(previous[j], current[j - 1]) + 1);
                }
                previous = current;
            }
            return previous[b.Length];
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
            string name = Clean(projectName) ?? "unnamed";
            if (name.Length > MaxLength)
            {
                // Long names often differ only at the end, a plain cut would merge them.
                string suffix = "_" + Hash(name);
                name = name.Substring(0, MaxLength - suffix.Length).TrimEnd('_', '.', '-') + suffix;
            }
            return name.EndsWith(".lock") ? name + "_" : name; // git refuses refs ending in .lock
        }

        // FNV-1a. It only has to tell names apart, and needs no cryptography API in the sandbox.
        private static string Hash(string text)
        {
            uint hash = 2166136261;
            foreach (char c in text) hash = unchecked((hash ^ c) * 16777619);
            return hash.ToString("x8", CultureInfo.InvariantCulture);
        }
    }
}
