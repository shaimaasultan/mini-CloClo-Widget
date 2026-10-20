using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MiniCloClo;

// The Run key has no ordering guarantee. At sign-in, wait until the Defender
// antivirus, Windows Security, and Security Center services report Running
// before creating the widget. A bounded wait also covers PCs where Defender
// is disabled by policy or another antivirus is installed.
internal static class StartupReadiness
{
    private static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly string[] Services = ["WinDefend", "SecurityHealthService", "wscsvc"];

    public static void WaitForSecurityServices()
    {
        var timer = Stopwatch.StartNew();
        var stableChecks = 0;
        while (timer.Elapsed < MaximumWait)
        {
            stableChecks = AllRunning() ? stableChecks + 1 : 0;
            if (stableChecks >= 3) return;
            var remaining = MaximumWait - timer.Elapsed;
            if (remaining > TimeSpan.Zero)
                Thread.Sleep(remaining < PollInterval ? remaining : PollInterval);
        }
    }

    // Read-only status queries; never asks for more rights or changes a
    // service. Anything unreadable counts as not running, so the timeout
    // remains the fallback.
    private static bool AllRunning()
    {
        var manager = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (manager == IntPtr.Zero) return false;
        try
        {
            foreach (var name in Services)
            {
                var service = OpenServiceW(manager, name, SERVICE_QUERY_STATUS);
                if (service == IntPtr.Zero) return false;
                try
                {
                    if (!QueryServiceStatus(service, out var status) || status.dwCurrentState != SERVICE_RUNNING) return false;
                }
                finally { CloseServiceHandle(service); }
            }
            return true;
        }
        finally { CloseServiceHandle(manager); }
    }

    private const uint SC_MANAGER_CONNECT = 0x0001, SERVICE_QUERY_STATUS = 0x0004, SERVICE_RUNNING = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode,
            dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, uint access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr manager, string serviceName, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);
}
