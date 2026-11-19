#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Json.Schema;
using TiaSbom.Core;
using Xunit;

namespace TiaSbom.Tests
{
    /// <summary>
    /// The SBOM is written by hand (no CycloneDX library may load in TIA), so the official CycloneDX 1.6 schema
    /// is the only proof that the output is valid. Schemas come unchanged from github.com/CycloneDX/specification, tag 1.6.
    /// </summary>
    public class SchemaTests
    {
        private static readonly Lazy<JsonSchema> Bom = new Lazy<JsonSchema>(() =>
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Schema");
            foreach (string dependency in new[] { "spdx.schema.json", "jsf-0.82.schema.json" })
            {
                var schema = JsonSchema.FromFile(Path.Combine(folder, dependency));
                SchemaRegistry.Global.Register(new Uri("http://cyclonedx.org/schema/" + dependency), schema);
            }
            return JsonSchema.FromFile(Path.Combine(folder, "bom-1.6.schema.json"));
        });

        private static void AssertValid(string json)
        {
            EvaluationResults result = Bom.Value.Evaluate(JsonNode.Parse(json), new EvaluationOptions { OutputFormat = OutputFormat.List });
            string errors = string.Join("\n", (result.Details ?? new EvaluationResults[0])
                .Where(d => d.HasErrors)
                .SelectMany(d => d.Errors.Select(e => $"{d.InstanceLocation}: {e.Key} {e.Value}")));
            Assert.True(result.IsValid, errors);
        }

        [Fact]
        public void Typical_project_is_valid_CycloneDX_1_6() =>
            AssertValid(SbomBuilder.ToJson(SbomBuilder.Build(Samples.Line())));

        [Fact]
        public void Empty_and_odd_values_stay_valid()
        {
            var s = new ProjectSnapshot { ProjectName = null, Family = null, TiaVersion = SbomBuilder.Unknown };
            s.Items.Add(new HwItem { Path = "A/\"quoted\"\\path", OrderNumber = "6ES7 Ä", TypeName = "", Name = "x\ny" });
            AssertValid(SbomBuilder.ToJson(SbomBuilder.Build(s)));
        }

        [Fact]
        public void Invalid_document_is_rejected_so_the_test_can_fail()
        {
            EvaluationResults result = Bom.Value.Evaluate(JsonNode.Parse("{\"bomFormat\":\"CycloneDX\",\"specVersion\":\"1.6\",\"components\":[{\"type\":\"spaceship\",\"name\":\"x\"}]}"));
            Assert.False(result.IsValid);
        }
    }
}
#endif
