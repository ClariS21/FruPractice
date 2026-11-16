# FruPractice.ActBridge

An ACT plugin that receives FRU Practice simulation log lines over TCP loopback at `127.0.0.1:10512`. Its default **Overlay-only** mode raises live ACT log events for overlays such as cactbot. **Log writer** mode passes lines to the FFXIV ACT plugin's log output and writes them to its network log.

Build from the repository root:

```powershell
dotnet build ActBridge/FruPractice.ActBridge.csproj -c Release
```

The DLL lands at `ActBridge/bin/Release/net48/FruPractice.ActBridge.dll`.

In ACT, open **Plugins** → **Plugin Listing** → **Browse**, select the DLL, then **Add/Enable Plugin**. The bridge tab shows its connection, counts, mode, and recent messages. Choose **Overlay-only** or **Log writer** in the mode dropdown. Overlay-only is the default; Log writer also records simulation lines in ACT's network log files.

Use **Inject test line** to send a sample StartsCasting event, or run `ActBridge/send-test-line.ps1` to exercise the socket from PowerShell.
