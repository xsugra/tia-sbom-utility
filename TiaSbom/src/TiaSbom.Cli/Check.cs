using System;
using System.Collections.Generic;
using TiaSbom.Gitea;

namespace TiaSbom.Cli
{
    /// <summary>
    /// tiasbom --check [--config path]: checks configuration, network, token and permissions without TIA Portal.
    /// Exit code 0 when every step passed, 3 otherwise. Never prints the token.
    /// </summary>
    internal static class Check
    {
        public static int Run(string[] args)
        {
            string config = Value(args, "--config");
            List<CheckStep> steps = ConnectionCheck.RunAsync(
                () => GiteaSettings.Load(config),
                () =>
                {
                    string fromEnvironment = Environment.GetEnvironmentVariable("GITEA_TOKEN");
                    if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;
                    return TokenStore.TryLoad(out string token) ? token : null;
                }).GetAwaiter().GetResult();

            Console.WriteLine($"TiaSbom connection check, {Environment.UserName}@{Environment.MachineName}");
            foreach (CheckStep step in steps)
            {
                Console.ForegroundColor = step.Ok ? ConsoleColor.Green : ConsoleColor.Red;
                Console.Write(step.Ok ? "  OK      " : "  FAILED  ");
                Console.ResetColor();
                Console.WriteLine($"{step.Name}: {(step.Code != null ? step.Code + " " : "")}{step.Detail}");
            }

            bool ok = steps.TrueForAll(s => s.Ok);
            Console.WriteLine(ok ? "RESULT: OK, exports to Gitea will work." : "RESULT: FAILED, see the first failed step.");
            return ok ? 0 : 3;
        }

        private static string Value(string[] args, string name)
        {
            int i = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
