using System;
using System.Collections.Generic;
using System.Linq;
using TiaSbom.Core;
using Xunit;

namespace TiaSbom.Tests
{
    public class ProjectKeyTests
    {
        [Theory]
        [InlineData("10001", "10001")]
        [InlineData("  Linka 1  ", "Linka_1")]
        [InlineData("Čiara-Šamorín", "Ciara-Samorin")]
        [InlineData("a//b\\c", "a_b_c")]
        [InlineData("..x..y..", "x.y")]
        [InlineData("   ", null)]
        [InlineData("ščž", "scz")]
        [InlineData("___", null)]
        public void Normalize(string raw, string expected) => Assert.Equal(expected, ProjectKey.Normalize(raw));

        [Fact]
        public void Normalize_cuts_to_the_Gitea_limit() => Assert.Equal(100, ProjectKey.Normalize(new string('a', 150)).Length);

        [Theory]
        [InlineData("10001_20260101_v0_1_V21", "10001")]
        [InlineData("Linka 1", "Linka_1")]
        [InlineData("12_short", "12_short")]
        public void Suggest(string projectName, string expected)
        {
            Assert.Equal(expected, ProjectKey.Suggest(projectName));
        }

        [Fact]
        public void Version_name_never_ends_with_lock() => Assert.Equal("x.lock_", ProjectKey.VersionName("x.lock"));

        [Fact]
        public void Long_version_names_stay_distinct()
        {
            string a = ProjectKey.VersionName(new string('a', 120) + "1");
            string b = ProjectKey.VersionName(new string('a', 120) + "2");
            Assert.NotEqual(a, b);
            Assert.True(a.Length <= 100);
        }

        [Fact]
        public void Job_numbers_are_never_typos() =>
            Assert.Empty(ProjectKey.Similar("10001", new[] { "10002", "10000" }));

        [Fact]
        public void Close_names_are_reported_as_typos() =>
            Assert.Equal(new[] { "LinkaA" }, ProjectKey.Similar("LinkaB", new[] { "LinkaA", "Other" }));
    }

    public class ExportPlanTests
    {
        private static PreviousSbom V(string name, string signature) => new PreviousSbom { VersionName = name, Signature = signature };

        [Fact]
        public void First_export_uses_the_project_name() =>
            Assert.Equal("Line_1", ExportPlan.For("Line 1", "s1", new PreviousSbom[0]).VersionName);

        [Fact]
        public void Same_content_is_not_written_again()
        {
            ExportPlan plan = ExportPlan.For("Line 1", "s1", new[] { V("Line_1", "s1") });
            Assert.True(plan.AlreadyStored);
            Assert.Equal("Line_1", plan.VersionName);
        }

        [Fact]
        public void Changed_content_never_replaces_a_stored_version()
        {
            Assert.Equal("Line_1_2", ExportPlan.For("Line 1", "s2", new[] { V("Line_1", "s1") }).VersionName);
            Assert.Equal("Line_1_4", ExportPlan.For("Line 1", "s9", new[] { V("Line_1", "s1"), V("Line_1_3", "s3"), V("Other", "s9") }).VersionName);
        }

        [Fact]
        public void Older_numbered_version_with_same_content_is_found()
        {
            ExportPlan plan = ExportPlan.For("Line 1", "s2", new[] { V("Line_1", "s1"), V("Line_1_2", "s2") });
            Assert.True(plan.AlreadyStored);
            Assert.Equal("Line_1_2", plan.VersionName);
        }
    }

    public class GuardTests
    {
        private static ProjectSnapshot Snapshot(string name, params (string station, string order)[] modules)
        {
            var s = new ProjectSnapshot { ProjectName = name, TiaVersion = "V21" };
            foreach (var m in modules)
                s.Items.Add(new HwItem { Station = m.station, Path = m.station + "/" + m.order, OrderNumber = m.order, TypeIdentifier = "OrderNumber:" + m.order });
            return s;
        }

        [Fact]
        public void Unsaved_project_and_empty_project_are_refused()
        {
            var s = Snapshot("x");
            s.HasUnsavedChanges = true;
            List<string> problems = ExportGuard.Problems(s);
            Assert.Equal(2, problems.Count);
        }

        [Fact]
        public void Different_job_number_warns()
        {
            var warnings = MismatchCheck.Evaluate("10001", Snapshot("20002_line", ("PLC_1", "6ES7")), new PreviousSbom[0], new[] { "10001" });
            Assert.Contains(warnings, w => w.Contains("20002"));
        }

        [Fact]
        public void New_version_of_the_same_machine_does_not_warn()
        {
            var current = Snapshot("10001_v2", ("PLC_1", "6ES7 1"), ("PLC_1", "6ES7 2"), ("HMI_1", "6AV2"));
            var stored = new PreviousSbom { VersionName = "10001_v1", ProjectName = "10001_v1" };
            foreach (string f in MismatchCheck.Fingerprints(Snapshot("x", ("PLC_1", "6ES7 1"), ("PLC_1", "6ES7 2")))) stored.Fingerprints.Add(f);
            Assert.Empty(MismatchCheck.Evaluate("10001", current, new[] { stored }, new[] { "10001" }));
        }

        [Fact]
        public void Different_hardware_warns()
        {
            var current = Snapshot("10001_v2", ("PLC_9", "X"), ("PLC_9", "Y"));
            var stored = new PreviousSbom { VersionName = "a", ProjectName = "10001_a" };
            stored.Fingerprints.Add("PLC_1|6ES7 1");
            stored.Fingerprints.Add("PLC_1|6ES7 2");
            Assert.Contains(MismatchCheck.Evaluate("10001", current, new[] { stored }, new[] { "10001" }), w => w.StartsWith("Only"));
        }
    }

    public class ModelTests
    {
        [Theory]
        [InlineData("Rack", "System:Rack.S71200", false)]
        [InlineData("6GK5 208-0BA00-2AC2", "System:Scalance", true)]
        [InlineData("6ES7 214-1AG40-0XB0", "OrderNumber:6ES7 214-1AG40-0XB0/V4.5", true)]
        [InlineData(null, "GSD:GSDML-V2.42-FESTO-CMMT-20230509.XML/DAP/DAP1", true)]
        [InlineData(null, "GSD:GSDML-V2.42-FESTO-CMMT-20230509.XML/M/1", false)]
        public void Is_product(string order, string type, bool expected) =>
            Assert.Equal(expected, new HwItem { OrderNumber = order, TypeIdentifier = type }.IsProduct);

        [Fact]
        public void Identity_from_type_identifier()
        {
            Assert.Equal("6ES7 214-1AG40-0XB0", HwIdentity.OrderNumber("OrderNumber:6ES7 214-1AG40-0XB0/V4.5"));
            Assert.Equal("V4.5", HwIdentity.FirmwareVersion("OrderNumber:6ES7 214-1AG40-0XB0/V4.5"));
            Assert.Null(HwIdentity.OrderNumber("GSD:x"));
        }

        [Theory]
        [InlineData(@"C:\P\Line.ap21", "V21")]
        [InlineData(@"C:\P\Line.zap19", "V19")]
        [InlineData(@"C:\P\Line.ap15_1", "V15.1")]
        [InlineData(@"C:\P\Line.txt", null)]
        public void Tia_version_from_path(string path, string expected) => Assert.Equal(expected, TiaBuild.VersionFromPath(path));

        [Fact]
        public void Supplier_from_GSDML_name() =>
            Assert.Equal("KEYENCE", SbomBuilder.Supplier(new HwItem { IsGsd = true, TypeIdentifier = "GSD:GSDML-V2.42-KEYENCE-VS-XXXX-20230509.XML/DAP" }));
    }

    public class JsonTests
    {
        [Fact]
        public void Round_trip_keeps_special_characters()
        {
            string text = "Čiara \"1\" \\ \n\t \u0001 ✓";
            string json = Core.Json.Serialize(new JsonObject { { "a", text }, { "b", 5 }, { "c", true }, { "d", new List<object> { "x" } } });
            var back = (Dictionary<string, object>)Core.Json.Parse(json);
            Assert.Equal(text, back["a"]);
            Assert.Equal(5.0, back["b"]);
            Assert.Equal(true, back["c"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{\"a\":}")]
        [InlineData("<html>")]
        [InlineData("{} x")]
        public void Invalid_json_is_a_FormatException(string text) => Assert.Throws<FormatException>(() => Core.Json.Parse(text));

        [Fact]
        public void Keys_ignore_case()
        {
            var o = (Dictionary<string, object>)Core.Json.Parse("{\"BaseUrl\":\"x\"}");
            Assert.Equal("x", o["baseurl"]);
        }
    }

    public class ReadmeTests
    {
        [Fact]
        public void Lists_current_and_older_versions_newest_first()
        {
            var s = new ProjectSnapshot { ProjectName = "Line 1", TiaVersion = "V21", ExportedBy = "eng" };
            var older = new[]
            {
                new PreviousSbom { VersionName = "old1", StoredAt = DateTimeOffset.Parse("2026-01-01T00:00:00Z") },
                new PreviousSbom { VersionName = "old2", StoredAt = DateTimeOffset.Parse("2026-02-01T00:00:00Z") },
            };
            string readme = Readme.Build("10001", s, older, "Line_1");
            Assert.Contains("# SBOM – 10001", readme);
            Assert.True(readme.IndexOf("Line_1", StringComparison.Ordinal) < readme.IndexOf("old2", StringComparison.Ordinal));
            Assert.True(readme.IndexOf("old2", StringComparison.Ordinal) < readme.IndexOf("old1", StringComparison.Ordinal));
        }
    }

    internal static class Samples
    {
        /// <summary>A project like the real test projects: CPU with firmware, passive module, rack, GSD device, HMI panel.</summary>
        public static ProjectSnapshot Line(string name = "10001_20260101_v0_1_V21")
        {
            var s = new ProjectSnapshot
            {
                ProjectName = name,
                ProjectPath = @"C:\Projects\Line\Line.ap21",
                Family = "10001",
                TiaVersion = "V21",
                UsedProductsRead = true,
                UnpluggedItemsRead = true,
                ExportedBy = "engineer",
                ExportedAtUtc = new DateTime(2026, 11, 18, 20, 0, 0, DateTimeKind.Utc),
            };
            s.UsedProducts.Add(new UsedProduct { Name = "STEP 7 Professional", Version = "V21" });
            s.UsedProducts.Add(new UsedProduct { Name = "WinCC Unified", Version = "" });
            s.Items.Add(new HwItem { Station = "PLC_1", Path = "PLC_1/Rail_0/PLC_1", Name = "PLC_1", TypeName = "CPU 1516F-3 PN/DP",
                OrderNumber = "6ES7 516-3FP03-0AB0", TypeIdentifier = "OrderNumber:6ES7 516-3FP03-0AB0/V3.1", FirmwareVersion = "V3.1", IsPlugged = true });
            s.Items.Add(new HwItem { Station = "PLC_1", Path = "PLC_1/Rail_0/DI 16x24VDC_1", Name = "DI 16x24VDC_1", TypeName = "DI 16x24VDC BA",
                OrderNumber = "6ES7 521-1BH10-0AA0", FirmwareVersion = "V0.0", IsPlugged = true });
            s.Items.Add(new HwItem { Station = "PLC_1", Path = "PLC_1/Rail_0", Name = "Rail_0", OrderNumber = "Rack", TypeIdentifier = "System:Rack.S71500" });
            s.Items.Add(new HwItem { Station = "Festo", Path = "Festo/CMMT", Name = "CMMT", IsGsd = true,
                TypeIdentifier = "GSD:GSDML-V2.42-FESTO-CMMT-ST-20230509.XML/DAP/DAP1", FirmwareVersion = "V1.2" });
            s.Items.Add(new HwItem { Station = "HMI_1", Path = "HMI_1/HMI_RT_1", Name = "HMI_1", OrderNumber = "6AV2 128-3GB06-0AX1",
                FirmwareVersion = "V21.0.0.0", IsPlugged = true });
            return s;
        }
    }

    public class SbomBuilderTests
    {
        [Fact]
        public void Rack_is_left_out_and_firmware_is_nested()
        {
            string json = SbomBuilder.ToJson(SbomBuilder.Build(Samples.Line()));
            var bom = (Dictionary<string, object>)Core.Json.Parse(json);
            var components = ((List<object>)bom["components"]).Cast<Dictionary<string, object>>().ToList();
            Assert.Equal(2, components.Count(c => (string)c["type"] == "application"));
            Assert.Equal(4, components.Count(c => (string)c["type"] == "device"));
            Assert.DoesNotContain(components, c => (string)c["name"] == "Rail_0");

            var cpu = components.Single(c => (string)c["name"] == "CPU 1516F-3 PN/DP");
            var firmware = (Dictionary<string, object>)((List<object>)cpu["components"]).Single();
            Assert.Equal("V3.1", firmware["version"]);
            Assert.Equal("FESTO", ((Dictionary<string, object>)components.Single(c => (string)c["name"] == "CMMT")["supplier"])["name"]);
        }

        [Fact]
        public void Signature_of_snapshot_matches_signature_read_back_from_stored_sbom()
        {
            // The client computes the stored signature from sbom.json; if both disagree, every export would look new.
            ProjectSnapshot s = Samples.Line();
            string json = SbomBuilder.ToJson(SbomBuilder.Build(s));
            var read = typeof(Gitea.GiteaClient).GetMethod("ReadVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var stored = (PreviousSbom)read.Invoke(null, new object[] { "v", json });
            Assert.Equal(SbomSignature.Of(s), stored.Signature);
        }
    }
}
