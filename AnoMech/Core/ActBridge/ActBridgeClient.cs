using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AnoMech.Core.ActBridge;

internal interface IActLineSink
{
    // False while nobody listens, so callers can skip building lines.
    bool Connected { get; }

    // Never blocks; a line is dropped when not connected or the queue is full.
    void Send(string line);
}

// The client half of ActBridge's loopback protocol: one UTF-8 line per message. Everything socket-side
// runs on a background task, so the framework thread only ever queues.
internal sealed class ActBridgeClient : IActLineSink, IDisposable
{
    public const int DefaultPort = 10512;
    private const int QueueCapacity = 4096;
    private static readonly TimeSpan GoodbyeTimeout = TimeSpan.FromMilliseconds(500);

    private readonly IPEndPoint endpoint;
    private readonly string hello;
    private readonly TimeSpan retryDelay;
    private readonly Channel<string> queue = Channel.CreateBounded<string>(
        new BoundedChannelOptions(QueueCapacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = false });
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private volatile bool connected;
    private volatile bool disposed;

    public bool Connected => connected;

    public ActBridgeClient(string version, int port = DefaultPort, TimeSpan? retryDelay = null)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, port);
        hello = $"#hello|{PluginIdentity.InternalName}|{version}";
        this.retryDelay = retryDelay ?? TimeSpan.FromSeconds(2);
        worker = Task.Run(Run);
    }

    public void Send(string line)
    {
        try
        {
            if (connected) queue.Writer.TryWrite(line);
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        stop.Cancel();
        try { worker.Wait(GoodbyeTimeout + GoodbyeTimeout); }
        catch (Exception) { }
        stop.Dispose();
    }

    private async Task Run()
    {
        var token = stop.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var client = new TcpClient { NoDelay = true };
                await client.ConnectAsync(endpoint, token).ConfigureAwait(false);
                await Pump(client, token).ConfigureAwait(false);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                return;
            }
            finally
            {
                connected = false;
            }

            try { await Task.Delay(retryDelay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task Pump(TcpClient client, CancellationToken token)
    {
        await using var stream = client.GetStream();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false };
        while (queue.Reader.TryRead(out _)) { }
        await writer.WriteLineAsync(hello).ConfigureAwait(false);
        await writer.FlushAsync(token).ConfigureAwait(false);
        connected = true;
        // The server never writes; a read that returns means it hung up.
        var hangup = stream.ReadAsync(new byte[16], token).AsTask();
        using var connection = CancellationTokenSource.CreateLinkedTokenSource(token);
        try
        {
            while (true)
            {
                var more = queue.Reader.WaitToReadAsync(connection.Token).AsTask();
                if (await Task.WhenAny(more, hangup).ConfigureAwait(false) == hangup || !await more.ConfigureAwait(false)) return;
                while (queue.Reader.TryRead(out var line)) await writer.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
                await writer.FlushAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            connected = false;
            while (queue.Reader.TryRead(out var line)) await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.WriteLineAsync("#bye").ConfigureAwait(false);
            using var goodbye = new CancellationTokenSource(GoodbyeTimeout);
            await writer.FlushAsync(goodbye.Token).ConfigureAwait(false);
            throw;
        }
        finally
        {
            connection.Cancel();
        }
    }
}
