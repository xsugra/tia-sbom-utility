using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            try
            {
                GiteaSettings settings = GiteaSettings.Load();
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

                using (var gitea = new GiteaClient(settings, token))
                {
                    string key = ProjectKey.Normalize(snapshot.Family);
                    bool askedForKey = key == null;
                    if (askedForKey)
                    {
                        List<string> existing = gitea.ListReposAsync().GetAwaiter().GetResult();
                        Log.Write($"Repositories listed: {existing.Count}");
                        key = Ui.Run(owner => RepoPickerDialog.Ask(owner, snapshot.ProjectName,
                            ProjectKey.Suggest(snapshot.ProjectName), existing));
                        if (key == null)
                        {
                            Log.Write("Stopped: no project key chosen.");
                            return;
                        }
                    }

                    // Guard against exporting into another project's repository.
                    List<string> warnings = MismatchCheck.Evaluate(key, snapshot, gitea.GetPreviousAsync(key).GetAwaiter().GetResult());
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
                    string url;
                    using (_tiaPortal.ExclusiveAccess("Uploading SBOM to Gitea…"))
                    {
                        url = gitea.UploadSbomAsync(key, ProjectKey.VersionName(snapshot.ProjectName), snapshot.ProjectName,
                            json, Readme.Build(key, snapshot)).GetAwaiter().GetResult();
                    }
                    Log.Write($"Uploaded to '{key}': {url}");

                    string text = $"SBOM was saved to Gitea project '{key}'.";
                    if (askedForKey)
                        text += $"\n\nTip: set Project → Properties → Family = {key}\nso that next exports go there without asking.";
                    if (url != null) text += "\n\nOpen the commit in the browser?";

                    if (Ui.Message(text, Title, url != null ? MessageBoxButtons.YesNo : MessageBoxButtons.OK,
                            MessageBoxIcon.Information) == DialogResult.Yes)
                    {
                        Process.Start(url);
                    }
                }
            }
            catch (GiteaException ex) when (ex.IsAuthError)
            {
                Log.Write("Gitea rejected the token: " + ex.Message);
                if (tokenFromConfig)
                {
                    Report("Gitea rejected the service token from the TiaSbom configuration.\nPlease contact IT.\n\n" + ex.Message,
                        MessageBoxIcon.Warning);
                }
                else
                {
                    TokenStore.Clear(); // next click asks for a new token
                    Report("Gitea rejected the token (or it may not create repositories). Please try again with a valid token.\n\n" + ex.Message,
                        MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Write("Export failed: " + ex);
                Report("SBOM export failed:\n\n" + ex.Message, MessageBoxIcon.Error);
            }
        }

        // The last step of a failed export: a failure to show the message must still end up in the log.
        private static void Report(string text, MessageBoxIcon icon)
        {
            try
            {
                Ui.Message(text, Title, MessageBoxButtons.OK, icon);
            }
            catch (Exception ex)
            {
                Log.Write("Message not shown: " + ex);
            }
        }
    }
}
