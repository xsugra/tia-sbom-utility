using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TiaSbom.Core
{
    /// <summary>
    /// Maps a <see cref="ProjectSnapshot"/> to a CycloneDX 1.6 BOM (hardware + firmware).
    /// Written by hand on purpose (no CycloneDX.Core) — see <see cref="Json"/>. Validate output against the CycloneDX schema in CI.
    /// </summary>
    public static class SbomBuilder
    {
        public const string Unknown = "unknown";
        public const string SpecVersion = "1.6";
        public const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

        public static JsonObject Build(ProjectSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            return new JsonObject
            {
                { "bomFormat", "CycloneDX" },
                { "specVersion", SpecVersion },
                { "serialNumber", "urn:uuid:" + Guid.NewGuid() },
                { "version", 1 },
                { "metadata", new JsonObject
                    {
                        { "timestamp", snapshot.ExportedAtUtc.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture) },
                        { "component", new JsonObject
                            {
                                { "type", "application" },
                                { "bom-ref", "project" },
                                { "name", snapshot.ProjectName ?? Unknown },
                                { "properties", new List<object>
                                    {
                                        Prop("tia:version", snapshot.TiaVersion),
                                        Prop("tia:opennessApi", TiaBuild.Api),
                                        Prop("tia:unpluggedItemsRead", snapshot.UnpluggedItemsRead ? "true" : "false"),
                                        Prop("tia:usedProductsRead", snapshot.UsedProductsRead ? "true" : "false"),
                                        Prop("tia:family", snapshot.Family),
                                        Prop("tia:projectPath", snapshot.ProjectPath),
                                        Prop("tia:exportedBy", snapshot.ExportedBy),
                                    }
                                },
                            }
                        },
                    }
                },
                { "components", snapshot.UsedProducts
                    .Select(ToSoftwareComponent)
                    .Concat(snapshot.Items
                        .Where(i => i.IsProduct)
                        .Select(ToComponent))
                    .ToList() },
            };
        }

        /// <summary>Engineering software the project was built with (STEP 7, WinCC, Safety, ...).</summary>
        private static object ToSoftwareComponent(UsedProduct product) => new JsonObject
        {
            { "type", "application" },
            { "bom-ref", "sw:" + product.Name },
            { "supplier", new JsonObject { { "name", "Siemens AG" } } },
            { "name", product.Name },
            { "version", string.IsNullOrWhiteSpace(product.Version) ? Unknown : product.Version },
            { "properties", new List<object> { Prop("tia:kind", "usedProduct") } },
        };

        public static string ToJson(JsonObject bom) => Json.Serialize(bom);

        private static object ToComponent(HwItem item)
        {
            var properties = new List<object>
            {
                Prop("siemens:orderNumber", item.OrderNumber),
                Prop("tia:typeIdentifier", item.TypeIdentifier),
                Prop("tia:station", item.Station),
                Prop("tia:path", item.Path),
                Prop("tia:isGsd", item.IsGsd ? "true" : "false"),
                Prop("tia:isPlugged", item.IsPlugged ? "true" : "false"),
            };

            // Firmware as nested component: this is what vulnerability matching (CPE / ProductCERT) keys on.
            // Passive modules (racks, BaseUnits, DI/DQ ST) report "" or "V0.0" — they have no firmware.
            List<object> firmware = null;
            if (HasFirmware(item.FirmwareVersion))
            {
                firmware = new List<object>
                {
                    new JsonObject
                    {
                        { "type", "firmware" },
                        { "bom-ref", "fw:" + item.Path },
                        { "name", item.ProductName + " firmware" },
                        { "version", item.FirmwareVersion },
                        // TODO: map OrderNumber -> CPE (e.g. cpe:2.3:o:siemens:simatic_s7-1500_cpu_firmware:...) via a lookup table.
                    },
                };
            }
            else
            {
                properties.Add(Prop("tia:firmware", "none"));
            }

            return new JsonObject
            {
                { "type", "device" },
                { "bom-ref", "hw:" + item.Path },
                { "supplier", new JsonObject { { "name", Supplier(item) } } },
                { "name", item.ProductName },
                { "description", item.Name },
                { "properties", properties },
                { "components", firmware },
            };
        }

        public static bool HasFirmware(string version) =>
            !string.IsNullOrWhiteSpace(version) && version != "V0.0" && version != "0.0";

        /// <summary>
        /// Siemens catalog items -> "Siemens AG". GSD items -> vendor from the GSDML file name
        /// ("GSD:GSDML-V2.42-KEYENCE-VS-XXXX-20230509.XML/..." -> "KEYENCE").
        /// </summary>
        public static string Supplier(HwItem item)
        {
            if (!item.IsGsd) return "Siemens AG";

            string id = item.TypeIdentifier ?? string.Empty;
            int start = id.IndexOf("GSDML-", StringComparison.OrdinalIgnoreCase);
            if (start >= 0)
            {
                string[] parts = id.Substring(start).Split('-');
                if (parts.Length > 2 && parts[2].Length > 0) return parts[2];
            }
            return Unknown;
        }

        private static JsonObject Prop(string name, string value) =>
            new JsonObject { { "name", name }, { "value", value ?? string.Empty } };
    }
}
