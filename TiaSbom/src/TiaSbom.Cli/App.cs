using System;
using System.IO;
using System.Linq;
using Siemens.Engineering;
using TiaSbom.Core;
using TiaSbom.Gitea;
using TiaSbom.Tia;

namespace TiaSbom.Cli
{
    /// <summary>
    /// tiasbom --project C:\P\Line1.ap21 [--out sbom.json] [--upload]
    /// tiasbom --archive C:\P\Line1.zap21 [--out sbom.json] [--upload]
    /// tiasbom --attach [--out sbom.json] [--upload]      (uses project open in a running TIA Portal)
    /// tiasbom --login                                     (stores Gitea token, DPAPI-encrypted)
    /// </summary>
    internal static class App
    {
        public static int Run(string[] args)
        {
            if (args.Length == 0 || Has(args, "--help")) { PrintUsage(); return 0; }

            if (Has(args, "--login"))
            {
                Console.Write("Gitea access token: ");
                TokenStore.Save(ReadSecret());
                Console.WriteLine("Token saved.");
                return 0;
            }

            string outFile = Value(args, "--out");
            bool upload = Has(args, "--upload");
            if (outFile == null && !upload) outFile = "sbom.json";

            ProjectSnapshot snapshot = Has(args, "--attach")
                ? FromRunningPortal()
                : FromFile(Value(args, "--project"), Value(args, "--archive"));

            Console.WriteLine($"Collected {snapshot.Items.Count} device items and {snapshot.UsedProducts.Count} used products " +
                              $"from '{snapshot.ProjectName}' (Family: '{snapshot.Family}').");
            string json = SbomBuilder.ToJson(SbomBuilder.Build(snapshot));

            if (outFile != null)
            {
                File.WriteAllText(outFile, json);
                Console.WriteLine("SBOM written to " + Path.GetFullPath(outFile));
            }

            if (upload)
            {
                // --key overrides; otherwise the TIA project property "Family" is the project key.
                string key = ProjectKey.Normalize(Value(args, "--key") ?? snapshot.Family)
                    ?? throw new InvalidOperationException(
                        $"Project has no Family. Set it in TIA (Project > Properties) or pass --key (suggestion: {ProjectKey.Suggest(snapshot.ProjectName)}).");

                GiteaSettings settings = GiteaSettings.Load(Value(args, "--config"));
                string token = Environment.GetEnvironmentVariable("GITEA_TOKEN");
                if (string.IsNullOrEmpty(token)) token = settings.Token;
                if (string.IsNullOrEmpty(token) && !TokenStore.TryLoad(out token))
                    throw new InvalidOperationException("No Gitea token. Run 'tiasbom --login', set GITEA_TOKEN or Token in config.json.");

                using (var gitea = new GiteaClient(settings, token))
                {
                    var warnings = MismatchCheck.Evaluate(key, snapshot, gitea.GetPreviousAsync(key).GetAwaiter().GetResult());
                    foreach (string w in warnings) Console.Error.WriteLine("WARNING: " + w);
                    if (warnings.Count > 0 && !Has(args, "--force"))
                        throw new InvalidOperationException($"Refusing to upload to '{key}' — looks like a different project. Use --force to override.");

                    string url = gitea.UploadSbomAsync(key, ProjectKey.VersionName(snapshot.ProjectName), snapshot.ProjectName,
                        json, Readme.Build(key, snapshot)).GetAwaiter().GetResult();
                    Console.WriteLine($"Uploaded to '{key}': " + (url ?? "(no commit URL returned)"));
                }
            }
            return 0;
        }

        private static ProjectSnapshot FromFile(string projectPath, string archivePath)
        {
            if (projectPath == null && archivePath == null)
                throw new ArgumentException("Specify --project, --archive or --attach.");

            using (var portal = new TiaPortal(TiaPortalMode.WithoutUserInterface))
            {
                Project project;
                if (archivePath != null)
                {
                    // Retrieve into a temp folder; the archive itself is never modified.
                    var target = new DirectoryInfo(Path.Combine(Path.GetTempPath(), "tiasbom", Guid.NewGuid().ToString("N")));
                    target.Create();
                    project = portal.Projects.Retrieve(new FileInfo(archivePath), target);
                }
                else
                {
                    project = portal.Projects.Open(new FileInfo(projectPath));
                }

                try { return HardwareCollector.Collect(project); }
                finally { project.Close(); }
            }
        }

        private static ProjectSnapshot FromRunningPortal()
        {
            // TIA shows an Openness firewall prompt on first attach — expected.
            var process = TiaPortal.GetProcesses().FirstOrDefault()
                ?? throw new InvalidOperationException("No running TIA Portal V21 found.");

            using (TiaPortal portal = process.Attach())
            {
                Project project = portal.Projects.FirstOrDefault()
                    ?? throw new InvalidOperationException("TIA Portal has no open project.");
                return HardwareCollector.Collect(project); // do not close — the user owns this project
            }
        }

        private static bool Has(string[] args, string name) => args.Contains(name, StringComparer.OrdinalIgnoreCase);

        private static string Value(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static string ReadSecret()
        {
            var sb = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace) { if (sb.Length > 0) sb.Length--; }
                else sb.Append(key.KeyChar);
            }
            Console.WriteLine();
            return sb.ToString();
        }

        private static void PrintUsage() => Console.WriteLine(
@"tiasbom --project <file.ap21> | --archive <file.zap21> | --attach
        [--out <sbom.json>] [--upload [--key <project key>] [--force]] [--config <config.json>]
tiasbom --login");
    }
}
