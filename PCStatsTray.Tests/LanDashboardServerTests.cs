using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PCStatsTray.Tests;

[TestClass]
public class LanDashboardServerTests
{
    [TestMethod]
    public async Task ApplyConfig_SwitchesAccessAndServesTheSameCachedSnapshot()
    {
        using var portAllocator = new TcpListener(IPAddress.Loopback, 0);
        portAllocator.Start();
        int port = ((IPEndPoint)portAllocator.LocalEndpoint).Port;
        portAllocator.Stop();

        IPAddress alternateAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
            .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            ?? IPAddress.Parse("127.0.0.2");

        string loopback = $"http://127.0.0.1:{port}";
        string alternate = $"http://{alternateAddress}:{port}";
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false })
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        using var server = new LanDashboardServer();
        var config = new OverlayConfig { DashboardAccess = DashboardAccessMode.ThisPcOnly, PhoneDashboardPort = port };
        var snapshot = DashboardSnapshotBuilder.Build(
            new Dictionary<string, MetricReading>
            {
                ["CpuTemp"] = MetricReading.Scalar(71.75f, "°C", "0")
            },
            refreshIntervalMs: 5000);
        server.UpdateSnapshot(snapshot);
        server.ApplyConfig(config);
        Assert.IsTrue(server.IsRunning, server.LastError);
        Assert.IsNull(server.GetLanUrl());

        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => http.GetStringAsync($"{alternate}/api/metrics"));
        Assert.IsTrue((await http.GetStringAsync($"{loopback}/")).Contains("dashboard.js", StringComparison.Ordinal));

        string localJson = await http.GetStringAsync($"{loopback}/api/metrics");
        using var result = JsonDocument.Parse(localJson);
        var root = result.RootElement;
        var metric = root.GetProperty("metrics")[0];
        Assert.AreEqual(snapshot.GeneratedAtUtc, root.GetProperty("generatedAtUtc").GetDateTimeOffset());
        Assert.AreEqual(5000, root.GetProperty("refreshIntervalMs").GetInt32());
        Assert.AreEqual("72°C", metric.GetProperty("value").GetString());
        Assert.AreEqual(71.75, metric.GetProperty("numericValue").GetDouble());
        Assert.AreEqual("°C", metric.GetProperty("unit").GetString());
        Assert.IsTrue(metric.GetProperty("defaultVisible").GetBoolean());

        config.DashboardAccess = DashboardAccessMode.LocalNetwork;
        server.ApplyConfig(config);
        Assert.IsTrue(server.IsRunning, server.LastError);
        var clients = await Task.WhenAll(http.GetStringAsync($"{loopback}/api/metrics"),
                                         http.GetStringAsync($"{alternate}/api/metrics"));
        Assert.AreEqual(clients[0], clients[1]);
        using var lanResult = JsonDocument.Parse(clients[1]);
        Assert.AreEqual(snapshot.GeneratedAtUtc, lanResult.RootElement.GetProperty("generatedAtUtc").GetDateTimeOffset());

        config.DashboardAccess = DashboardAccessMode.ThisPcOnly;
        server.ApplyConfig(config);
        Assert.IsTrue(server.IsRunning, server.LastError);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => http.GetStringAsync($"{alternate}/api/metrics"));
        Assert.AreEqual(localJson, await http.GetStringAsync($"{loopback}/api/metrics"));

        config.DashboardAccess = DashboardAccessMode.Off;
        server.ApplyConfig(config);
        Assert.IsFalse(server.IsRunning);
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => http.GetStringAsync($"{loopback}/api/metrics"));
    }
}
