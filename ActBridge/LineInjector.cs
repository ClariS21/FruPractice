using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Advanced_Combat_Tracker;

namespace FruPractice.ActBridge
{
    internal enum InjectionMode { OverlayOnly, LogWriter }

    internal sealed class LineInjector
    {
        private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private FieldInfo beforeLogLineRead;
        private object logOutput;
        private MethodInfo writeLine;

        public InjectionMode Mode { get; set; }

        public bool Inject(int type, string fields, out string error)
        {
            try
            {
                if (Mode == InjectionMode.LogWriter)
                {
                    InjectThroughLogWriter(type, fields);
                    error = null;
                    return true;
                }
                return InjectThroughActEvent(type, fields, out error);
            }
            catch (Exception ex)
            {
                error = (ex is TargetInvocationException invocation && invocation.InnerException != null
                    ? invocation.InnerException : ex).Message;
                return false;
            }
        }

        private bool InjectThroughActEvent(int type, string fields, out string error)
        {
            var form = ActGlobals.oFormActMain ?? throw new InvalidOperationException("ACT main form is unavailable.");
            if (form.InvokeRequired)
                throw new InvalidOperationException("ACT log events must be raised on the ACT UI thread.");

            var now = DateTime.Now;
            var line = ActEventLine.Format(type, now, fields);
            var args = new LogLineEventArgs(line, type, now, string.Empty, false);
            beforeLogLineRead = beforeLogLineRead ?? form.GetType().GetField("BeforeLogLineRead", InstanceFlags)
                ?? throw new MissingFieldException(form.GetType().FullName, "BeforeLogLineRead");
            var handlers = (LogLineEventDelegate)beforeLogLineRead.GetValue(form);
            if (handlers == null) throw new InvalidOperationException("ACT has no BeforeLogLineRead subscribers.");
            var delivered = 0;
            var failingSubscribers = new List<string>();
            string firstFailure = null;
            foreach (LogLineEventDelegate handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(false, args);
                    delivered++;
                }
                catch (Exception ex)
                {
                    failingSubscribers.Add(handler.Method.DeclaringType?.FullName ?? "<unknown subscriber>");
                    var failure = ex is TargetInvocationException invocation && invocation.InnerException != null
                        ? invocation.InnerException : ex;
                    if (firstFailure == null) firstFailure = failure.Message;
                }
            }
            error = firstFailure == null ? null : $"{string.Join(", ", failingSubscribers)}: {firstFailure}";
            return delivered > 0;
        }

        private void InjectThroughLogWriter(int type, string fields)
        {
            if (writeLine == null) FindLogWriter();
            writeLine.Invoke(logOutput, new object[] { type, DateTime.Now, fields });
        }

        private void FindLogWriter()
        {
            var form = ActGlobals.oFormActMain ?? throw new InvalidOperationException("ACT main form is unavailable.");
            var plugin = form.ActPlugins.FirstOrDefault(item => item.cbEnabled.Checked && item.pluginObj != null &&
                item.lblPluginTitle.Text.StartsWith("FFXIV_ACT_Plugin", StringComparison.Ordinal));
            if (plugin == null) throw new InvalidOperationException("Enabled FFXIV ACT plugin was not found.");

            var containerField = plugin.pluginObj.GetType().GetField("_iocContainer", InstanceFlags)
                ?? throw new MissingFieldException(plugin.pluginObj.GetType().FullName, "_iocContainer");
            var container = containerField.GetValue(plugin.pluginObj)
                ?? throw new InvalidOperationException("FFXIV plugin service container is unavailable.");
            var logfileAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => assembly.GetName().Name == "FFXIV_ACT_Plugin.Logfile")
                ?? throw new InvalidOperationException("FFXIV_ACT_Plugin.Logfile is not loaded.");
            var serviceType = logfileAssembly.GetType("FFXIV_ACT_Plugin.Logfile.ILogOutput", true);
            var getService = container.GetType().GetMethod("GetService", InstanceFlags, null, new[] { typeof(Type) }, null)
                ?? throw new MissingMethodException(container.GetType().FullName, "GetService(Type)");
            var output = getService.Invoke(container, new object[] { serviceType })
                ?? throw new InvalidOperationException("FFXIV log output service is unavailable.");
            var method = serviceType.GetMethod("WriteLine", new[] { typeof(int), typeof(DateTime), typeof(string) })
                ?? throw new MissingMethodException(serviceType.FullName, "WriteLine(int, DateTime, string)");
            logOutput = output;
            writeLine = method;
        }
    }
}
