using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TiaSbom.Core;

namespace TiaSbom.Gitea
{
    public sealed class PublishResult
    {
        public string VersionName { get; set; }
        /// <summary>The same content was already stored, nothing was written.</summary>
        public bool AlreadyStored { get; set; }
        public string Url { get; set; }
        public string TagProblem { get; set; }
    }

    /// <summary>
    /// The write step shared by the Add-In and the CLI. Two people can export to one repository at the same moment,
    /// so every attempt starts from what the repository holds now.
    /// </summary>
    public static class SbomPublisher
    {
        private const int Attempts = 5;
        private static readonly Random Jitter = new Random();

        public static async Task<PublishResult> PublishAsync(GiteaClient gitea, string key, ProjectSnapshot snapshot, string json)
        {
            string signature = SbomSignature.Of(snapshot);
            for (int attempt = 1; ; attempt++)
            {
                List<PreviousSbom> versions = await gitea.GetVersionsAsync(key).ConfigureAwait(false);
                ExportPlan plan = ExportPlan.For(snapshot.ProjectName, signature, versions);
                if (plan.AlreadyStored)
                    return new PublishResult { VersionName = plan.VersionName, AlreadyStored = true };

                try
                {
                    string readme = Readme.Build(key, snapshot, versions, plan.VersionName);
                    UploadResult upload = await gitea.UploadSbomAsync(key, plan.VersionName, snapshot.ProjectName, json, readme).ConfigureAwait(false);
                    return new PublishResult { VersionName = plan.VersionName, Url = upload.Url, TagProblem = upload.TagProblem };
                }
                // Gitea 28 reports a lost race also as 500 "PushOutOfDate" with an empty message. A retry is safe: when the
                // first write did land, the plan finds the same signature and writes nothing.
                catch (GiteaException ex) when ((ex.IsConflict || (int)ex.StatusCode >= 500) && attempt < Attempts)
                {
                    // Next attempt reads the repository again. A random pause keeps two losers from colliding once more.
                }
                int pause;
                lock (Jitter) pause = Jitter.Next(200, 600) * attempt;
                await Task.Delay(pause).ConfigureAwait(false);
            }
        }
    }
}
