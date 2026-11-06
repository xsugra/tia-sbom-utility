using System;
using System.Collections.Generic;
using System.Linq;

namespace TiaSbom.Core
{
    /// <summary>One hardware module (CPU, IO module, CP, ...) found in the TIA project.</summary>
    public sealed class HwItem
    {
        public string Station { get; set; }
        /// <summary>Hierarchical path inside the station, e.g. "PLC_1/Rail_0/DI 16x24VDC_1". Unique per project.</summary>
        public string Path { get; set; }
        public string Name { get; set; }
        public string TypeName { get; set; }
        public string TypeIdentifier { get; set; }
        public string OrderNumber { get; set; }
        public string FirmwareVersion { get; set; }
        public bool IsGsd { get; set; }
        public bool IsPlugged { get; set; }

        /// <summary>
        /// False for items without a real product identity, e.g. the S7-1200 rack: order number "Rack",
        /// type "System:Rack.S71200". System items with a real order number (SCALANCE) are products.
        /// A GSD device access point is always a product: some GSDML files give it no order number (Festo CMMT-ST).
        /// </summary>
        public bool IsProduct =>
            IsGsdDeviceAccessPoint ||
            (!string.IsNullOrWhiteSpace(OrderNumber) &&
             (TypeIdentifier == null || !TypeIdentifier.StartsWith("System:") || OrderNumber.Any(char.IsDigit)));

        private bool IsGsdDeviceAccessPoint =>
            TypeIdentifier != null && TypeIdentifier.StartsWith("GSD:") && TypeIdentifier.Contains("/DAP/");

        /// <summary>HMI panels report no TypeName, and their item name ("HMI_1") says nothing about the product.</summary>
        public string ProductName =>
            !string.IsNullOrWhiteSpace(TypeName) ? TypeName
            : !string.IsNullOrWhiteSpace(OrderNumber) ? OrderNumber
            : Name;
    }

    /// <summary>
    /// Reads a hardware catalog identifier: "OrderNumber:&lt;order&gt;[/&lt;firmware&gt;[/&lt;additional&gt;]]".
    /// Openness allows the OrderNumber attribute of a device item to be empty, the identifier still names the product.
    /// </summary>
    public static class HwIdentity
    {
        private const string Prefix = "OrderNumber:";

        public static string OrderNumber(string typeIdentifier) => Part(typeIdentifier, 0);

        public static string FirmwareVersion(string typeIdentifier) => Part(typeIdentifier, 1);

        private static string Part(string typeIdentifier, int index)
        {
            if (typeIdentifier == null || !typeIdentifier.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;

            string[] parts = typeIdentifier.Substring(Prefix.Length).Split('/');
            return index < parts.Length && !string.IsNullOrWhiteSpace(parts[index]) ? parts[index].Trim() : null;
        }
    }

    /// <summary>Engineering software used by the project (Project.UsedProducts), e.g. "STEP 7 Professional" V21.</summary>
    public sealed class UsedProduct
    {
        public string Name { get; set; }
        public string Version { get; set; }
    }

    /// <summary>Everything the SBOM builder needs; produced by TiaSbom.Tia, independent of Openness types.</summary>
    public sealed class ProjectSnapshot
    {
        public string ProjectName { get; set; }
        public string ProjectPath { get; set; }
        /// <summary>Project property "Family" — the stable project key (= Gitea repository name). May be empty.</summary>
        public string Family { get; set; }
        /// <summary>TIA Portal version of the project, not the Openness API version — see <see cref="TiaBuild"/>.</summary>
        public string TiaVersion { get; set; } = SbomBuilder.Unknown;
        /// <summary>False when this API cannot read unplugged modules, so the SBOM may be incomplete for them.</summary>
        public bool UnpluggedItemsRead { get; set; }
        public string ExportedBy { get; set; } = Environment.UserName;
        public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
        public List<HwItem> Items { get; } = new List<HwItem>();
        public List<UsedProduct> UsedProducts { get; } = new List<UsedProduct>();
    }
}
