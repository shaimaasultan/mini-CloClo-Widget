using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MiniCloClo;

// Asks a paired phone to dial a number over Bluetooth, the way
// a car hands-free kit does: Mini CloClo connects to the phone's Hands-Free
// Audio Gateway service, does the standard hands-free handshake, sends one
// dial command (ATD), and disconnects. The phone places the call on its own
// line; the call's sound stays on the phone. Bluetooth is turned on only if
// it was off, and turned off again afterwards.
//
// This is the only file allowed to open a socket (scripts\check-no-network.ps1),
// and it only ever connects to a Bluetooth service of a paired device - the
// host it connects to always comes from an RfcommDeviceService. It never
// uses the network.
internal sealed class BluetoothDialer
{
    // Hands-Free Audio Gateway: the phone's side of the hands-free profile.
    private static readonly RfcommServiceId HandsFreeGateway =
        RfcommServiceId.FromUuid(new Guid("0000111F-0000-1000-8000-00805F9B34FB"));
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(5);

    // What happened, step by step, for the test report and the error message.
    public List<string> Log { get; } = new();
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    // A log line with the time since the run started.
    private void Note(string line) => Log.Add($"[{_clock.Elapsed.TotalSeconds,5:0.0}s] {line}");

    public sealed record PairedDevice(string Id, string Name);

    // The paired classic-Bluetooth devices, by name.
    public static async Task<List<PairedDevice>> PairedDevicesAsync()
    {
        var found = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        return found.Select(d => new PairedDevice(d.Id, d.Name)).OrderBy(d => d.Name).ToList();
    }

    // Connects, does the handshake and - with a number - dials it. Returns
    // true when every step succeeded. Bluetooth and the connection are always
    // put back the way they were.
    public async Task<bool> RunAsync(PairedDevice phone, string? number)
    {
        if (number != null && !IsDialable(number))
        {
            Note($"refused: \"{number}\" is not a phone number (digits, an optional leading +, at most 20)");
            return false;
        }
        Radio? radio = null;
        var turnedOn = false;
        StreamSocket? socket = null;
        try
        {
            (radio, turnedOn) = await EnsureBluetoothOnAsync();
            if (radio == null) return false;

            var device = await BluetoothDevice.FromIdAsync(phone.Id);
            if (device == null)
            {
                Note("the paired device could not be opened");
                return false;
            }
            Note($"device: {device.Name}, connection now: {device.ConnectionStatus}");
            var services = await device.GetRfcommServicesForIdAsync(HandsFreeGateway, BluetoothCacheMode.Uncached);
            Note($"hands-free gateway lookup: {services.Error}, {services.Services.Count} found");
            if (services.Error != BluetoothError.Success || services.Services.Count == 0)
            {
                Note("the phone offers no hands-free gateway right now (is it nearby, unlocked, with Bluetooth on?)");
                return false;
            }
            var service = services.Services[0];

            socket = new StreamSocket();
            using (var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(connectTimeout.Token);
            Note("connected to the phone's hands-free gateway");

            using var writer = new DataWriter(socket.OutputStream);
            using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };

            // The hands-free "service level connection": our features (none
            // optional), the phone's indicators, and indicator reporting on.
            if (!await CommandAsync(writer, reader, "AT+BRSF=0")) return false;
            if (!await CommandAsync(writer, reader, "AT+CIND=?")) return false;
            if (!await CommandAsync(writer, reader, "AT+CIND?")) return false;
            if (!await CommandAsync(writer, reader, "AT+CMER=3,0,0,1")) return false;
            Note("handshake complete");

            if (number == null)
            {
                Note("no number given: not dialing (test only)");
                return true;
            }
            var dialed = await CommandAsync(writer, reader, $"ATD{number};");
            if (dialed)
            {
                // Give the phone a moment to start the call before we leave.
                await ReadForAsync(reader, TimeSpan.FromSeconds(2));
                Note("dial request accepted by the phone");
            }
            writer.DetachStream();
            reader.DetachStream();
            return dialed;
        }
        catch (Exception ex)
        {
            Note($"stopped: {ex.GetType().Name}: {ex.Message.Trim()}");
            return false;
        }
        finally
        {
            socket?.Dispose();
            if (socket != null) Note("disconnected from the phone");
            if (turnedOn && radio != null)
            {
                try
                {
                    var state = await radio.SetStateAsync(RadioState.Off);
                    Note($"Bluetooth turned back off: {state}");
                }
                catch (Exception ex) { Note($"couldn't turn Bluetooth back off: {ex.GetType().Name}"); }
            }
            else if (radio != null) Note("Bluetooth left on, as it was");
        }
    }

    private static bool IsDialable(string number) => ContactSettings.IsDialable(number);

    // The paired devices that can place calls - phones offer a hands-free
    // gateway, headsets and speakers don't. Each is asked over Bluetooth, so
    // only phones that are nearby with Bluetooth on are found.
    public static async Task<List<PairedDevice>> FindPhonesAsync()
    {
        var phones = new List<PairedDevice>();
        foreach (var candidate in await PairedDevicesAsync())
        {
            try
            {
                var device = await BluetoothDevice.FromIdAsync(candidate.Id);
                if (device == null) continue;
                var services = await device.GetRfcommServicesForIdAsync(HandsFreeGateway, BluetoothCacheMode.Uncached);
                if (services.Error == BluetoothError.Success && services.Services.Count > 0) phones.Add(candidate);
            }
            catch
            {
                // Not reachable now: not offered.
            }
        }
        return phones;
    }

    private async Task<(Radio? Radio, bool TurnedOn)> EnsureBluetoothOnAsync()
    {
        var access = await Radio.RequestAccessAsync();
        var radio = (await Radio.GetRadiosAsync()).FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        if (radio == null)
        {
            Note("no Bluetooth radio found");
            return (null, false);
        }
        Note($"Bluetooth was: {radio.State} (radio control: {access})");
        if (radio.State == RadioState.On) return (radio, false);
        if (access != RadioAccessStatus.Allowed)
        {
            Note("Windows doesn't let apps turn Bluetooth on (Settings > Privacy & security > Radios)");
            return (null, false);
        }
        var result = await radio.SetStateAsync(RadioState.On);
        Note($"turning Bluetooth on: {result}");
        for (var i = 0; i < 20 && radio.State != RadioState.On; i++) await Task.Delay(250);
        // The phone needs a moment to see the PC again.
        await Task.Delay(1500);
        return radio.State == RadioState.On ? (radio, true) : (null, false);
    }

    // Sends one AT command and reads the reply up to OK or ERROR.
    private async Task<bool> CommandAsync(DataWriter writer, DataReader reader, string command)
    {
        writer.WriteString(command + "\r");
        await writer.StoreAsync();
        var reply = new StringBuilder();
        using var timeout = new CancellationTokenSource(CommandTimeout);
        try
        {
            while (true)
            {
                var count = await reader.LoadAsync(512).AsTask(timeout.Token);
                if (count == 0) break;
                reply.Append(reader.ReadString(count));
                var text = reply.ToString();
                if (text.Contains("\r\nOK\r\n") || text.Contains("ERROR")) break;
            }
        }
        catch (OperationCanceledException) { reply.Append(" (no reply within 5 s)"); }
        var shown = reply.ToString().Replace("\r", "").Replace("\n", " ").Trim();
        // Dialed numbers aren't kept in the log beyond their length.
        var label = command.StartsWith("ATD", StringComparison.Ordinal) ? $"ATD<{command.Length - 4} digits>;" : command;
        Note($"  {label} -> {shown}");
        return reply.ToString().Contains("\r\nOK\r\n");
    }

    private async Task ReadForAsync(DataReader reader, TimeSpan time)
    {
        using var timeout = new CancellationTokenSource(time);
        try
        {
            while (true)
            {
                var count = await reader.LoadAsync(512).AsTask(timeout.Token);
                if (count == 0) return;
                var text = reader.ReadString(count).Replace("\r", "").Replace("\n", " ").Trim();
                if (text.Length > 0) Note($"  phone says: {text}");
            }
        }
        catch (OperationCanceledException) { }
    }
}
