using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TiaSbom.Tests
{
    /// <summary>
    /// Minimal HTTP server on 127.0.0.1 that answers every request the same way, or never answers.
    /// Plain sockets, so it behaves the same on .NET Framework and .NET 8 and needs no admin rights.
    /// </summary>
    public sealed class FakeServer : IDisposable
    {
        private readonly TcpListener _listener = new TcpListener(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();

        private FakeServer(int status, string contentType, string body, bool hang)
        {
            _listener.Start();
            Task.Run(() => AcceptLoop(status, contentType, body, hang));
        }

        public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

        public static FakeServer Answer(int status, string body, string contentType = "application/json") =>
            new FakeServer(status, contentType, body, hang: false);

        /// <summary>Accepts the connection and never answers, like an overloaded or half-dead server.</summary>
        public static FakeServer Hang() => new FakeServer(0, null, null, hang: true);

        /// <summary>A port where nothing listens: connection refused.</summary>
        public static string ClosedPortUrl()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return $"http://127.0.0.1:{port}";
        }

        private async Task AcceptLoop(int status, string contentType, string body, bool hang)
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false); }
                catch (Exception) { return; }
                _ = Task.Run(() => Serve(client, status, contentType, body, hang));
            }
        }

        private async Task Serve(TcpClient client, int status, string contentType, string body, bool hang)
        {
            using (client)
            {
                NetworkStream stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.ASCII);
                string line;
                while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync().ConfigureAwait(false))) { }

                if (hang)
                {
                    try { await Task.Delay(Timeout.Infinite, _stop.Token).ConfigureAwait(false); } catch (TaskCanceledException) { }
                    return;
                }

                byte[] payload = Encoding.UTF8.GetBytes(body ?? "");
                string head = $"HTTP/1.1 {status} X\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n";
                byte[] header = Encoding.ASCII.GetBytes(head);
                await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Stop();
        }
    }
}
