using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DeepcoolLm240.Usb;

namespace DeepcoolLm240Gui;

public partial class MainWindow : Window
{
    readonly WriteableBitmap _preview = new(
        new PixelSize(Lm240Protocol.Width, Lm240Protocol.Height),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Opaque);
    readonly TemplateRenderer _renderer = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    byte[] _frame = Lm240Protocol.Solid(14, 14, 18);
    Lm240Device? _device;
    string? _devicePath;
    string? _gameArtPath;
    bool _busy;
    bool _opened;

    public MainWindow()
    {
        InitializeComponent();
        LcdPreview.Source = _preview;
        LoadTemplates();
        _timer.Tick += (_, _) => RenderCurrent();
        _timer.Start();
        RenderCurrent();
        UpdateDeviceStatus();
    }

    public void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!App.IsExiting)
        {
            e.Cancel = true;
            Hide();
            StatusText.Text = "Still running in the tray. Open it from there, or Quit to exit.";
        }

        base.OnClosing(e);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _opened = true;
        WindowState = WindowState.Normal;
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_opened && change.Property == WindowStateProperty && WindowState == WindowState.Minimized)
        {
            Hide();
            WindowState = WindowState.Normal;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _device?.Dispose();
        _device = null;
        base.OnClosed(e);
    }

    void LoadTemplates()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "templates");
        var templates = LcdTemplate.LoadAll(dir);
        TemplateList.ItemsSource = templates;
        TemplateList.SelectionChanged += (_, _) => RenderCurrent();
        if (templates.Count > 0)
            TemplateList.SelectedIndex = 0;
        else
            StatusText.Text = "No templates found. Add a JSON file in windows/templates and reopen.";
    }

    void RenderCurrent()
    {
        if (TemplateList.SelectedItem is not LcdTemplate template)
            return;
        var stats = MonitorSnapshot.Demo(DateTime.UtcNow, "Demo session", _gameArtPath);
        _frame = _renderer.RenderRgb565(template, stats);
        ShowFrame(_frame);
        TemplateTitle.Text = template.Name;
        CpuPowerText.Text = $"{stats.CpuPower:0} W";
        CpuTempText.Text = $"{stats.CpuTemp:0}°";
        GpuPowerText.Text = $"{stats.GpuPower:0} W";
        GpuTempText.Text = $"{stats.GpuTemp:0}°";
    }

    async void OnLoadGameArt(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Game art for overlay templates",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });
        if (files.Count == 0)
            return;

        var dest = Path.Combine(Path.GetTempPath(), "lm240-game-art" + Path.GetExtension(files[0].Name));
        await using (var input = await files[0].OpenReadAsync())
        await using (var output = File.Create(dest))
            await input.CopyToAsync(output);

        _gameArtPath = dest;
        GameArtLabel.Text = files[0].Name;
        RenderCurrent();
        StatusText.Text = $"Using {files[0].Name} as game art. Detection will fill this automatically later.";
    }

    void OnFindDevice(object? sender, RoutedEventArgs e)
    {
        _device?.Dispose();
        _device = null;
        _devicePath = null;
        UpdateDeviceStatus();
    }

    async void OnSend(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        _busy = true;
        SetSendEnabled(false);
        RenderCurrent();
        var frame = _frame;
        try
        {
            await Task.Run(() =>
            {
                EnsureDevice();
                _device!.SendFrame(frame);
            });
            StatusText.Text = OperatingSystem.IsWindows()
                ? "Sent to the pump display."
                : "Preview only on this computer. Sending needs Windows 11 and WinUSB.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            _device?.Dispose();
            _device = null;
        }
        finally
        {
            _busy = false;
            SetSendEnabled(true);
        }
    }

    void EnsureDevice()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Preview updated. Sending to the display needs Windows 11 with WinUSB.");

        if (_device is not null)
            return;

        var devices = Lm240Device.Find();
        if (devices.Count == 0)
            throw new InvalidOperationException(
                "No LM240 found. Close DeepCool’s app and bind WinUSB in Zadig.");

        _devicePath = devices[0].Path;
        _device = Lm240Device.Open(_devicePath);
    }

    void UpdateDeviceStatus()
    {
        if (!OperatingSystem.IsWindows())
        {
            DeviceStatusText.Text = "Demo sensors";
            StatusText.Text = "Preview with demo sensors. Sending to the pump needs Windows 11.";
            return;
        }

        var devices = Lm240Device.Find();
        if (devices.Count == 0)
        {
            DeviceStatusText.Text = "Display not found";
            StatusText.Text = "Close DeepCool’s app and bind WinUSB in Zadig, then Find device.";
        }
        else
        {
            DeviceStatusText.Text = "Display connected";
            StatusText.Text = "Choose a template, then Send to Display.";
        }
    }

    void ShowFrame(byte[] frame)
    {
        var bgra = new byte[Lm240Protocol.Width * Lm240Protocol.Height * 4];
        Lm240Protocol.Rgb565ToBgra(frame, bgra);
        using var fb = _preview.Lock();
        int srcStride = Lm240Protocol.Width * 4;
        if (fb.RowBytes == srcStride)
        {
            Marshal.Copy(bgra, 0, fb.Address, bgra.Length);
        }
        else
        {
            for (int y = 0; y < Lm240Protocol.Height; y++)
                Marshal.Copy(bgra, y * srcStride, fb.Address + y * fb.RowBytes, srcStride);
        }

        LcdPreview.InvalidateVisual();
    }

    void SetSendEnabled(bool enabled) => SendButton.IsEnabled = enabled;
}
