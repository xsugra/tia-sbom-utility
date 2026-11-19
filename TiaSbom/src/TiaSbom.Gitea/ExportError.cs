using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security;
using System.Security.Authentication;
using System.Threading.Tasks;

namespace TiaSbom.Gitea
{
    /// <summary>What an ordinary user is told about one failure. <see cref="Code"/> is listed in docs/11-chybove-hlasky.md.</summary>
    public sealed class ExportProblem
    {
        public ExportProblem(string code, string summary, string action)
        {
            Code = code;
            Summary = summary;
            Action = action;
        }

        public string Code { get; }
        /// <summary>What happened, in one or two sentences.</summary>
        public string Summary { get; }
        /// <summary>What the user does next.</summary>
        public string Action { get; }

        public override string ToString() => $"{Summary}\n\n{Action}\n\nError code: {Code}";
    }

    /// <summary>
    /// Turns a failure into what an ordinary user needs: what happened and what to do next.
    /// The technical details stay in the log. Every message carries a code, so colleagues can look it up.
    /// </summary>
    public static class ExportError
    {
        private const string NotSaved = "The SBOM was not saved.";
        private const string Admin = "If it happens again, contact the administrator.";

        public static string Describe(Exception error, string giteaUrl, string logPath = null)
        {
            string text = Explain(error, giteaUrl).ToString();
            return logPath == null ? text : text + "\nDetails: " + logPath;
        }

        public static ExportProblem Explain(Exception error, string giteaUrl)
        {
            string server = string.IsNullOrEmpty(giteaUrl) ? "the Gitea server" : giteaUrl;
            error = Unwrap(error);

            if (error is GiteaException gitea) return Explain(gitea, server);

            // Certificate problems arrive wrapped in connection errors, so they are looked for first.
            if (Find<AuthenticationException>(error) != null || IsWebStatus(error, WebExceptionStatus.TrustFailure, WebExceptionStatus.SecureChannelFailure))
                return new ExportProblem("TS-TLS-01",
                    $"The secure connection to {server} was refused: this computer does not trust the server certificate. {NotSaved}",
                    "Contact the administrator. The company certificate authority has to be installed on this computer.");

            switch (error)
            {
                case TaskCanceledException _:
                case TimeoutException _:
                    return new ExportProblem("TS-NET-04",
                        $"{server} did not answer in time. {NotSaved}",
                        "Check that you are connected to the company network and try again. " + Admin);

                case FileNotFoundException _:
                    return new ExportProblem("TS-CFG-01",
                        "TiaSbom is not set up on this computer: its configuration file is missing.",
                        "Contact the administrator. The installer creates the file.");

                case InvalidDataException _:
                    return new ExportProblem("TS-CFG-02",
                        "The TiaSbom configuration is damaged or incomplete: " + error.Message,
                        "Contact the administrator. Running the installer again repairs it.");

                case UnauthorizedAccessException _:
                case SecurityException _:
                    return new ExportProblem("TS-SEC-01",
                        "TiaSbom has no permission to do this on this computer.",
                        "Contact the administrator.");
            }

            SocketException socket = Find<SocketException>(error);
            if (socket != null || error is HttpRequestException || error is WebException)
                return Connection(socket, error, server);

            return new ExportProblem("TS-UNK-01", error.Message, Admin);
        }

        private static ExportProblem Connection(SocketException socket, Exception error, string server)
        {
            SocketError code = socket?.SocketErrorCode ?? SocketError.SocketError;
            if (IsWebStatus(error, WebExceptionStatus.NameResolutionFailure) ||
                code == SocketError.HostNotFound || code == SocketError.NoData || code == SocketError.TryAgain)
                return new ExportProblem("TS-NET-01",
                    $"The server name in {server} was not found. {NotSaved}",
                    "Check that you are connected to the company network. " + Admin);

            if (code == SocketError.ConnectionRefused)
                return new ExportProblem("TS-NET-02",
                    $"{server} is reachable, but the Gitea service does not answer. {NotSaved}",
                    "The Gitea server is probably stopped. Contact the administrator.");

            if (code == SocketError.TimedOut || code == SocketError.HostUnreachable || code == SocketError.NetworkUnreachable)
                return new ExportProblem("TS-NET-04",
                    $"{server} cannot be reached. {NotSaved}",
                    "Check that you are connected to the company network and try again. " + Admin);

            return new ExportProblem("TS-NET-03",
                $"Cannot connect to {server}. {NotSaved}",
                "Check the network connection and try again. " + Admin);
        }

        private static ExportProblem Explain(GiteaException error, string server)
        {
            switch (error.Kind)
            {
                case GiteaProblem.NotGitea:
                    return new ExportProblem("TS-API-01",
                        $"{server} does not answer like a Gitea server. {NotSaved}",
                        "The server address in the TiaSbom configuration is probably wrong. Contact the administrator.");
                case GiteaProblem.MissingOrganization:
                    return new ExportProblem("TS-GIT-01",
                        $"The Gitea organization was not found, or your account cannot see it. {NotSaved}",
                        "Contact the administrator. Your Gitea account has to be a member of the SBOM organization.");
                case GiteaProblem.Archived:
                    return new ExportProblem("TS-GIT-03",
                        $"This Gitea project is archived and read-only. {NotSaved}",
                        "Ask the administrator to unarchive it.");
                case GiteaProblem.InvalidContent:
                    return new ExportProblem("TS-GIT-07",
                        $"The Gitea project contains a file that is not a valid SBOM. {NotSaved}",
                        "Contact the administrator. " + error.Message);
            }

            int status = (int)error.StatusCode;
            if (error.IsMissingScope)
                return new ExportProblem("TS-AUTH-02",
                    $"Your Gitea token is valid, but it lacks permissions. {NotSaved}",
                    "Create a new token in Gitea (Settings > Applications) with organization: Read and Write and repository: Read and Write, then export again.");
            if (error.StatusCode == HttpStatusCode.Unauthorized)
                return new ExportProblem("TS-AUTH-01",
                    $"Gitea did not accept your token. {NotSaved}",
                    "Export again and enter a valid token.");
            if (status == 423)
                return Explain(new GiteaException(error.StatusCode, error.Message, GiteaProblem.Archived), server);
            if (error.StatusCode == HttpStatusCode.NotFound)
                return new ExportProblem("TS-GIT-06",
                    $"Gitea answered 'not found'. {NotSaved}",
                    "Contact the administrator. " + Admin);
            // Before 403: Gitea reports a lost commit race as 403 too.
            if (error.IsConflict)
                return new ExportProblem("TS-GIT-04",
                    $"Someone else was exporting to this project at the same moment. {NotSaved}",
                    "Try again.");
            if (error.StatusCode == HttpStatusCode.Forbidden)
                return new ExportProblem("TS-GIT-02",
                    $"Your Gitea account may not write to this project. {NotSaved}",
                    "Ask the administrator for write access to the SBOM organization.");
            if (status >= 500)
                return new ExportProblem("TS-GIT-05",
                    $"{server} reported an internal error. {NotSaved}",
                    "Try again later. " + Admin);
            return new ExportProblem("TS-GIT-06",
                $"Gitea refused the request ({status}). {NotSaved}",
                Admin);
        }

        /// <summary>A synchronous wait wraps the real failure in AggregateException.</summary>
        private static Exception Unwrap(Exception error)
        {
            while (error is AggregateException aggregate && aggregate.InnerExceptions.Count == 1) error = aggregate.InnerException;
            return error;
        }

        private static T Find<T>(Exception error) where T : Exception
        {
            for (Exception e = error; e != null; e = e.InnerException)
                if (e is T match) return match;
            return null;
        }

        private static bool IsWebStatus(Exception error, params WebExceptionStatus[] statuses)
        {
            WebException web = Find<WebException>(error);
            return web != null && Array.IndexOf(statuses, web.Status) >= 0;
        }
    }
}
