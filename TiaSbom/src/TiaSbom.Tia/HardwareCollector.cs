using System.Collections.Generic;
using Siemens.Engineering;
using Siemens.Engineering.HW;
using TiaSbom.Core;

namespace TiaSbom.Tia
{
    /// <summary>
    /// Reads hardware configuration via TIA Portal Openness V21.
    /// Shared by CLI and Add-In — the only place that touches Siemens.Engineering HW types.
    /// </summary>
    public static class HardwareCollector
    {
        public static ProjectSnapshot Collect(Project project)
        {
            var snapshot = new ProjectSnapshot
            {
                ProjectName = project.Name,
                ProjectPath = project.Path?.FullName,
                Family = project.Family?.Trim(),
            };

            // V21 docs: "Read project related attributes" -> UsedProducts (name + version)
            foreach (var product in project.UsedProducts)
                snapshot.UsedProducts.Add(new Core.UsedProduct { Name = product.Name, Version = product.Version });

            foreach (Device device in AllDevices(project))
            {
                // Plugged items: Device -> rack -> module -> submodule
                Walk(device.DeviceItems, device.Name, device.Name, snapshot.Items);
                // Unplugged items have no container and are only reachable via Device.UnpluggedItems
                foreach (DeviceItem item in device.UnpluggedItems)
                    Add(item, device.Name, device.Name + "/<unplugged>", snapshot.Items);
            }
            return snapshot;
        }

        private static IEnumerable<Device> AllDevices(Project project)
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
                // Dynamic attributes (V21 docs: "Mandatory attributes of device items") — not present on every item.
                TypeName = Get<string>(item, "TypeName"),
                OrderNumber = Get<string>(item, "OrderNumber"),
                FirmwareVersion = Get<string>(item, "FirmwareVersion"),
            });
            return path;
        }

        private static T Get<T>(IEngineeringObject obj, string attribute)
        {
            try { return obj.GetAttribute(attribute) is T value ? value : default; }
            catch (EngineeringException) { return default; } // attribute not supported on this item
        }
    }
}
