using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TiaSbom.Gitea
{
    /// <summary>
    /// Talks to the Gitea REST API — no git.exe needed on the engineering PC.
    /// Layout per project key (= repository in the configured organization):
    ///   README.md                         generated summary
    ///   latest.json                       last exported SBOM (input for the vulnerability tracker)
    ///   versions/&lt;project name&gt;/sbom.json  one folder + git tag per TIA project version
    /// </summary>
    public sealed class GiteaClient : IDisposable
    {
        private readonly GiteaSettings _settings;
        private readonly HttpClient _http;

        static GiteaClient()
        {
            // .NET Framework 4.8 may default to old protocols depending on OS / registry.
            // The V17 Add-In sandbox may refuse this; a failure here must not make the whole type unusable.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (System.Security.SecurityException) { }
        }

        public GiteaClient(GiteaSettings settings, string token)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = new HttpClient
            {
                BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/api/v1/"),
                Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            };
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("TiaSbom/0.2");
        }

        /// <summary>
        /// Creates the repository if needed, stores the SBOM (version folder + latest.json + README) and tags the version.
        /// <paramref name="repo"/> and <paramref name="version"/> must already be normalized (see TiaSbom.Core.ProjectKey).
        /// Returns the web URL of the version commit.
        /// </summary>
        public async Task<string> UploadSbomAsync(string repo, string version, string projectName, string json, string readme)
        {
            await EnsureRepoAsync(repo, $"SBOM archive for project {repo}").ConfigureAwait(false);

            string message = $"SBOM {projectName} ({Environment.UserName}@{Environment.MachineName})";
            Commit commit = await PutFileAsync(repo, $"versions/{version}/sbom.json", json, message).ConfigureAwait(false);
            await PutFileAsync(repo, "latest.json", json, message + " [latest]").ConfigureAwait(false);
            await PutFileAsync(repo, "README.md", readme, "Update README").ConfigureAwait(false);
            await TryCreateTagAsync(repo, version, commit.Sha, message).ConfigureAwait(false);
            return commit.Url;
        }

        /// <summary>
        /// Summary of the repository's latest.json, for <see cref="Core.MismatchCheck"/>.
        /// Null when the repository or latest.json does not exist yet (first export).
        /// </summary>
        public async Task<Core.PreviousSbom> GetPreviousAsync(string repo)
        {
            using (var response = await _http.GetAsync(ContentsUri(repo, "latest.json") + "?ref=" + Uri.EscapeDataString(_settings.Branch)).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, "read latest.json in " + repo).ConfigureAwait(false);

                string base64 = Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Get("content") as string;
                if (string.IsNullOrEmpty(base64)) return null;
                var bom = Parse(Encoding.UTF8.GetString(Convert.FromBase64String(base64)));
                if (bom == null) return null;

                var metadata = bom.Get("metadata") as Dictionary<string, object>;
                var project = metadata?.Get("component") as Dictionary<string, object>;
                var previous = new Core.PreviousSbom
                {
                    ProjectName = project?.Get("name") as string,
                    Timestamp = metadata?.Get("timestamp") as string,
                    ExportedBy = Property(project, "tia:exportedBy"),
                };
                foreach (var component in (bom.Get("components") as IEnumerable) ?? new object[0])
                {
                    var c = component as Dictionary<string, object>;
                    string order = Property(c, "siemens:orderNumber");
                    if (order != null) previous.Fingerprints.Add(Core.MismatchCheck.Fingerprint(Property(c, "tia:station"), order));
                }
                return previous;
            }
        }

        private static string Property(Dictionary<string, object> component, string name)
        {
            foreach (var p in (component?.Get("properties") as IEnumerable) ?? new object[0])
                if (p is Dictionary<string, object> prop && prop.Get("name") as string == name)
                    return prop.Get("value") as string;
            return null;
        }

        /// <summary>Repository names in the organization, for the "pick existing project" dialog.</summary>
        public async Task<List<string>> ListReposAsync()
        {
            var names = new List<string>();
            for (int page = 1; ; page++)
            {
                string text = await GetStringAsync($"orgs/{Esc(_settings.Owner)}/repos?limit=50&page={page}").ConfigureAwait(false);
                var items = Serializer().DeserializeObject(text) as IEnumerable;
                int count = 0;
                foreach (var item in items ?? new object[0])
                {
                    if ((item as Dictionary<string, object>)?.Get("name") is string name) names.Add(name);
                    count++;
                }
                if (count < 50) break;
            }
            names.Sort(StringComparer.OrdinalIgnoreCase);
            return names;
        }

        public async Task EnsureRepoAsync(string repo, string description)
        {
            using (var response = await _http.GetAsync($"repos/{Esc(_settings.Owner)}/{Esc(repo)}").ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode) return;
                if (response.StatusCode != HttpStatusCode.NotFound) await ThrowAsync(response, "read repository " + repo).ConfigureAwait(false);
            }

            var body = new Dictionary<string, object>
            {
                ["name"] = repo,
                ["description"] = description,
                ["private"] = _settings.PrivateRepos,
                ["auto_init"] = true,              // creates the default branch so the Contents API works
                ["default_branch"] = _settings.Branch,
            };
            using (var response = await _http.PostAsync($"orgs/{Esc(_settings.Owner)}/repos", JsonContent(body)).ConfigureAwait(false))
            {
                // 409: created concurrently by someone else — fine.
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
                    await ThrowAsync(response, "create repository " + repo).ConfigureAwait(false);
            }
        }

        public async Task<Commit> PutFileAsync(string repo, string repoPath, string content, string message)
        {
            string sha = await GetShaAsync(repo, repoPath).ConfigureAwait(false);
            var body = new Dictionary<string, object>
            {
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                ["message"] = message,
                ["branch"] = _settings.Branch,
            };
            if (sha != null) body["sha"] = sha;

            var request = new HttpRequestMessage(sha == null ? HttpMethod.Post : HttpMethod.Put, ContentsUri(repo, repoPath))
            {
                Content = JsonContent(body),
            };
            using (var response = await _http.SendAsync(request).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, "write " + repoPath).ConfigureAwait(false);
                var commit = Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Get("commit") as Dictionary<string, object>;
                return new Commit { Sha = commit?.Get("sha") as string, Url = commit?.Get("html_url") as string };
            }
        }

        /// <summary>Tags the version commit. An existing tag (same version exported again) is kept as the first release marker.</summary>
        private async Task TryCreateTagAsync(string repo, string tag, string targetSha, string message)
        {
            if (targetSha == null) return;
            var body = new Dictionary<string, object> { ["tag_name"] = tag, ["target"] = targetSha, ["message"] = message };
            using (var response = await _http.PostAsync($"repos/{Esc(_settings.Owner)}/{Esc(repo)}/tags", JsonContent(body)).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict
                    && (int)response.StatusCode != 422)
                    await ThrowAsync(response, "create tag " + tag).ConfigureAwait(false);
            }
        }

        private async Task<string> GetShaAsync(string repo, string repoPath)
        {
            using (var response = await _http.GetAsync(ContentsUri(repo, repoPath) + "?ref=" + Uri.EscapeDataString(_settings.Branch)).ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, "read " + repoPath).ConfigureAwait(false);
                return Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Get("sha") as string;
            }
        }

        private async Task<string> GetStringAsync(string uri)
        {
            using (var response = await _http.GetAsync(uri).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, "GET " + uri).ConfigureAwait(false);
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static async Task ThrowAsync(HttpResponseMessage response, string action)
        {
            string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new GiteaException(response.StatusCode, $"Gitea: cannot {action}: {(int)response.StatusCode} {text}");
        }

        private string ContentsUri(string repo, string repoPath) =>
            $"repos/{Esc(_settings.Owner)}/{Esc(repo)}/contents/{string.Join("/", Array.ConvertAll(repoPath.Split('/'), Uri.EscapeDataString))}";

        private static string Esc(string s) => Uri.EscapeDataString(s);

        private static StringContent JsonContent(object body) =>
            new StringContent(Serializer().Serialize(body), Encoding.UTF8, "application/json");

        // JavaScriptSerializer ships with .NET Framework (GAC) — no version conflicts inside the TIA process.
        internal static JavaScriptSerializer Serializer() => new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private static Dictionary<string, object> Parse(string json) =>
            Serializer().DeserializeObject(json) as Dictionary<string, object>;

        public void Dispose() => _http.Dispose();

        public sealed class Commit
        {
            public string Sha { get; set; }
            public string Url { get; set; }
        }
    }

    internal static class DictionaryExtensions
    {
        public static object Get(this Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object value) ? value : null;
    }

    public sealed class GiteaException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public GiteaException(HttpStatusCode status, string message) : base(message) { StatusCode = status; }
        public bool IsAuthError => StatusCode == HttpStatusCode.Unauthorized || StatusCode == HttpStatusCode.Forbidden;
    }
}
