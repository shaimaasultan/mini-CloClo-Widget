using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MiniCloClo;

// A hand-written entry point instead of the one WPF generates from
// App.xaml, so DLL loading is hardened and the single-instance lock taken
// before WPF loads anything.
public static class Program
{
    // This copy's hold on "the one running Mini CloClo" (see SingleInstance.cs).
    internal static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        ProcessHardening.Apply([]);
#if DEBUG
        if (args.Length == 2 && args[0] == "--snapshot") return Snapshot.Run(args[1]);
        if (args.Length == 2 && args[0] == "--prayer-test") return Snapshot.PrayerTest(args[1]);
        if (args.Length == 2 && args[0] == "--load-trace") return Snapshot.LoadTrace(args[1]);
        if (args.Length == 2 && args[0] == "--quran-test") return Snapshot.QuranTest(args[1]);
        if (args.Length == 2 && args[0] == "--reminder-test") return Snapshot.ReminderTest(args[1]);
        if (args.Length == 2 && args[0] == "--engine-check") return Snapshot.EngineCheck(args[1]);
        if (args.Length is 2 or 3 && args[0] == "--pull-test") return Snapshot.PullTest(args[1], args.Length == 3 && args[2] == "real");
        if (args.Length is 2 or 3 && args[0] == "--topics-test") return Snapshot.TopicsTest(args[1], args.Length == 3 ? args[2] : null);
        if (args.Length is 3 or 4 && args[0] == "--bt-test")
            return Snapshot.BluetoothTest(args[1], args[2], args.Length == 4 ? args[3] : null);
        // A debug test that loads its own copy of the model, so it may run
        // beside an installed Mini CloClo.
        // --voice-test <file> [text] [--model <ggml file>]
        if (args.Length >= 2 && args[0] == "--voice-test")
        {
            var rest = args.Skip(2).ToList();
            var model = rest.IndexOf("--model");
            if (model >= 0 && model + 1 < rest.Count)
            {
                LocalAudioTranscriber.TestModelPath = rest[model + 1];
                rest.RemoveRange(model, 2);
            }
            return Snapshot.VoiceTest(args[1], rest.FirstOrDefault());
        }
#endif

        // A sign-in launch waits for Windows' security services without
        // holding the single-instance mutex.
        if (args.Length == 1 && args[0] == StartupRegistration.SecurityWaitArgument)
            StartupReadiness.WaitForSecurityServices();

        Instance = SingleInstance.TryAcquire();
        if (Instance == null)
        {
            // In another session the running copy can't be brought up here,
            // so say why nothing opens.
            if (!SingleInstance.SignalRunningCopy())
                MessageBoxW(IntPtr.Zero,
                    "Mini CloClo is already running on this PC - for another Windows user, or as administrator. Only one copy can run at a time.",
                    "Mini CloClo", MB_OK | MB_ICONINFORMATION);
            return 0;
        }
        try
        {
            return Run();
        }
        finally
        {
            Instance.Dispose();
        }
    }

    // user32's own message box: no WPF type is touched before Run.
    private const uint MB_OK = 0x0, MB_ICONINFORMATION = 0x40;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    // Kept out of Main — and never inlined into it — so that compiling Main
    // can't touch any WPF type before the checks above have run.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run()
    {
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
