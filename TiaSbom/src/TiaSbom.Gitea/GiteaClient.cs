using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace TiaSbom.Gitea
{
    /// <summary>
    /// Talks to the Gitea REST API — no git.exe needed on the engineering PC.
    /// Layout per project key (= repository in the configured organization):
    ///   README.md                         generated overview of all versions
    ///   latest.json                       most recently exported version only
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
        /// Creates the repository if needed, stores the SBOM, latest.json and README in one commit, so a failure leaves
        /// no half-written version, and tags the version.
        /// <paramref name="repo"/> and <paramref name="version"/> must already be normalized (see TiaSbom.Core.ProjectKey).
        /// </summary>
        public async Task<UploadResult> UploadSbomAsync(string repo, string version, string projectName, string json, string readme)
        {
            await EnsureRepoAsync(repo, $"SBOM archive for project {repo}").ConfigureAwait(false);

            string message = $"SBOM {projectName} ({Environment.UserName}@{Environment.MachineName})";
            var body = new Core.JsonObject
            {
                { "branch", _settings.Branch },
                { "message", message },
                { "files", new List<object>
                    {
                        await FileChangeAsync(repo, $"versions/{version}/sbom.json", json).ConfigureAwait(false),
                        await FileChangeAsync(repo, "latest.json", json).ConfigureAwait(false),
                        await FileChangeAsync(repo, "README.md", readme).ConfigureAwait(false),
                    }
                },
            };

            using (var response = await _http.PostAsync($"{RepoUri(repo)}/contents", JsonContent(body)).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, "write version " + version).ConfigureAwait(false);

                var commit = Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Get("commit") as Dictionary<string, object>;
                string sha = commit?.Get("sha") as string;
                return new UploadResult
                {
                    Url = commit?.Get("html_url") as string,
                    TagProblem = await TryCreateTagAsync(repo, version, sha, message).ConfigureAwait(false),
                };
            }
        }

        private async Task<Core.JsonObject> FileChangeAsync(string repo, string repoPath, string content)
        {
            string sha = await GetShaAsync(repo, repoPath).ConfigureAwait(false);
            return new Core.JsonObject
            {
                { "operation", sha == null ? "create" : "update" },
                { "path", repoPath },
                { "content", Convert.ToBase64String(Encoding.UTF8.GetBytes(content)) },
                { "sha", sha },                    // skipped when null (new file)
            };
        }

        /// <summary>
        /// Summary of every version in the repository, for <see cref="Core.MismatchCheck"/>, the plan and the README.
        /// Empty when the repository or the versions folder does not exist yet (first export).
        /// </summary>
        public async Task<List<Core.PreviousSbom>> GetVersionsAsync(string repo)
        {
            var versions = new List<Core.PreviousSbom>();
            string listing = await GetContentsAsync(repo, "versions").ConfigureAwait(false);
            if (listing == null) return versions;

            foreach (var entry in Core.Json.Parse(listing) as List<object> ?? new List<object>())
            {
                var folder = entry as Dictionary<string, object>;
                if (folder?.Get("type") as string != "dir" || !(folder.Get("name") is string name)) continue;

                string text = await GetFileTextAsync(repo, $"versions/{name}/sbom.json").ConfigureAwait(false);
                if (text == null) continue;

                Core.PreviousSbom version = ReadVersion(name, text)
                    ?? throw new InvalidOperationException($"versions/{name}/sbom.json in {repo} is not a valid SBOM.");
                if (DateTimeOffset.TryParse(folder.Get("last_committer_date") as string, out DateTimeOffset storedAt))
                    version.StoredAt = storedAt;
                versions.Add(version);
            }
            return versions;
        }

        /// <summary>The Contents API leaves out the content of large files, those come from the raw endpoint.</summary>
        private async Task<string> GetFileTextAsync(string repo, string repoPath)
        {
            string contents = await GetContentsAsync(repo, repoPath).ConfigureAwait(false);
            if (contents == null) return null;

            if (Parse(contents)?.Get("content") is string base64 && base64.Length > 0)
                return Encoding.UTF8.GetString(Convert.FromBase64String(base64));

            using (var response = await _http.GetAsync($"{RepoUri(repo)}/raw/{EscPath(repoPath)}?ref={Esc(_settings.Branch)}").ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, $"read {repoPath} in {repo}").ConfigureAwait(false);
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        /// <summary>JSON of the Contents API, null when the path does not exist.</summary>
        private async Task<string> GetContentsAsync(string repo, string repoPath)
        {
            using (var response = await _http.GetAsync($"{RepoUri(repo)}/contents/{EscPath(repoPath)}?ref={Esc(_settings.Branch)}").ConfigureAwait(false))
            {
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                if (!response.IsSuccessStatusCode) await ThrowAsync(response, $"read {repoPath} in {repo}").ConfigureAwait(false);
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }

        private static Core.PreviousSbom ReadVersion(string versionName, string sbomJson)
        {
            var bom = Parse(sbomJson);
            if (bom == null) return null;

            var metadata = bom.Get("metadata") as Dictionary<string, object>;
            var project = metadata?.Get("component") as Dictionary<string, object>;
            var version = new Core.PreviousSbom
            {
                VersionName = versionName,
                ProjectName = project?.Get("name") as string,
                TiaVersion = Property(project, "tia:version"),
                Timestamp = metadata?.Get("timestamp") as string,
                ExportedBy = Property(project, "tia:exportedBy"),
            };
            var signature = new List<string> { Core.SbomSignature.Tia(version.TiaVersion) };
            foreach (var component in (bom.Get("components") as IEnumerable) ?? new object[0])
            {
                var c = component as Dictionary<string, object>;
                string type = c?.Get("type") as string;
                if (type == "application")
                {
                    signature.Add(Core.SbomSignature.Software(c.Get("name") as string, c.Get("version") as string));
                }
                else if (type == "device")
                {
                    string order = Property(c, "siemens:orderNumber");
                    version.ModuleCount++;
                    version.Fingerprints.Add(Core.MismatchCheck.Fingerprint(Property(c, "tia:station"), order));
                    signature.Add(Core.SbomSignature.Hardware(Property(c, "tia:path"), order, FirmwareOf(c)));
                }
            }
            version.Signature = Core.SbomSignature.Join(signature);
            return version;
        }

        private static string FirmwareOf(Dictionary<string, object> device)
        {
            foreach (var child in (device.Get("components") as IEnumerable) ?? new object[0])
                if (child is Dictionary<string, object> c && c.Get("type") as string == "firmware")
                    return c.Get("version") as string;
            return null;
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
                var items = Core.Json.Parse(text) as List<object>;
                int count = 0;
                foreach (var item in items ?? new List<object>())
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
            using (var response = await _http.GetAsync(RepoUri(repo)).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode)
                {
                    // A finished job is archived on purpose: a new version needs a human decision, not a failed write.
                    if (Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false))?.Get("archived") is bool archived && archived)
                        throw new InvalidOperationException($"Gitea project '{repo}' is archived and read-only. Ask an administrator to unarchive it.");
                    return;
                }
                if (response.StatusCode != HttpStatusCode.NotFound) await ThrowAsync(response, "read repository " + repo).ConfigureAwait(false);
            }

            var body = new Core.JsonObject
            {
                { "name", repo },
                { "description", description },
                { "private", _settings.PrivateRepos },
                { "auto_init", true },             // creates the default branch so the Contents API works
                { "default_branch", _settings.Branch },
            };
            using (var response = await _http.PostAsync($"orgs/{Esc(_settings.Owner)}/repos", JsonContent(body)).ConfigureAwait(false))
            {
                // 409: created concurrently by someone else — fine.
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
                    await ThrowAsync(response, "create repository " + repo).ConfigureAwait(false);
            }
        }

        /// <summary>Tags the version commit. Returns the problem as text: the SBOM is already stored at this point.</summary>
        private async Task<string> TryCreateTagAsync(string repo, string tag, string targetSha, string message)
        {
            if (targetSha == null) return $"Gitea returned no commit, tag '{tag}' was not created.";

            var body = new Core.JsonObject { { "tag_name", tag }, { "target", targetSha }, { "message", message } };
            using (var response = await _http.PostAsync($"{RepoUri(repo)}/tags", JsonContent(body)).ConfigureAwait(false))
            {
                if (response.IsSuccessStatusCode) return null;
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                return $"Tag '{tag}' was not created: {(int)response.StatusCode} {text}";
            }
        }

        private async Task<string> GetShaAsync(string repo, string repoPath)
        {
            string contents = await GetContentsAsync(repo, repoPath).ConfigureAwait(false);
            return contents == null ? null : Parse(contents)?.Get("sha") as string;
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

        private string RepoUri(string repo) => $"repos/{Esc(_settings.Owner)}/{Esc(repo)}";

        private static string EscPath(string repoPath) =>
            string.Join("/", Array.ConvertAll(repoPath.Split('/'), Uri.EscapeDataString));

        private static string Esc(string s) => Uri.EscapeDataString(s);

        private static StringContent JsonContent(Core.JsonObject body) =>
            new StringContent(Core.Json.Serialize(body), Encoding.UTF8, "application/json");

        private static Dictionary<string, object> Parse(string json) =>
            Core.Json.Parse(json) as Dictionary<string, object>;

        public void Dispose() => _http.Dispose();
    }

    public sealed class UploadResult
    {
        public string Url { get; set; }
        /// <summary>Null when the tag exists. The SBOM is stored either way.</summary>
        public string TagProblem { get; set; }
    }

    internal static class DictionaryExtensions
    {
        public static object Get(this Dictionary<string, object> d, string key) =>
            d.TryGetValue(key, out object value) ? value : null;
    }

    public sealed class GiteaException : Exception
    {
        private const int UnprocessableEntity = 422;

        public HttpStatusCode StatusCode { get; }
        public GiteaException(HttpStatusCode status, string message) : base(message) { StatusCode = status; }

        /// <summary>
        /// 403 alone proves nothing about the token, Gitea also uses it for repository rules. A valid personal token
        /// must not be deleted because of them, so only its own message counts.
        /// </summary>
        public bool IsAuthError =>
            StatusCode == HttpStatusCode.Unauthorized ||
            (StatusCode == HttpStatusCode.Forbidden && Message.IndexOf("token", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>Someone else changed the repository in between, a new attempt with fresh data can succeed.</summary>
        public bool IsConflict => StatusCode == HttpStatusCode.Conflict || (int)StatusCode == UnprocessableEntity;
    }
}
