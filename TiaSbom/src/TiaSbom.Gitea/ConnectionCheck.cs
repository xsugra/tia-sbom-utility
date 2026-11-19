using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TiaSbom.Gitea
{
    public sealed class CheckStep
    {
        public string Name { get; set; }
        public bool Ok { get; set; }
        /// <summary>Result on success, or the user-facing explanation of the failure.</summary>
        public string Detail { get; set; }
        public string Code { get; set; }
    }

    /// <summary>
    /// Checks one link after another, from the configuration to write access in the organization, and stops at the
    /// first broken one. The first failed step names the cause, so the administrator does not have to guess.
    /// Runs in the CLI (tiasbom --check) and needs no TIA Portal.
    /// </summary>
    public static class ConnectionCheck
    {
        public static async Task<List<CheckStep>> RunAsync(Func<GiteaSettings> loadSettings, Func<string> loadToken)
        {
            var steps = new List<CheckStep>();
            GiteaSettings settings = null;
            string giteaUrl = null;

            bool Step(string name, Func<string> action)
            {
                try
                {
                    steps.Add(new CheckStep { Name = name, Ok = true, Detail = action() });
                    return true;
                }
                catch (Exception ex)
                {
                    steps.Add(Failed(name, ex, giteaUrl));
                    return false;
                }
            }

            if (!Step("Configuration", () =>
                {
                    settings = loadSettings();
                    giteaUrl = settings.BaseUrl;
                    return $"{settings.BaseUrl}, organization {settings.Owner}, timeout {settings.TimeoutSeconds} s";
                }))
                return steps;

            string token = null;
            bool hasToken = Step("Token", () =>
            {
                token = !string.IsNullOrEmpty(settings.Token) ? settings.Token : loadToken();
                if (string.IsNullOrEmpty(token))
                    throw new InvalidOperationException("No token saved for this Windows user.");
                return !string.IsNullOrEmpty(settings.Token) ? "company token from the configuration" : "personal token of this Windows user";
            });
            if (!hasToken)
            {
                steps[steps.Count - 1].Code = "TS-AUTH-04";
                steps[steps.Count - 1].Detail = "No token saved for this Windows user. Run 'tiasbom --login' or export once from TIA, the Add-In asks for it.";
            }

            using (var gitea = new GiteaClient(settings, token ?? string.Empty))
            {
                // A wrong token does not matter for /version, so a network problem is told apart from a token problem.
                string version = null;
                bool reachable = await StepAsync(steps, giteaUrl, "Gitea server", async () =>
                {
                    version = await gitea.GetServerVersionAsync().ConfigureAwait(false);
                    return version != null ? "Gitea " + version : "Gitea reachable, version hidden because sign-in is required";
                }).ConfigureAwait(false);
                if (!reachable || !hasToken) return steps;

                // The export needs only organization and repository scopes, so the token is checked with those.
                // /user would need read:user, which the documented token does not have (seen on the V21 VM).
                if (!await StepAsync(steps, giteaUrl, "Token and organization", async () =>
                    {
                        List<string> repositories = await gitea.ListReposAsync().ConfigureAwait(false);
                        return $"token accepted, {settings.Owner}: {repositories.Count} projects";
                    }).ConfigureAwait(false))
                    return steps;

                await StepAsync(steps, giteaUrl, "May create projects", async () =>
                {
                    string login;
                    try { login = await gitea.GetCurrentUserAsync().ConfigureAwait(false); }
                    catch (GiteaException ex) when (ex.IsMissingScope)
                    {
                        return "not checked, the token has no read:user permission (the export does not need it)";
                    }
                    bool? canCreate = await gitea.CanCreateRepositoryAsync(login).ConfigureAwait(false);
                    if (canCreate == false)
                        throw new GiteaException(System.Net.HttpStatusCode.Forbidden, $"{login} may not create repositories in {settings.Owner}.");
                    return canCreate == true ? $"yes ({login})" : $"Gitea did not say, assumed yes ({login})";
                }).ConfigureAwait(false);
            }
            return steps;
        }

        private static async Task<bool> StepAsync(List<CheckStep> steps, string giteaUrl, string name, Func<Task<string>> action)
        {
            try
            {
                steps.Add(new CheckStep { Name = name, Ok = true, Detail = await action().ConfigureAwait(false) });
                return true;
            }
            catch (Exception ex)
            {
                steps.Add(Failed(name, ex, giteaUrl));
                return false;
            }
        }

        // "The SBOM was not saved" belongs to an export, a check saves nothing.
        private static CheckStep Failed(string name, Exception ex, string giteaUrl)
        {
            ExportProblem problem = ExportError.Explain(ex, giteaUrl);
            string summary = problem.Summary.Replace(" The SBOM was not saved.", "");
            return new CheckStep { Name = name, Ok = false, Code = problem.Code, Detail = summary + " " + problem.Action };
        }
    }
}
