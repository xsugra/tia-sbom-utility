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
                // --check needs no Openness at all, so it runs before anything could load Siemens assemblies.
                if (Array.FindIndex(args, a => string.Equals(a, "--check", StringComparison.OrdinalIgnoreCase)) >= 0)
                    return Check.Run(args);

                // --help and --login work without TIA Portal.
                if (args.Length > 0 && Array.FindIndex(args, IsLocalCommand) < 0)
                    _opennessFolder = OpennessLocator.FindApiDir(args);
                return App.Run(args);
            }
            catch (Exception ex)
            {
                // Same codes as the Add-In, so docs/11-chybove-hlasky.md covers both.
                Gitea.ExportProblem problem = Gitea.ExportError.Explain(ex, null);
                Console.Error.WriteLine($"ERROR {problem.Code}: {ex.Message}");
                if (problem.Code != "TS-UNK-01") Console.Error.WriteLine(problem.Action);
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
