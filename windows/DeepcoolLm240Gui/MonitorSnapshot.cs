namespace DeepcoolLm240Gui;

public sealed class MonitorSnapshot
{
    public double CpuTemp { get; init; }
    public double CpuPower { get; init; }
    public double CpuUsage { get; init; }
    public double CpuFreq { get; init; }
    public double GpuTemp { get; init; }
    public double GpuPower { get; init; }
    public double GpuUsage { get; init; }
    public string GameName { get; init; } = "";
    public string? GameArtPath { get; init; }

    public static MonitorSnapshot Demo(DateTime utc, string gameName = "Demo session", string? gameArtPath = null)
    {
        double t = utc.TimeOfDay.TotalSeconds;
        return new MonitorSnapshot
        {
            CpuTemp = 58 + 8 * Math.Sin(t / 7),
            CpuPower = 72 + 18 * Math.Sin(t / 5),
            CpuUsage = 42 + 28 * (0.5 + 0.5 * Math.Sin(t / 3)),
            CpuFreq = 4.2 + 0.4 * Math.Sin(t / 9),
            GpuTemp = 64 + 10 * Math.Sin(t / 6 + 1),
            GpuPower = 168 + 35 * Math.Sin(t / 4 + 0.4),
            GpuUsage = 55 + 30 * (0.5 + 0.5 * Math.Sin(t / 3.6 + 0.8)),
            GameName = gameName,
            GameArtPath = gameArtPath,
        };
    }

    public object? Read(string key) => key switch
    {
        "cpu.temp" => CpuTemp,
        "cpu.power" => CpuPower,
        "cpu.usage" => CpuUsage,
        "cpu.freq" => CpuFreq,
        "gpu.temp" => GpuTemp,
        "gpu.power" => GpuPower,
        "gpu.usage" => GpuUsage,
        "game.name" => GameName,
        "game.art" => GameArtPath,
        _ => null,
    };
}
