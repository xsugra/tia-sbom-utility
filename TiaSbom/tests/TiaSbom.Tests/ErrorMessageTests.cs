using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TiaSbom.Gitea;
using Xunit;

namespace TiaSbom.Tests
{
    /// <summary>
    /// Every failure an ordinary user can meet must end in a message with a code and a next step.
    /// The codes are the contract with docs/11-chybove-hlasky.md.
    /// </summary>
    public class ErrorMessageTests
    {
        private static GiteaSettings Settings(string url, int timeout = 5) =>
            new GiteaSettings { BaseUrl = url, Owner = "sbom", TimeoutSeconds = timeout };

        private static async Task<ExportProblem> ProblemOf(string url, Func<GiteaClient, Task> call, int timeout = 5)
        {
            using (var gitea = new GiteaClient(Settings(url, timeout), "not-a-real-token"))
            {
                Exception error = await Record.ExceptionAsync(() => call(gitea));
                Assert.NotNull(error);
                ExportProblem problem = ExportError.Explain(error, url);
                Assert.False(string.IsNullOrWhiteSpace(problem.Action));
                return problem;
            }
        }

        [Fact]
        public async Task Server_not_running_is_TS_NET_02()
        {
            string url = FakeServer.ClosedPortUrl();
            ExportProblem problem = await ProblemOf(url, g => g.ListReposAsync());
            Assert.Equal("TS-NET-02", problem.Code);
            Assert.Contains(url, problem.Summary);
        }

        [Fact]
        public async Task Unknown_host_name_is_TS_NET_01()
        {
            ExportProblem problem = await ProblemOf("http://gitea-does-not-exist.invalid", g => g.ListReposAsync());
            Assert.Equal("TS-NET-01", problem.Code);
        }

        [Fact]
        public async Task Server_that_never_answers_is_TS_NET_04()
        {
            using (FakeServer server = FakeServer.Hang())
            {
                ExportProblem problem = await ProblemOf(server.Url, g => g.ListReposAsync(), timeout: 1);
                Assert.Equal("TS-NET-04", problem.Code);
            }
        }

        [Fact]
        public async Task Html_instead_of_api_is_TS_API_01_not_a_damaged_configuration()
        {
            using (FakeServer server = FakeServer.Answer(200, "<html><body>Welcome to IIS</body></html>", "text/html"))
            {
                ExportProblem problem = await ProblemOf(server.Url, g => g.ListReposAsync());
                Assert.Equal("TS-API-01", problem.Code);
            }
        }

        [Theory]
        [InlineData(401, "{\"message\":\"token is required\"}", "TS-AUTH-01")]
        [InlineData(403, "{\"message\":\"token does not have at least one of required scope(s): [read:organization]\"}", "TS-AUTH-02")]
        [InlineData(403, "{\"message\":\"user does not have permission\"}", "TS-GIT-02")]
        [InlineData(404, "{\"message\":\"GetOrgByName\"}", "TS-GIT-01")]
        [InlineData(500, "{\"message\":\"database is locked\"}", "TS-GIT-05")]
        [InlineData(502, "Bad Gateway", "TS-GIT-05")]
        public async Task Gitea_status_codes_on_listing(int status, string body, string code)
        {
            using (FakeServer server = FakeServer.Answer(status, body))
            {
                ExportProblem problem = await ProblemOf(server.Url, g => g.ListReposAsync());
                Assert.Equal(code, problem.Code);
            }
        }

        [Fact]
        public async Task Missing_scope_also_counts_as_token_problem_so_the_Add_In_asks_again()
        {
            using (FakeServer server = FakeServer.Answer(403, "{\"message\":\"token does not have at least one of required scope(s): [write:repository]\"}"))
            using (var gitea = new GiteaClient(Settings(server.Url), "x"))
            {
                var error = await Assert.ThrowsAsync<GiteaException>(() => gitea.ListReposAsync());
                Assert.True(error.IsAuthError);
                Assert.True(error.IsMissingScope);
            }
        }

        [Fact]
        public async Task Archived_repository_is_TS_GIT_03()
        {
            using (FakeServer server = FakeServer.Answer(200, "{\"name\":\"10001\",\"archived\":true}"))
            {
                ExportProblem problem = await ProblemOf(server.Url, g => g.EnsureRepoAsync("10001", "x"));
                Assert.Equal("TS-GIT-03", problem.Code);
            }
        }

        [Fact]
        public void Missing_configuration_is_TS_CFG_01()
        {
            Exception error = Record.Exception(() => GiteaSettings.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
            Assert.Equal("TS-CFG-01", ExportError.Explain(error, null).Code);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[1,2]")]
        [InlineData("{\"Owner\":\"sbom\"}")]
        [InlineData("{\"BaseUrl\":\"gitea:3000\",\"Owner\":\"sbom\"}")]
        [InlineData("{\"BaseUrl\":\"http://gitea\",\"Owner\":\"sbom\",\"TimeoutSeconds\":0}")]
        public void Damaged_configuration_is_TS_CFG_02(string content)
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            File.WriteAllText(path, content);
            try
            {
                Exception error = Record.Exception(() => GiteaSettings.Load(path));
                Assert.NotNull(error);
                Assert.Equal("TS-CFG-02", ExportError.Explain(error, null).Code);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Every_message_names_the_code_and_says_what_to_do()
        {
            string text = ExportError.Describe(new System.Net.Http.HttpRequestException("x"), "http://gitea:3000", @"C:\log\addin.log");
            Assert.Contains("Error code: TS-NET-03", text);
            Assert.Contains("http://gitea:3000", text);
            Assert.Contains(@"C:\log\addin.log", text);
        }

        [Fact]
        public void Wrapped_failure_from_a_synchronous_wait_is_unwrapped()
        {
            var error = new AggregateException(new TaskCanceledException());
            Assert.Equal("TS-NET-04", ExportError.Explain(error, "http://x").Code);
        }

        [Fact]
        public void Certificate_failure_is_TS_TLS_01()
        {
            var error = new System.Net.Http.HttpRequestException("ssl",
                new System.Security.Authentication.AuthenticationException("remote certificate is invalid"));
            Assert.Equal("TS-TLS-01", ExportError.Explain(error, "https://gitea").Code);
        }

        [Fact]
        public void Catalog_lists_every_code_the_code_can_produce()
        {
            string docs = Repo.File_("..", "docs", "11-chybove-hlasky.md");
            Assert.True(docs != null && File.Exists(docs), "docs/11-chybove-hlasky.md is missing");
            string catalog = File.ReadAllText(docs);
            string source = File.ReadAllText(Repo.File_("src", "TiaSbom.Gitea", "ExportError.cs"));
            var codes = System.Text.RegularExpressions.Regex.Matches(source, "\"(TS-[A-Z]+-\\d\\d)\"").Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value).Distinct();
            foreach (string code in codes) Assert.Contains(code, catalog);
        }
    }
}
