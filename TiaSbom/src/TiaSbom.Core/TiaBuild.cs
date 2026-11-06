using System.Text.RegularExpressions;

namespace TiaSbom.Core
{
    /// <summary>
    /// Openness API this binary was built against vs. the TIA Portal version of a project.
    /// They differ on purpose: a V17 build also runs on TIA V18 - V20, so the API must never be reported as the TIA version.
    /// </summary>
    public static class TiaBuild
    {
#if TIA_API_V16
        public const string Api = "V16";
#elif TIA_API_V17
        public const string Api = "V17";
#elif TIA_API_V19
        public const string Api = "V19";
#elif TIA_API_V21
        public const string Api = "V21";
#else
#error Unsupported TiaApi. Supported values: V16, V17, V19, V21.
#endif

        /// <summary>
        /// TIA version from the project file extension: .ap19, .zap19, .als19, .ap15_1. Returns null when it is not a TIA file.
        /// </summary>
        public static string VersionFromPath(string path)
        {
            Match m = Regex.Match(path ?? string.Empty, @"\.(?:z?ap|z?als|aps)(\d+)(?:_(\d+))?$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return "V" + m.Groups[1].Value + (m.Groups[2].Success ? "." + m.Groups[2].Value : string.Empty);
        }
    }
}
