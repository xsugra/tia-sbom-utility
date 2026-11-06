using System.Collections.Generic;
using System.Linq;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaSbom.Core;
#if TIA_HAS_SESSIONS
using TiaProject = Siemens.Engineering.ProjectBase; // Project or MultiuserProject
#else
using TiaProject = Siemens.Engineering.Project;
#endif

namespace TiaSbom.Tia
{
    /// <summary>
    /// Reads the hardware configuration via TIA Portal Openness.
    /// Shared by CLI and Add-In — the only place that touches Siemens.Engineering HW types.
    /// </summary>
    public static class HardwareCollector
    {
        /// <summary>The project open in this TIA Portal: a single-user project or a multiuser local session. Null if none.</summary>
        public static TiaProject FindOpenProject(TiaPortal portal)
        {
            Project single = portal.Projects.FirstOrDefault();
            if (single != null) return single;
#if TIA_HAS_SESSIONS
            return portal.LocalSessions.FirstOrDefault()?.Project;
#else
            return null;
#endif
        }

        public static ProjectSnapshot Collect(TiaProject project)
        {
            string path = project.Path?.FullName;
            var snapshot = new ProjectSnapshot
            {
                ProjectName = project.Name,
                ProjectPath = path,
                TiaVersion = TiaBuild.VersionFromPath(path) ?? SbomBuilder.Unknown,
                // Read dynamically: the Family property is documented on Project, not on ProjectBase.
                Family = Get<string>(project, "Family")?.Trim(),
                HasUnsavedChanges = Get<bool>(project, "IsModified"),
#if TIA_HAS_UNPLUGGED_ITEMS
                UnpluggedItemsRead = true,
#endif
            };

            // "Read project related attributes" documents UsedProducts on Project only.
            if (project is Project single)
            {
                snapshot.UsedProductsRead = true;
                foreach (var product in single.UsedProducts)
                    snapshot.UsedProducts.Add(new Core.UsedProduct { Name = product.Name, Version = product.Version });
            }

            foreach (Device device in AllDevices(project))
            {
                // Plugged items: Device -> rack -> module -> submodule
                Walk(device.DeviceItems, device.Name, device.Name, snapshot.Items);
#if TIA_HAS_UNPLUGGED_ITEMS
                // Unplugged items have no container and are only reachable via Device.UnpluggedItems
                foreach (DeviceItem item in device.UnpluggedItems)
                    Add(item, device.Name, device.Name + "/<unplugged>", snapshot.Items);
#endif
            }
            return snapshot;
        }

        private static IEnumerable<Device> AllDevices(TiaProject project)
        {
            foreach (Device d in project.Devices) yield return d;
            foreach (Device d in project.UngroupedDevicesGroup.Devices) yield return d;
            foreach (DeviceUserGroup g in project.DeviceGroups)
                foreach (Device d in InGroup(g)) yield return d;
        }

        private static IEnumerable<Device> InGroup(DeviceUserGroup group)
        {
            foreach (Device d in group.Devices) yield return d;
            foreach (DeviceUserGroup sub in group.Groups)
                foreach (Device d in InGroup(sub)) yield return d;
        }

        private static void Walk(DeviceItemComposition items, string station, string parentPath, List<HwItem> result)
        {
            foreach (DeviceItem item in items)
            {
                string path = Add(item, station, parentPath, result);
                Walk(item.DeviceItems, station, path, result);
            }
        }

        private static string Add(DeviceItem item, string station, string parentPath, List<HwItem> result)
        {
            string path = parentPath + "/" + item.Name;
            result.Add(new HwItem
            {
                Station = station,
                Path = path,
                Name = item.Name,
                TypeIdentifier = item.TypeIdentifier,
                // IsGsd on device items returned false for real GSD devices in V21 — TypeIdentifier is authoritative.
                IsGsd = (item.TypeIdentifier?.StartsWith("GSD:") ?? false) || Get<bool>(item, "IsGsd"),
                IsPlugged = item.IsPlugged,
                // Dynamic attributes ("Mandatory attributes of device items") — not present on every item.
                TypeName = Get<string>(item, "TypeName"),
                OrderNumber = Filled(Get<string>(item, "OrderNumber")) ?? Core.HwIdentity.OrderNumber(item.TypeIdentifier),
                FirmwareVersion = Filled(Get<string>(item, "FirmwareVersion")) ?? Core.HwIdentity.FirmwareVersion(item.TypeIdentifier),
            });
            return path;
        }

        private static string Filled(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static T Get<T>(IEngineeringObject obj, string attribute)
        {
            try { return obj.GetAttribute(attribute) is T value ? value : default; }
            catch (EngineeringException) { return default; } // attribute not supported on this item
        }
    }
}
