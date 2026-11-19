using System;
using System.Threading.Tasks;
using TiaSbom.Core;
using TiaSbom.Gitea;
using Xunit;

namespace TiaSbom.Tests
{
    /// <summary>
    /// Fills a demo repository with invented data for the screenshots in the documentation, so no customer project
    /// ever appears there. Runs only with TIASBOM_DEMO=1 and leaves the repository in place.
    /// </summary>
    public class DemoData
    {
        [GiteaFact]
        public async Task Create_demo_repository_for_docs()
        {
            if (Environment.GetEnvironmentVariable("TIASBOM_DEMO") != "1") return;
            using (var admin = new GiteaAdmin()) await admin.EnsureOrgAsync();
            using (var gitea = new GiteaClient(GiteaTarget.Settings(), GiteaTarget.Token))
            {
                ProjectSnapshot v1 = Samples.Line("10001_Demo_Linka_v1");
                v1.Family = "10001";
                v1.ProjectPath = @"C:\Projekty\Demo\10001_Demo_Linka_v1\10001_Demo_Linka_v1.ap21";
                v1.ExportedBy = "inzinier1";
                await SbomPublisher.PublishAsync(gitea, "10001", v1, SbomBuilder.ToJson(SbomBuilder.Build(v1)));

                ProjectSnapshot v2 = Samples.Line("10001_Demo_Linka_v2");
                v2.Family = "10001";
                v2.ProjectPath = @"C:\Projekty\Demo\10001_Demo_Linka_v2\10001_Demo_Linka_v2.ap21";
                v2.ExportedBy = "inzinier2";
                v2.Items[0].FirmwareVersion = "V3.2";
                await SbomPublisher.PublishAsync(gitea, "10001", v2, SbomBuilder.ToJson(SbomBuilder.Build(v2)));
            }
        }
    }
}
