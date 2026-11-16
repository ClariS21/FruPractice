using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FruPractice.ActBridge
{
    internal sealed class SocketServer : IDisposable
    {
        private readonly Action<string> onLine;
        private readonly Action<bool> onConnectionChanged;
        private readonly Action<string> onError;
        private readonly TcpListener listener = new TcpListener(IPAddress.Loopback, 10512);
        private readonly Thread thread;
        private readonly object gate = new object();
        private TcpClient currentClient;
        private volatile bool stopping;
        private readonly ManualResetEventSlim stopSignal = new ManualResetEventSlim(false);
        private const int FirstRetryMilliseconds = 1000;
        private const int MaxRetryMilliseconds = 30000;

        public SocketServer(Action<string> onLine, Action<bool> onConnectionChanged, Action<string> onError)
        {
            this.onLine = onLine;
            this.onConnectionChanged = onConnectionChanged;
            this.onError = onError;
            thread = new Thread(Run) { IsBackground = true, Name = "FruPractice ActBridge socket" };
        }

        public void Start()
        {
            listener.Start();
            thread.Start();
        }

        private void Run()
        {
            var retryMilliseconds = FirstRetryMilliseconds;
            while (!stopping)
            {
                var connected = false;
                var failed = false;
                try
                {
                    using (var client = listener.AcceptTcpClient())
                    {
                        retryMilliseconds = FirstRetryMilliseconds;
                        lock (gate) currentClient = client;
                        if (stopping) break;
                        var remote = client.Client.RemoteEndPoint as IPEndPoint;
                        if (remote == null || !IPAddress.IsLoopback(remote.Address)) continue;

                        connected = true;
                        onConnectionChanged(true);
                        using (var reader = new StreamReader(client.GetStream(), new UTF8Encoding(false, true)))
                        {
                            string line;
                            while (!stopping && (line = reader.ReadLine()) != null)
                                onLine(line);
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed = true;
                    if (!stopping) onError(ex.Message);
                }
                finally
                {
                    lock (gate) currentClient = null;
                    if (connected && !stopping) onConnectionChanged(false);
                }
                // A persistent error (a dead listener, say) would otherwise spin; back off 1 s, doubling to 30 s.
                if (failed && !stopping)
                {
                    stopSignal.Wait(retryMilliseconds);
                    retryMilliseconds = Math.Min(retryMilliseconds * 2, MaxRetryMilliseconds);
                }
            }
        }

        public void Dispose()
        {
            stopping = true;
            stopSignal.Set();
            listener.Stop();
            lock (gate) currentClient?.Close();
            if (thread.IsAlive) thread.Join(1000);
        }
    }
}
