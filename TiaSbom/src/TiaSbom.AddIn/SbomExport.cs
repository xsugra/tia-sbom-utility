using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Siemens.Engineering;
using TiaSbom.Core;
using TiaSbom.Gitea;
using TiaSbom.Tia;

namespace TiaSbom.AddIn
{
    /// <summary>
    /// The one-click flow: collect -> resolve project key (Family) -> build CycloneDX -> commit to Gitea -> show result.
    /// </summary>
    internal sealed class SbomExport
    {
        private const string Title = "SBOM Export";
        private readonly TiaPortal _tiaPortal;

        public SbomExport(TiaPortal tiaPortal)
        {
            _tiaPortal = tiaPortal;
        }

        public void Run()
        {
            Log.WriteEnvironment();
            bool tokenFromConfig = false;
            string giteaUrl = null;
            try
            {
                GiteaSettings settings = GiteaSettings.Load();
                giteaUrl = settings.BaseUrl;
                Log.Write($"Config loaded: {settings.BaseUrl}, owner {settings.Owner}");

                string token = settings.Token;
                tokenFromConfig = !string.IsNullOrEmpty(token);
                if (!tokenFromConfig && !TokenStore.TryLoad(out token))
                {
                    Log.Write("No saved token, asking the user.");
                    token = Ui.Run(TokenDialog.Ask);
                    if (string.IsNullOrEmpty(token))
                    {
                        Log.Write("Stopped: no token entered.");
                        return;
                    }
                    TokenStore.Save(token);
                }
                Log.Write(tokenFromConfig ? "Token: from configuration." : "Token: per-user.");

                // Read the project under TIA's progress dialog. With TIA.ReadOnly, ExclusiveAccess.Text is not writable,
                // so each phase opens its own ExclusiveAccess with a fixed text.
                ProjectSnapshot snapshot;
                using (_tiaPortal.ExclusiveAccess("Reading hardware configuration…"))
                {
                    var project = HardwareCollector.FindOpenProject(_tiaPortal)
                        ?? throw new InvalidOperationException("No project is open.");
                    snapshot = HardwareCollector.Collect(project);
                }
                Log.Write($"Project read: {snapshot.ProjectName}, Family '{snapshot.Family}'");

                List<string> problems = ExportGuard.Problems(snapshot);
                if (problems.Count > 0)
                {
                    Log.Write("Stopped: " + string.Join(" ", problems));
                    Ui.Message(string.Join("\n\n", problems), Title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                using (var gitea = new GiteaClient(settings, token))
                {
                    string key = ProjectKey.Normalize(snapshot.Family);
                    bool askedForKey = key == null;
                    List<string> repositories = gitea.ListReposAsync().GetAwaiter().GetResult();
                    Log.Write($"Repositories listed: {repositories.Count}");
                    string login = gitea.TryGetCurrentUserAsync().GetAwaiter().GetResult();
                    if (login != null) snapshot.ExportedBy = login;
                    Log.Write(login != null
                        ? $"Exported by Gitea account '{login}'"
                        : $"Token has no read:user permission, exported by Windows user '{snapshot.ExportedBy}'");
                    if (askedForKey)
                    {
                        key = Ui.Run(owner => RepoPickerDialog.Ask(owner, snapshot.ProjectName,
                            ProjectKey.Suggest(snapshot.ProjectName), repositories));
                        if (key == null)
                        {
                            Log.Write("Stopped: no project key chosen.");
                            return;
                        }
                    }

                    // Guard against exporting into another project's repository.
                    List<PreviousSbom> versions = gitea.GetVersionsAsync(key).GetAwaiter().GetResult();
                    Log.Write($"Versions in '{key}': {versions.Count}");
                    List<string> warnings = MismatchCheck.Evaluate(key, snapshot, versions, repositories);
                    if (warnings.Count > 0 &&
                        Ui.Message(
                            $"This SBOM may belong to a different project than '{key}':\n\n• " + string.Join("\n• ", warnings) +
                            $"\n\nExport '{snapshot.ProjectName}' to '{key}' anyway?",
                            Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        Log.Write("Stopped: mismatch warning not confirmed.");
                        return;
                    }

                    string json = SbomBuilder.ToJson(SbomBuilder.Build(snapshot));
                    PublishResult result;
                    using (_tiaPortal.ExclusiveAccess("Uploading SBOM to Gitea…"))
                    {
                        result = SbomPublisher.PublishAsync(gitea, key, snapshot, json).GetAwaiter().GetResult();
                    }
                    if (result.AlreadyStored)
                    {
                        Log.Write($"Stopped: already stored as '{result.VersionName}' in '{key}'.");
                        Ui.Message($"This content is already stored in '{key}' as '{result.VersionName}'. Nothing was changed.",
                            Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    Log.Write($"Uploaded to '{key}' as '{result.VersionName}': {result.Url}");

                    string text = $"SBOM was saved to Gitea project '{key}' as '{result.VersionName}'.";
                    if (result.TagProblem != null)
                    {
                        Log.Write(result.TagProblem);
                        text += $"\n\nWarning: {result.TagProblem}";
                    }
                    if (askedForKey)
                        text += $"\n\nTip: set Project → Properties → Family = {key}\nso that next exports go there without asking.";
                    if (result.Url != null) text += "\n\n" + result.Url;

                    // No Process.Start: System.Diagnostics.Process requires full trust, the V17 sandbox rejects it.
                    Ui.Message(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (GiteaException ex) when (ex.IsAuthError)
            {
                Log.Write("Gitea rejected the token: " + ex.Message);
                if (tokenFromConfig)
                {
                    Report(new ExportProblem("TS-AUTH-03",
                        "Gitea rejected the company token of TiaSbom. The SBOM was not saved.",
                        "Contact the administrator. The token in the TiaSbom configuration has to be replaced.").ToString(),
                        MessageBoxIcon.Warning);
                }
                else
                {
                    TokenStore.Clear(); // next click asks for a new token
                    Log.Write("Saved token removed, the next export asks for a new one.");
                    Report(WithLog(ExportError.Describe(ex, giteaUrl)), MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                ExportProblem problem = ExportError.Explain(ex, giteaUrl);
                Log.Write($"Export failed, {problem.Code}: {ex}");
                Report(WithLog(problem.ToString()), MessageBoxIcon.Error);
            }
        }

        private static string WithLog(string text) =>
            Log.FilePath == null ? text : text + "\nDetails: " + Log.FilePath;

        private static void Report(string text, MessageBoxIcon icon)
        {
            Ui.Message(text, Title, MessageBoxButtons.OK, icon);
        }
    }
}
