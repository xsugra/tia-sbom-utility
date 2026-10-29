using System;
using System.IO;

namespace TiaSbom.Gitea
{
    /// <summary>
    /// Company-wide settings, deployed by IT to %ProgramData%\TiaSbom\config.json (read-only for users).
    /// One Gitea organization holds one repository per project key (TIA project property "Family").
    /// </summary>
    public sealed class GiteaSettings
    {
        public string BaseUrl { get; set; }          // https://gitea.company.local
        public string Owner { get; set; }            // organization, e.g. "sbom"
        public string Branch { get; set; } = "main";
        public bool PrivateRepos { get; set; }       // false: every Gitea user can read every SBOM
        public int TimeoutSeconds { get; set; } = 60;

        /// <summary>
        /// Optional service-account token (write + create repos in <see cref="Owner"/> only).
        /// When empty, the per-user token from <see cref="TokenStore"/> is used.
        /// </summary>
        public string Token { get; set; }

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TiaSbom", "config.json");

        public static GiteaSettings Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path))
                throw new FileNotFoundException("TiaSbom configuration not found. Ask IT to deploy it.", path);

            var settings = GiteaClient.Serializer().Deserialize<GiteaSettings>(File.ReadAllText(path));
            if (string.IsNullOrWhiteSpace(settings?.BaseUrl) || string.IsNullOrWhiteSpace(settings.Owner))
                throw new InvalidDataException($"BaseUrl and Owner must be set in {path}.");
            return settings;
        }
    }
}
