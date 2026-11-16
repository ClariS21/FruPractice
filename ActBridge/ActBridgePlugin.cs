using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Advanced_Combat_Tracker;

namespace FruPractice.ActBridge
{
    public class ActBridgePlugin : IActPluginV1
    {
        private const string TestFields = "40000001|Oracle of Darkness|9D4A|Ultimate Relativity|E0000000||10.000|100.00|100.00|0.00|3.14";
        private readonly LineInjector injector = new LineInjector();
        private readonly Queue<string> logLines = new Queue<string>();
        private TabPage tab;
        private Label pluginStatus;
        private Label connectionStatus;
        private Label counters;
        private TextBox logBox;
        private System.Windows.Forms.Timer counterTimer;
        private SocketServer server;
        private int received;
        private int injected;
        private int failed;
        private bool stopping;

        public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
        {
            try
            {
                tab = pluginScreenSpace;
                pluginStatus = pluginStatusText;
                stopping = false;
                BuildUi();
                counterTimer = new System.Windows.Forms.Timer { Interval = 100 };
                counterTimer.Tick += (sender, args) => UpdateCounters();
                counterTimer.Start();
                pluginStatus.Text = "FruPractice ActBridge: listening on 127.0.0.1:10512";
                server = new SocketServer(HandleLineFromSocket, connected => PostToUi(() => SetConnectionStatus(connected)),
                    error => PostToUi(() => { failed++; Log("Socket error: " + error); }));
                server.Start();
            }
            catch (Exception ex)
            {
                if (pluginStatusText != null) pluginStatusText.Text = "FruPractice ActBridge: " + ex.Message;
                Log("Startup failed: " + ex.Message);
            }
        }

        public void DeInitPlugin()
        {
            try
            {
                stopping = true;
                counterTimer?.Stop();
                counterTimer?.Dispose();
                counterTimer = null;
                server?.Dispose();
                server = null;
                if (pluginStatus != null) pluginStatus.Text = "FruPractice ActBridge: stopped";
                if (connectionStatus != null) connectionStatus.Text = "Socket: stopped";
            }
            catch (Exception ex)
            {
                if (pluginStatus != null) pluginStatus.Text = "FruPractice ActBridge stop error: " + ex.Message;
            }
        }

        private void BuildUi()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 5,
                AutoScroll = true
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            connectionStatus = new Label { Text = "Socket: listening on 127.0.0.1:10512", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            counters = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            UpdateCounters();
            var modePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            modePanel.Controls.Add(new Label { Text = "Mode:", AutoSize = true, Margin = new Padding(0, 6, 8, 0) });
            var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            mode.Items.AddRange(new object[] { "Overlay-only", "Log writer" });
            mode.SelectedIndex = 0;
            mode.SelectedIndexChanged += (sender, args) =>
            {
                injector.Mode = mode.SelectedIndex == 0 ? InjectionMode.OverlayOnly : InjectionMode.LogWriter;
            };
            modePanel.Controls.Add(mode);

            var test = new Button { Text = "Inject test line", AutoSize = true, Dock = DockStyle.Left };
            test.Click += (sender, args) => InjectLine(20, TestFields);
            logBox = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                WordWrap = false
            };
            layout.Controls.Add(connectionStatus, 0, 0);
            layout.Controls.Add(counters, 0, 1);
            layout.Controls.Add(modePanel, 0, 2);
            layout.Controls.Add(test, 0, 3);
            layout.Controls.Add(logBox, 0, 4);
            tab.Controls.Add(layout);
        }

        private void HandleLineFromSocket(string line)
        {
            PostToUi(() =>
            {
                received++;
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    Log(line);
                    return;
                }

                var separator = line.IndexOf('|');
                if (separator < 1 || !int.TryParse(line.Substring(0, separator), out var type))
                {
                    failed++;
                    Log("Invalid line: " + line);
                    return;
                }
                InjectLine(type, line.Substring(separator + 1));
            });
        }

        private void InjectLine(int type, string fields)
        {
            try
            {
                if (injector.Inject(type, fields, out var error))
                {
                    injected++;
                    if (error != null)
                    {
                        failed++;
                        Log("Injection failed for a subscriber: " + error);
                    }
                }
                else
                {
                    failed++;
                    Log("Injection failed: " + error);
                }
            }
            catch (Exception ex)
            {
                failed++;
                Log("Injection failed: " + ex.Message);
            }
        }

        private void SetConnectionStatus(bool connected)
        {
            var state = connected ? "connected" : "listening on 127.0.0.1:10512";
            connectionStatus.Text = "Socket: " + state;
            pluginStatus.Text = "FruPractice ActBridge: " + state;
        }

        private void UpdateCounters()
        {
            if (counters != null)
                counters.Text = $"Received: {received}    Injected: {injected}    Failed: {failed}";
        }

        private void Log(string message)
        {
            if (logBox == null || logBox.IsDisposed) return;
            logLines.Enqueue(DateTime.Now.ToString("HH:mm:ss") + " " + message);
            while (logLines.Count > 200) logLines.Dequeue();
            logBox.Lines = logLines.ToArray();
            logBox.SelectionStart = logBox.TextLength;
            logBox.ScrollToCaret();
        }

        private void PostToUi(Action action)
        {
            try
            {
                var form = ActGlobals.oFormActMain;
                if (stopping || form == null || form.IsDisposed || !form.IsHandleCreated) return;
                form.BeginInvoke((MethodInvoker)(() =>
                {
                    if (stopping) return;
                    try { action(); }
                    catch (Exception ex)
                    {
                        failed++;
                        Log("Bridge error: " + ex.Message);
                    }
                }));
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }
    }
}
