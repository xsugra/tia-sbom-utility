using System;
using System.IO;
using System.Reflection;

namespace TiaSbom.Cli
{
    /// <summary>
    /// Entry point. Must not reference any Siemens.Engineering type — the resolver has to be registered first
    /// (V21 docs: "Connecting to the TIA Portal"). All Openness code lives in <see cref="App"/>.
    /// </summary>
    internal static class Program
    {
        private static readonly string OpennessFolder =
            Environment.GetEnvironmentVariable("TIASBOM_API_DIR")
            ?? @"C:\Program Files\Siemens\Automation\Portal V21\PublicAPI\V21\net48";

        static Program()
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private static int Main(string[] args)
        {
            try
            {
                return App.Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static Assembly OnAssemblyResolve(object sender, ResolveEventArgs args)
        {
            var requested = new AssemblyName(args.Name);
            string filePath = Path.Combine(OpennessFolder, requested.Name + ".dll");

            if (!requested.Name.StartsWith("Siemens.Engineering", StringComparison.Ordinal) || !File.Exists(filePath))
                return null;

            Assembly loaded = Assembly.LoadFrom(filePath);
            if (requested.FullName != loaded.GetName().FullName)
                throw new FileNotFoundException("TIA Portal Openness version does not match", filePath);
            return loaded;
        }
    }
}
