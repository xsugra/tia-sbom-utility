using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using TiaSbom.Core;
using TiaSbom.Gitea;
using Xunit;

namespace TiaSbom.Tests
{
    /// <summary>Runs only against a real Gitea. Never point it at production: it creates and deletes repositories.</summary>
    public sealed class GiteaFactAttribute : FactAttribute
    {
        public GiteaFactAttribute()
        {
            if (string.IsNullOrEmpty(GiteaTarget.Url) || string.IsNullOrEmpty(GiteaTarget.Token))
                Skip = "Set TIASBOM_TEST_GITEA_URL and TIASBOM_TEST_TOKEN to run against a real Gitea.";
        }
    }

    internal static class GiteaTarget
    {
        public static string Url => Environment.GetEnvironmentVariable("TIASBOM_TEST_GITEA_URL");
        public static string Token => Environment.GetEnvironmentVariable("TIASBOM_TEST_TOKEN");
        /// <summary>Optional token with read-only scopes, for the missing-permission message.</summary>
        public static string ReadOnlyToken => Environment.GetEnvironmentVariable("TIASBOM_TEST_READONLY_TOKEN");
        /// <summary>Optional token with exactly the documented scopes: organization and repository Read and Write, nothing else.</summary>
        public static string DocumentedToken => Environment.GetEnvironmentVariable("TIASBOM_TEST_DOC_TOKEN");
        /// <summary>A separate organization, so tests never touch real SBOMs.</summary>
        public static string Owner => Environment.GetEnvironmentVariable("TIASBOM_TEST_OWNER") ?? "sbom-test-org";

        public static GiteaSettings Settings(string owner = null) =>
            new GiteaSettings { BaseUrl = Url, Owner = owner ?? Owner, TimeoutSeconds = 30 };
    }

    /// <summary>Plain API calls the product never makes: archive, delete, list tags.</summary>
    internal sealed class GiteaAdmin : IDisposable
    {
        private readonly HttpClient _http = new HttpClient();

        public GiteaAdmin()
        {
            _http.BaseAddress = new Uri(GiteaTarget.Url.TrimEnd('/') + "/api/v1/");
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", GiteaTarget.Token);
        }

        public async Task EnsureOrgAsync()
        {
            using (var r = await _http.GetAsync("orgs/" + GiteaTarget.Owner))
                if (r.IsSuccessStatusCode) return;
            string body = "{\"username\":\"" + GiteaTarget.Owner + "\",\"visibility\":\"public\"}";
            using (var r = await _http.PostAsync("orgs", new StringContent(body, Encoding.UTF8, "application/json")))
                Assert.True(r.IsSuccessStatusCode, "cannot create test organization: " + await r.Content.ReadAsStringAsync());
        }

        public async Task ArchiveAsync(string repo)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), $"repos/{GiteaTarget.Owner}/{repo}")
            {
                Content = new StringContent("{\"archived\":true}", Encoding.UTF8, "application/json"),
            };
            using (var r = await _http.SendAsync(request)) Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }

        public async Task<List<string>> TagsAsync(string repo)
        {
            string text = await _http.GetStringAsync($"repos/{GiteaTarget.Owner}/{repo}/tags");
            return ((List<object>)Core.Json.Parse(text)).Cast<Dictionary<string, object>>().Select(t => (string)t["name"]).ToList();
        }

        public async Task DeleteAsync(string repo)
        {
            using (await _http.DeleteAsync($"repos/{GiteaTarget.Owner}/{repo}")) { }
        }

        public void Dispose() => _http.Dispose();
    }

    public class GiteaIntegrationTests
    {
        // Letters only: digits in the key would trigger the job-number warning of MismatchCheck.
        private static string NewRepo() => "it-" + new string(Guid.NewGuid().ToString("N").Select(c => (char)('a' + c % 16)).Take(10).ToArray());

        private static async Task<PublishResult> Publish(GiteaClient gitea, string repo, ProjectSnapshot s) =>
            await SbomPublisher.PublishAsync(gitea, repo, s, SbomBuilder.ToJson(SbomBuilder.Build(s)));

        [GiteaFact]
        public async Task Export_versions_tags_and_duplicates()
        {
            string repo = NewRepo();
            using (var admin = new GiteaAdmin())
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
            {
                await admin.EnsureOrgAsync();
                try
                {
                    ProjectSnapshot s = Samples.Line("10001_v1");
                    PublishResult first = await Publish(gitea, repo, s);
                    Assert.False(first.AlreadyStored);
                    Assert.Equal("10001_v1", first.VersionName);
                    Assert.Null(first.TagProblem);
                    Assert.NotNull(first.Url);

                    PublishResult again = await Publish(gitea, repo, Samples.Line("10001_v1"));
                    Assert.True(again.AlreadyStored);

                    ProjectSnapshot changed = Samples.Line("10001_v1");
                    changed.Items[0].FirmwareVersion = "V3.2";
                    PublishResult second = await Publish(gitea, repo, changed);
                    Assert.Equal("10001_v1_2", second.VersionName);

                    List<PreviousSbom> versions = await gitea.GetVersionsAsync(repo);
                    Assert.Equal(2, versions.Count);
                    Assert.Equal(new[] { "10001_v1", "10001_v1_2" }, (await admin.TagsAsync(repo)).OrderBy(t => t));
                    changed.Family = repo; // the sample's Family names another key, the Add-In would export there
                    Assert.Empty(MismatchCheck.Evaluate(repo, changed, versions, new[] { repo }));
                }
                finally { await admin.DeleteAsync(repo); }
            }
        }

        [GiteaFact]
        public async Task Simultaneous_exports_lose_nothing()
        {
            string repo = NewRepo();
            using (var admin = new GiteaAdmin())
            {
                await admin.EnsureOrgAsync();
                try
                {
                    // The repository exists first, as after the first export of a job.
                    using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
                        await Publish(gitea, repo, Samples.Line("base"));

                    var tasks = Enumerable.Range(1, 4).Select(async i =>
                    {
                        using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
                        {
                            ProjectSnapshot s = Samples.Line("parallel");
                            s.Items[0].FirmwareVersion = "V9." + i;
                            try { return (await Publish(gitea, repo, s)).VersionName; }
                            catch (GiteaException ex) { return "ERROR " + ExportError.Explain(ex, GiteaTarget.Url).Code + " " + ex.Message; }
                        }
                    }).ToList();
                    string[] results = await Task.WhenAll(tasks);

                    using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
                    {
                        var stored = (await gitea.GetVersionsAsync(repo)).Select(v => v.VersionName).ToList();
                        foreach (string name in results.Where(r => !r.StartsWith("ERROR")))
                            Assert.Contains(name, stored);
                        // A failed attempt must be reported as a conflict the user can retry, never as something else.
                        foreach (string error in results.Where(r => r.StartsWith("ERROR")))
                            Assert.True(error.StartsWith("ERROR TS-GIT-04"), error);
                        Assert.Equal(results.Length, results.Distinct().Count());
                    }
                }
                finally { await admin.DeleteAsync(repo); }
            }
        }

        [GiteaFact]
        public async Task Archived_project_gives_TS_GIT_03()
        {
            string repo = NewRepo();
            using (var admin = new GiteaAdmin())
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
            {
                await admin.EnsureOrgAsync();
                try
                {
                    await Publish(gitea, repo, Samples.Line("a"));
                    await admin.ArchiveAsync(repo);
                    Exception error = await Record.ExceptionAsync(() => Publish(gitea, repo, Samples.Line("b")));
                    Assert.Equal("TS-GIT-03", ExportError.Explain(error, GiteaTarget.Url).Code);
                }
                finally { await admin.DeleteAsync(repo); }
            }
        }

        [GiteaFact]
        public async Task Missing_organization_gives_TS_GIT_01()
        {
            using (var gitea = new GiteaClient(GiteaTarget.Settings("no-such-org-" + Guid.NewGuid().ToString("N").Substring(0, 6)), GiteaTarget.Token))
            {
                Exception error = await Record.ExceptionAsync(() => gitea.ListReposAsync());
                Assert.Equal("TS-GIT-01", ExportError.Explain(error, GiteaTarget.Url).Code);
            }
        }

        [GiteaFact]
        public async Task Wrong_token_gives_TS_AUTH_01()
        {
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), "0000000000000000000000000000000000000000"))
            {
                var error = await Assert.ThrowsAsync<GiteaException>(() => gitea.ListReposAsync());
                Assert.True(error.IsAuthError);
                Assert.Equal("TS-AUTH-01", ExportError.Explain(error, GiteaTarget.Url).Code);
            }
        }

        [GiteaFact]
        public async Task Read_only_token_gives_TS_AUTH_02()
        {
            if (string.IsNullOrEmpty(GiteaTarget.ReadOnlyToken)) return;
            string repo = NewRepo();
            using (var admin = new GiteaAdmin())
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.ReadOnlyToken))
            {
                await admin.EnsureOrgAsync();
                try
                {
                    Exception error = await Record.ExceptionAsync(() => Publish(gitea, repo, Samples.Line("ro")));
                    Assert.Equal("TS-AUTH-02", ExportError.Explain(error, GiteaTarget.Url).Code);
                }
                finally { await admin.DeleteAsync(repo); }
            }
        }

        [GiteaFact]
        public async Task Connection_check_passes_with_a_good_setup()
        {
            using (var admin = new GiteaAdmin()) await admin.EnsureOrgAsync();
            List<CheckStep> steps = await ConnectionCheck.RunAsync(() => GiteaTarget.Settings(), () => GiteaTarget.Token);
            Assert.True(steps.All(s => s.Ok), string.Join("\n", steps.Select(s => $"{s.Name}: {s.Code} {s.Detail}")));
            Assert.Equal(5, steps.Count);
        }

        [GiteaFact]
        public async Task Connection_check_stops_at_the_token_when_it_is_wrong()
        {
            List<CheckStep> steps = await ConnectionCheck.RunAsync(() => GiteaTarget.Settings(), () => "wrong");
            Assert.Equal(new[] { true, true, true, false }, steps.Select(s => s.Ok));
            Assert.Equal("TS-AUTH-01", steps.Last().Code);
        }

        /// <summary>Found on the V21 VM: a token with exactly the documented scopes failed the check at /user.</summary>
        [GiteaFact]
        public async Task Connection_check_passes_with_the_documented_token_scopes()
        {
            if (string.IsNullOrEmpty(GiteaTarget.DocumentedToken)) return;
            using (var admin = new GiteaAdmin()) await admin.EnsureOrgAsync();
            List<CheckStep> steps = await ConnectionCheck.RunAsync(() => GiteaTarget.Settings(), () => GiteaTarget.DocumentedToken);
            Assert.True(steps.All(s => s.Ok), string.Join("\n", steps.Select(s => $"{s.Name}: {s.Code} {s.Detail}")));
        }

        /// <summary>"By" must be the Gitea account: TIA VMs share the Windows account "Admin".</summary>
        [GiteaFact]
        public async Task Current_user_is_the_gitea_account_of_the_token()
        {
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
                Assert.Equal(await gitea.GetCurrentUserAsync(), await gitea.TryGetCurrentUserAsync());
        }

        /// <summary>Tokens created before 0.2.14 have no read:user. The export must go on with the Windows name.</summary>
        [GiteaFact]
        public async Task Current_user_does_not_stop_the_export_without_read_user()
        {
            if (string.IsNullOrEmpty(GiteaTarget.DocumentedToken)) return;
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.DocumentedToken))
                await gitea.TryGetCurrentUserAsync();
        }

        [GiteaFact]
        public async Task Export_works_with_the_documented_token_scopes()
        {
            if (string.IsNullOrEmpty(GiteaTarget.DocumentedToken)) return;
            string repo = NewRepo();
            using (var admin = new GiteaAdmin())
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.DocumentedToken))
            {
                await admin.EnsureOrgAsync();
                try { Assert.False((await Publish(gitea, repo, Samples.Line("doc"))).AlreadyStored); }
                finally { await admin.DeleteAsync(repo); }
            }
        }
    }

    public class ConnectionCheckOfflineTests
    {
        [Fact]
        public async Task Stops_at_the_network_when_the_server_is_down()
        {
            string url = FakeServer.ClosedPortUrl();
            List<CheckStep> steps = await ConnectionCheck.RunAsync(
                () => new GiteaSettings { BaseUrl = url, Owner = "sbom", TimeoutSeconds = 5 }, () => "token");
            Assert.Equal(new[] { "Configuration", "Token", "Gitea server" }, steps.Select(s => s.Name));
            Assert.Equal("TS-NET-02", steps.Last().Code);
        }

        [Fact]
        public async Task Reports_a_missing_token_but_still_checks_the_server()
        {
            using (FakeServer server = FakeServer.Answer(200, "{\"version\":\"28.2.0\"}"))
            {
                List<CheckStep> steps = await ConnectionCheck.RunAsync(
                    () => new GiteaSettings { BaseUrl = server.Url, Owner = "sbom" }, () => null);
                Assert.Equal("TS-AUTH-04", steps[1].Code);
                Assert.True(steps[2].Ok);
                Assert.Equal(3, steps.Count);
            }
        }

        /// <summary>Found on the production server: REQUIRE_SIGNIN_VIEW=true makes /version answer 403 without a token.</summary>
        [Fact]
        public async Task Server_that_requires_sign_in_counts_as_reachable()
        {
            using (FakeServer server = FakeServer.Answer(403, "{\"message\":\"Only signed in user is allowed to call APIs.\"}"))
            {
                List<CheckStep> steps = await ConnectionCheck.RunAsync(
                    () => new GiteaSettings { BaseUrl = server.Url, Owner = "sbom" }, () => null);
                Assert.True(steps[2].Ok, steps[2].Detail);
                Assert.Contains("sign-in is required", steps[2].Detail);
            }
        }

        [Fact]
        public async Task Html_403_is_not_taken_for_Gitea()
        {
            using (FakeServer server = FakeServer.Answer(403, "<html><body>Forbidden</body></html>", "text/html"))
            {
                List<CheckStep> steps = await ConnectionCheck.RunAsync(
                    () => new GiteaSettings { BaseUrl = server.Url, Owner = "sbom" }, () => null);
                Assert.False(steps[2].Ok);
            }
        }

        [Fact]
        public async Task Missing_configuration_stops_at_once()
        {
            List<CheckStep> steps = await ConnectionCheck.RunAsync(
                () => GiteaSettings.Load(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".json")), () => "t");
            Assert.Single(steps);
            Assert.Equal("TS-CFG-01", steps[0].Code);
        }
    }
}
