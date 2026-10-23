// Deepcool LM240 Windows USB proof of concept (CLI).
// GUI: dotnet run --project windows/DeepcoolLm240Gui
//
// Windows 11:
//   1. Close official DeepCool software.
//   2. Zadig → bind WinUSB to USB\VID_3633&PID_0026.
//   3. dotnet run --project windows/DeepcoolLm240Poc -- list
//   4. dotnet run --project windows/DeepcoolLm240Poc -- pattern

using DeepcoolLm240.Usb;

string command = args.Length > 0 ? args[0] : "help";
Guid? guidOverride = null;
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--guid")
        guidOverride = Guid.Parse(args[i + 1].Trim('{', '}'));
}

if (command is "help" or "--help" or "-h")
{
    Console.WriteLine("DeepcoolLm240Poc list|pattern [--guid {GUID}]");
    return 0;
}

if (command is not ("list" or "pattern"))
{
    Console.Error.WriteLine($"Unknown command: {command}");
    return 1;
}

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("This program talks to WinUSB and must be run on Windows 11.");
    return 1;
}

var devices = Lm240Device.Find(guidOverride);
if (devices.Count == 0)
{
    Console.Error.WriteLine(
        "No LM240 found (VID 3633 PID 0026). Close DeepCool software, bind WinUSB with Zadig, or pass --guid.");
    return 2;
}

if (command == "list")
{
    foreach (var device in devices)
        Console.WriteLine($"{device.Path}  guid={device.InterfaceGuid}");
    return 0;
}

using var usb = Lm240Device.Open(devices[0].Path);
Console.WriteLine($"Opened {devices[0].Path}");
usb.SendFrame(Lm240Protocol.ColorBars());
Console.WriteLine("Wrote init + color-bar frame.");
return 0;
