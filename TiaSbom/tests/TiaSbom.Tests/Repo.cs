using System;
using System.IO;

namespace TiaSbom.Tests
{
    /// <summary>Finds files of the repository from the test output folder, whatever the build layout is.</summary>
    internal static class Repo
    {
        /// <summary>The folder that holds TiaSbom.sln, or null when the tests run outside the repository.</summary>
        public static string Solution
        {
            get
            {
                for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
                    if (File.Exists(Path.Combine(dir.FullName, "TiaSbom.sln"))) return dir.FullName;
                return null;
            }
        }

        public static string File_(params string[] parts) =>
            Solution == null ? null : Path.Combine(Solution, Path.Combine(parts));
    }
}
