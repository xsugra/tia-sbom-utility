using System;
using System.IO;
using System.Reflection;

namespace TiaSbom.Cli
{
    /// <summary>
    /// Entry point. Must not reference any Siemens.Engineering type — the resolver has to be registered first
    /// (docs: "Connecting to the TIA Portal"). All Openness code lives in <see cref="App"/>.
    /// </summary>
    internal static class Program
    {
        private static string _opennessFolder;

        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static int Main(string[] args)
        {
            try
            {
                // --help and --login work without TIA Portal.
                if (args.Length > 0 && Array.FindIndex(args, IsLocalCommand) < 0)
                    _opennessFolder = OpennessLocator.FindApiDir(args);
                return App.Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static bool IsLocalCommand(string arg) =>
            string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "--login", StringComparison.OrdinalIgnoreCase);

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            if (_opennessFolder == null || !requested.Name.StartsWith("Siemens.Engineering", StringComparison.Ordinal))
                return null;

            string filePath = Path.Combine(_opennessFolder, requested.Name + ".dll");
            if (!File.Exists(filePath)) return null;

            Assembly loaded = Assembly.LoadFrom(filePath);
            if (requested.FullName != loaded.GetName().FullName)
                throw new FileNotFoundException("TIA Portal Openness version does not match", filePath);
            return loaded;
        }
    }
}
