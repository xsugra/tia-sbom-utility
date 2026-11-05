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
        /// </summary>
        public bool IsProduct =>
            !string.IsNullOrWhiteSpace(OrderNumber) &&
            (TypeIdentifier == null || !TypeIdentifier.StartsWith("System:") || OrderNumber.Any(char.IsDigit));
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
