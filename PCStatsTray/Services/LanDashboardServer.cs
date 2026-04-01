using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PCStatsTray
{
    internal sealed class LanDashboardServer : IDisposable
    {
        private const int MaxRequestBytes = 8192;
        private const string EmbeddedAssetPrefix = "PCStatsTray.Web.";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        private readonly object _sync = new();
        private DashboardSnapshot _snapshot = new();
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _acceptLoopTask;
        private int _port = 4587;

        public bool IsRunning { get; private set; }
        public string? LastError { get; private set; }
        public int Port => _port;

        public void UpdateSnapshot(DashboardSnapshot snapshot)
        {
            lock (_sync)
            {
                _snapshot = snapshot;
            }
        }

        public void ApplyConfig(OverlayConfig config)
        {
            config.NormalizeDashboard();

            if (!config.PhoneDashboardEnabled)
            {
                Stop();
                return;
            }

            if (IsRunning && _port == config.PhoneDashboardPort)
            {
                return;
            }

            Restart(config.PhoneDashboardPort);
        }

        public string GetLocalUrl()
        {
            return $"http://localhost:{_port}/";
        }

        public string? GetLanUrl()
        {
            string? ip = GetPreferredLanIp();
            return ip == null ? null : $"http://{ip}:{_port}/";
        }

        public void Stop()
        {
            TcpListener? listener;
            CancellationTokenSource? cts;
            Task? acceptLoopTask;

            lock (_sync)
            {
                listener = _listener;
                cts = _cts;
                acceptLoopTask = _acceptLoopTask;
                _listener = null;
                _cts = null;
                _acceptLoopTask = null;
                IsRunning = false;
                LastError = null;
            }

            try
            {
                cts?.Cancel();
                listener?.Stop();
                acceptLoopTask?.Wait(500);
            }
            catch
            {
            }
            finally
            {
                cts?.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private void Restart(int port)
        {
            Stop();

            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();

                var cts = new CancellationTokenSource();
                var acceptLoopTask = Task.Run(() => AcceptLoopAsync(listener, cts.Token));

                lock (_sync)
                {
                    _listener = listener;
                    _cts = cts;
                    _acceptLoopTask = acceptLoopTask;
                    _port = port;
                    IsRunning = true;
                    LastError = null;
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _port = port;
                    IsRunning = false;
                    LastError = ex.Message;
                }
            }
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    IsRunning = false;
                    LastError = ex.Message;
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            using (client)
            using (NetworkStream stream = client.GetStream())
            {
                try
                {
                    string? requestLine = await ReadRequestLineAsync(stream, cancellationToken);
                    if (string.IsNullOrWhiteSpace(requestLine))
                    {
                        return;
                    }

                    string[] parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteResponseAsync(stream, "405 Method Not Allowed", "text/plain; charset=utf-8", "Method Not Allowed", cancellationToken);
                        return;
                    }

                    if (!Uri.TryCreate("http://pcstats.local" + parts[1], UriKind.Absolute, out Uri? uri))
                    {
                        await WriteResponseAsync(stream, "400 Bad Request", "text/plain; charset=utf-8", "Bad Request", cancellationToken);
                        return;
                    }

                    string path = uri.AbsolutePath;
                    if (path == "/" || path.Equals("/index.html", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteAssetResponseAsync(stream, "dashboard.html", cancellationToken);
                        return;
                    }

                    if (path.Equals("/api/metrics", StringComparison.OrdinalIgnoreCase))
                    {
                        string payload = JsonSerializer.Serialize(BuildResponse(), JsonOptions);
                        await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", payload, cancellationToken);
                        return;
                    }

                    if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
                    {
                        await WriteResponseAsync(stream, "200 OK", "application/json; charset=utf-8", "{\"status\":\"ok\"}", cancellationToken);
                        return;
                    }

                    if (TryResolveAssetPath(path, out string? assetPath, out string contentType))
                    {
                        await WriteAssetResponseAsync(stream, assetPath!, contentType, cancellationToken);
                        return;
                    }

                    await WriteResponseAsync(stream, "404 Not Found", "text/plain; charset=utf-8", "Not Found", cancellationToken);
                }
                catch
                {
                }
            }
        }

        private DashboardApiResponse BuildResponse()
        {
            DashboardSnapshot snapshot;
            lock (_sync)
            {
                snapshot = _snapshot;
            }

            return new DashboardApiResponse
            {
                GeneratedAtUtc = snapshot.GeneratedAtUtc,
                MachineName = snapshot.MachineName,
                RefreshIntervalMs = snapshot.RefreshIntervalMs,
                LocalUrl = GetLocalUrl(),
                LanUrl = GetLanUrl(),
                Metrics = snapshot.Metrics
            };
        }

        private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[MaxRequestBytes];
            int totalRead = 0;

            while (totalRead < buffer.Length)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                totalRead += bytesRead;
                if (HasHeaderTerminator(buffer, totalRead))
                {
                    break;
                }
            }

            if (totalRead == 0)
            {
                return null;
            }

            string requestText = Encoding.ASCII.GetString(buffer, 0, totalRead);
            using var reader = new StringReader(requestText);
            return reader.ReadLine();
        }

        private static bool HasHeaderTerminator(byte[] buffer, int length)
        {
            for (int i = 3; i < length; i++)
            {
                if (buffer[i - 3] == '\r' &&
                    buffer[i - 2] == '\n' &&
                    buffer[i - 1] == '\r' &&
                    buffer[i] == '\n')
                {
                    return true;
                }
            }

            return false;
        }

        private static async Task WriteAssetResponseAsync(NetworkStream stream, string assetFileName, CancellationToken cancellationToken)
        {
            string contentType = GetContentType(assetFileName);
            await WriteAssetResponseAsync(stream, assetFileName, contentType, cancellationToken);
        }

        private static async Task WriteAssetResponseAsync(NetworkStream stream, string assetPath, string contentType, CancellationToken cancellationToken)
        {
            if (!TryLoadAssetBytes(assetPath, out byte[]? bodyBytes))
            {
                string missingBody = BuildMissingAssetHtml(Path.GetFileName(assetPath));
                await WriteResponseAsync(stream, "404 Not Found", "text/html; charset=utf-8", missingBody, cancellationToken);
                return;
            }

            await WriteBinaryResponseAsync(stream, "200 OK", contentType, bodyBytes!, cancellationToken);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, string status, string contentType, string body, CancellationToken cancellationToken)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
            await WriteBinaryResponseAsync(stream, status, contentType, bodyBytes, cancellationToken);
        }

        private static async Task WriteBinaryResponseAsync(NetworkStream stream, string status, string contentType, byte[] bodyBytes, CancellationToken cancellationToken)
        {
            string headers =
                $"HTTP/1.1 {status}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes.AsMemory(0, headerBytes.Length), cancellationToken);
            await stream.WriteAsync(bodyBytes.AsMemory(0, bodyBytes.Length), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        private static string GetWebRoot()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Web");
        }

        private static bool TryLoadAssetBytes(string assetPath, out byte[]? bodyBytes)
        {
            string directPath = assetPath;
            if (!Path.IsPathRooted(directPath))
            {
                directPath = Path.Combine(GetWebRoot(), assetPath);
            }

            if (File.Exists(directPath))
            {
                bodyBytes = File.ReadAllBytes(directPath);
                return true;
            }

            string relativePath = Path.GetRelativePath(GetWebRoot(), directPath);
            if (relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                relativePath = assetPath.TrimStart('/', '\\');
            }

            string resourceName = EmbeddedAssetPrefix + relativePath
                .Replace(Path.DirectorySeparatorChar, '.')
                .Replace(Path.AltDirectorySeparatorChar, '.');

            using Stream? resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (resourceStream == null)
            {
                bodyBytes = null;
                return false;
            }

            using var memoryStream = new MemoryStream();
            resourceStream.CopyTo(memoryStream);
            bodyBytes = memoryStream.ToArray();
            return true;
        }

        private static bool TryResolveAssetPath(string requestPath, out string? assetPath, out string contentType)
        {
            assetPath = null;
            contentType = "application/octet-stream";

            if (string.IsNullOrWhiteSpace(requestPath) || requestPath.Contains("..", StringComparison.Ordinal))
            {
                return false;
            }

            string relativePath = requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            string webRoot = Path.GetFullPath(GetWebRoot());
            string resolvedPath = Path.GetFullPath(Path.Combine(webRoot, relativePath));
            if (!resolvedPath.StartsWith(webRoot, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string extension = Path.GetExtension(resolvedPath);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return false;
            }

            contentType = GetContentType(resolvedPath);
            assetPath = resolvedPath;
            return true;
        }

        private static string GetContentType(string assetPath)
        {
            return Path.GetExtension(assetPath).ToLowerInvariant() switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "application/javascript; charset=utf-8",
                ".woff2" => "font/woff2",
                ".woff" => "font/woff",
                ".ttf" => "font/ttf",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream"
            };
        }

        private static string BuildMissingAssetHtml(string assetFileName)
        {
            return
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>Missing Dashboard Asset</title></head>" +
                "<body style=\"font-family:Segoe UI,sans-serif;padding:24px;background:#0b0f17;color:#eef2ff\">" +
                $"<h1>Missing asset</h1><p>The dashboard asset <strong>{assetFileName}</strong> was not found in the app output folder.</p>" +
                "<p>Rebuild the app so the <code>Web</code> assets are copied next to the executable.</p></body></html>";
        }

        private static string? GetPreferredLanIp()
        {
            string? fallbackAddress = null;

            foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    networkInterface.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                IPInterfaceProperties properties = networkInterface.GetIPProperties();
                bool hasGateway = properties.GatewayAddresses.Any(gateway =>
                    gateway?.Address?.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.Any.Equals(gateway.Address) &&
                    !IPAddress.None.Equals(gateway.Address));

                foreach (UnicastIPAddressInformation address in properties.UnicastAddresses)
                {
                    if (address.Address.AddressFamily != AddressFamily.InterNetwork ||
                        IPAddress.IsLoopback(address.Address))
                    {
                        continue;
                    }

                    string candidate = address.Address.ToString();
                    if (IsLinkLocalIpv4(candidate))
                    {
                        fallbackAddress ??= candidate;
                        continue;
                    }

                    if (hasGateway && IsPrivateIpv4(candidate))
                    {
                        return candidate;
                    }

                    fallbackAddress ??= candidate;
                }
            }

            return fallbackAddress;
        }

        private static bool IsLinkLocalIpv4(string address)
        {
            return address.StartsWith("169.254.", StringComparison.Ordinal);
        }

        private static bool IsPrivateIpv4(string address)
        {
            if (address.StartsWith("10.", StringComparison.Ordinal) ||
                address.StartsWith("192.168.", StringComparison.Ordinal))
            {
                return true;
            }

            if (!address.StartsWith("172.", StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = address.Split('.');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int secondOctet))
            {
                return false;
            }

            return secondOctet is >= 16 and <= 31;
        }
    }
}
