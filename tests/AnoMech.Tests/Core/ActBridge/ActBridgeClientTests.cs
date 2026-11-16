using System.Net;
using System.Net.Sockets;
using AnoMech.Core.ActBridge;

namespace AnoMech.Tests;

public class ActBridgeClientTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Test]
    public async Task SendsHelloThenQueuedLinesThenByeOnDispose()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var client = new ActBridgeClient("1.2.3", port, TimeSpan.FromMilliseconds(50));
        using var server = await listener.AcceptTcpClientAsync().WaitAsync(Patience);
        using var reader = new StreamReader(server.GetStream());

        Assert.That(await reader.ReadLineAsync().WaitAsync(Patience), Is.EqualTo("#hello|FruPractice|1.2.3"));
        SpinWait.SpinUntil(() => client.Connected, Patience);
        client.Send("20|a");
        client.Send("26|b");
        Assert.That(await reader.ReadLineAsync().WaitAsync(Patience), Is.EqualTo("20|a"));
        Assert.That(await reader.ReadLineAsync().WaitAsync(Patience), Is.EqualTo("26|b"));

        client.Dispose();
        Assert.That(await reader.ReadLineAsync().WaitAsync(Patience), Is.EqualTo("#bye"));
    }

    [Test]
    public void DropsLinesWithoutBlockingWhenNobodyListens()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        using var client = new ActBridgeClient("1", port, TimeSpan.FromMilliseconds(50));
        for (var i = 0; i < 100_000; i++) client.Send("20|x");

        Assert.That(client.Connected, Is.False);
    }

    [Test]
    public async Task ReconnectsAfterTheServerDrops()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var client = new ActBridgeClient("1", port, TimeSpan.FromMilliseconds(50));

        using (var first = await listener.AcceptTcpClientAsync().WaitAsync(Patience))
            Assert.That(await new StreamReader(first.GetStream()).ReadLineAsync().WaitAsync(Patience), Does.StartWith("#hello"));

        using var second = await listener.AcceptTcpClientAsync().WaitAsync(Patience);
        Assert.That(await new StreamReader(second.GetStream()).ReadLineAsync().WaitAsync(Patience), Does.StartWith("#hello"));
    }
}
