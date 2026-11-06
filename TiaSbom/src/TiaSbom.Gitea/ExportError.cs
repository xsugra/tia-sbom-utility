using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Threading.Tasks;

namespace TiaSbom.Gitea
{
    /// <summary>
    /// Turns a failure into what an ordinary user needs: what happened and what to do next.
    /// The technical details stay in the log.
    /// </summary>
    public static class ExportError
    {
        private const int Locked = 423;

        public static string Describe(Exception error, string giteaUrl)
        {
            string server = string.IsNullOrEmpty(giteaUrl) ? "the Gitea server" : giteaUrl;
            switch (error)
            {
                case TaskCanceledException _:
                    return $"{server} did not answer in time. The SBOM was not saved.\n\nTry again. If it happens again, contact the administrator.";

                case HttpRequestException _:
                case WebException _:
                case SocketException _:
                    return $"Cannot connect to {server}. The SBOM was not saved.\n\nCheck the network connection and try again. If it happens again, contact the administrator.";

                case FileNotFoundException _:
                    return "TiaSbom is not set up on this computer: its configuration file is missing.\n\nContact the administrator.";

                case InvalidDataException _:
                case FormatException _:
                    return "The TiaSbom configuration is damaged or incomplete.\n\nContact the administrator.\n\n" + error.Message;

                case GiteaException gitea:
                    return Describe(gitea, server);

                case UnauthorizedAccessException _:
                case SecurityException _:
                    return "TiaSbom has no permission to do this on this computer.\n\nContact the administrator.";

                default:
                    return error.Message;
            }
        }

        private static string Describe(GiteaException error, string server)
        {
            int status = (int)error.StatusCode;
            if (status == Locked)
                return "This Gitea project is archived and read-only. The SBOM was not saved.\n\nAsk the administrator to unarchive it.";
            if (error.StatusCode == HttpStatusCode.NotFound)
                return "The Gitea organization was not found, or your account cannot see it. The SBOM was not saved.\n\nContact the administrator.";
            if (error.StatusCode == HttpStatusCode.Forbidden)
                return "Your Gitea account may not write to this project. The SBOM was not saved.\n\nContact the administrator.";
            if (error.IsConflict)
                return "Someone else was exporting to this project at the same moment. The SBOM was not saved.\n\nTry again.";
            if (status >= 500)
                return $"{server} reported an internal error. The SBOM was not saved.\n\nTry again later. If it happens again, contact the administrator.";
            return error.Message;
        }
    }
}
