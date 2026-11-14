using System.IO;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using ForjaFPS.Models;
using ForjaFPS.Services;

namespace ForjaFPS;

public partial class MainWindow : Window
{
    readonly ObservableCollection<TweakItem> _tweaks = new();
    readonly ObservableCollection<AppxItem> _appx = new();
    readonly TweakEngine _engine = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    SystemMonitor? _monitor;
    bool _readingMonitor;

    public MainWindow()
    {
        InitializeComponent();
        _timer.Tick += async (_, _) => await RefreshMonitorAsync();
    }

    // ===================== Inicio =====================

    async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        VersionText.Text = $"v{typeof(MainWindow).Assembly.GetName().Version?.ToString(3)} · open source";
        BackupFolderText.Text = $"Los valores originales se guardan en:\n{_engine.Folder}\\state.json";

        try
        {
            LoadCatalog();
        }
        catch (Exception ex)
        {
            Log($"Error cargando Data\\tweaks.json: {ex.Message}");
        }

        _monitor = await Task.Run(() => new SystemMonitor());
        ShowPanel("Monitor");
        await ScanAsync();
        await RefreshAppxAsync();
    }

    void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _monitor?.Dispose();
    }

    void LoadCatalog()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Data", "tweaks.json");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(file), options) ?? new Catalog();

        foreach (var tweak in catalog.Tweaks)
            _tweaks.Add(new TweakItem(tweak));

        var view = new ListCollectionView(_tweaks);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TweakItem.Category)));
        TweakList.ItemsSource = view;

        foreach (var app in catalog.Appx)
            _appx.Add(new AppxItem(app.Package, app.Name));
        AppxList.ItemsSource = _appx;

        Log($"Catálogo cargado: {catalog.Tweaks.Count} tweaks y {catalog.Appx.Count} apps.");
    }

    // ===================== Navegación =====================

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return; // se dispara durante InitializeComponent
        if (sender is RadioButton { Tag: string tag }) ShowPanel(tag);
    }

    void ShowPanel(string name)
    {
        MonitorPanel.Visibility = name == "Monitor" ? Visibility.Visible : Visibility.Collapsed;
        TweaksPanel.Visibility = name == "Tweaks" ? Visibility.Visible : Visibility.Collapsed;
        DebloatPanel.Visibility = name == "Debloat" ? Visibility.Visible : Visibility.Collapsed;
        BackupPanel.Visibility = name == "Backup" ? Visibility.Visible : Visibility.Collapsed;

        if (name == "Monitor")
        {
            _timer.Start();
            _ = RefreshMonitorAsync();
        }
        else
        {
            _timer.Stop();
        }

        if (name == "Backup") UpdateBackupSummary();
    }

    // ===================== Monitor =====================

    async Task RefreshMonitorAsync()
    {
        var monitor = _monitor;
        if (monitor is null || _readingMonitor) return;
        _readingMonitor = true;
        try
        {
            var s = await Task.Run(() => monitor.Read());

            CpuText.Text = $"{s.CpuPercent:0}%";
            CpuBar.Value = s.CpuPercent;
            CpuDetail.Text = s.CpuTempC is double cpuTemp
                ? $"Temperatura (zona ACPI, aproximada): {cpuTemp:0} °C"
                : "Temperatura: tu equipo no la expone sin drivers extra";

            var ramPercent = s.RamTotalGb > 0 ? s.RamUsedGb / s.RamTotalGb * 100 : 0;
            RamText.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB";
            RamBar.Value = ramPercent;
            RamDetail.Text = $"{ramPercent:0}% en uso";

            if (s.GpuName is not null)
            {
                GpuText.Text = $"{(s.GpuPercent ?? 0):0}%";
                GpuBar.Value = s.GpuPercent ?? 0;
                GpuDetail.Text = $"{s.GpuName} · {(s.GpuTempC ?? 0):0} °C";

                var vramPercent = s.VramTotalMb > 0 ? (s.VramUsedMb ?? 0) / s.VramTotalMb.Value * 100 : 0;
                VramText.Text = $"{(s.VramUsedMb ?? 0) / 1024:0.0} / {(s.VramTotalMb ?? 0) / 1024:0.0} GB";
                VramBar.Value = vramPercent;
                VramDetail.Text = $"{vramPercent:0}% en uso";
            }
            else
            {
                GpuText.Text = "N/D";
                GpuBar.Value = 0;
                GpuDetail.Text = "No se encontró nvidia-smi (solo GPUs NVIDIA por ahora)";
                VramText.Text = "N/D";
                VramBar.Value = 0;
                VramDetail.Text = "";
            }
        }
        catch (Exception ex)
        {
            Log($"Monitor: {ex.Message}");
        }
        finally
        {
            _readingMonitor = false;
        }
    }

    // ===================== Tweaks =====================

    void Profile_Click(object sender, RoutedEventArgs e)
    {
        var profile = (sender as Button)?.Tag as string;
        foreach (var t in _tweaks)
        {
            var inProfile = profile switch
            {
                "Seguro" => t.Tweak.Risk == Risk.Seguro,
                "Gaming" => t.Tweak.Risk <= Risk.Moderado,
                "Extremo" => true,
                _ => false
            };
            t.IsSelected = inProfile && !t.IsApplied;
        }

        if (profile != "Ninguno")
            Log($"Perfil {profile}: {_tweaks.Count(t => t.IsSelected)} tweaks pendientes seleccionados (los ya aplicados se omiten).");
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var selected = _tweaks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Log("No hay tweaks seleccionados.");
            return;
        }

        var advanced = selected.Where(t => t.Tweak.Risk == Risk.Avanzado).Select(t => "• " + t.Name).ToList();
        if (advanced.Count > 0)
        {
            var answer = MessageBox.Show(
                "Vas a aplicar tweaks avanzados:\n\n" + string.Join("\n", advanced) +
                "\n\nPueden subir temperatura y consumo, o empeorar algunos juegos. Puedes deshacerlos luego. ¿Continuar?",
                "ForjaFPS", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        await RunBusyAsync(async () =>
        {
            if (RestorePointCheck.IsChecked == true && !await CreateRestorePointAsync(askToContinue: true))
                return;

            var ok = 0;
            foreach (var item in selected)
            {
                if (item.IsApplied)
                {
                    Log($"• {item.Name}: ya estaba aplicado, se omite");
                    continue;
                }
                try
                {
                    await _engine.ApplyAsync(item.Tweak);
                    ok++;
                    Log($"✔ {item.Name}");
                }
                catch (Exception ex)
                {
                    Log($"✖ {item.Name}: {ex.Message}");
                }
            }

            await ScanAsync();
            Log($"Aplicados {ok} de {selected.Count}.");
            if (selected.Any(t => t.Tweak.RequiresRestart))
                Log("Algunos cambios se completan al reiniciar o cerrar sesión.");
        });
    }

    async void UndoSelected_Click(object sender, RoutedEventArgs e)
    {
        var selected = _tweaks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            Log("Selecciona los tweaks que quieres deshacer.");
            return;
        }
        await RunBusyAsync(() => UndoAsync(selected));
    }

    async void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        var withBackup = _tweaks.Where(t => _engine.HasBackup(t.Tweak.Id)).ToList();
        if (withBackup.Count == 0)
        {
            Log("No hay tweaks aplicados por ForjaFPS para deshacer.");
            return;
        }

        var answer = MessageBox.Show(
            $"Se restaurarán los valores originales de {withBackup.Count} tweaks. ¿Continuar?",
            "ForjaFPS", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunBusyAsync(() => UndoAsync(withBackup));
    }

    async Task UndoAsync(List<TweakItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                if (await _engine.UndoAsync(item.Tweak))
                    Log($"↩ {item.Name}: restaurado");
                else if (item.State == TweakState.AppliedExternally)
                    Log($"– {item.Name}: lo aplicó otra herramienta o Windows; ForjaFPS no conoce el valor original");
                else
                    Log($"– {item.Name}: no hay nada que deshacer");
            }
            catch (Exception ex)
            {
                Log($"✖ {item.Name}: {ex.Message}");
            }
        }

        await ScanAsync();
        if (items.Any(t => t.Tweak.RequiresRestart))
            Log("Reinicia para completar la restauración.");
    }

    // ===================== Escaneo =====================

    /// <summary>Revisa el sistema real y marca cada tweak según quién lo aplicó.</summary>
    async Task ScanAsync()
    {
        ScanSummary.Text = "Escaneando...";
        foreach (var t in _tweaks)
        {
            try { t.State = await _engine.GetStateAsync(t.Tweak); }
            catch { t.State = TweakState.Pending; }
        }

        var ours = _tweaks.Count(t => t.State == TweakState.AppliedByForja);
        var external = _tweaks.Count(t => t.State == TweakState.AppliedExternally);
        var partial = _tweaks.Count(t => t.State == TweakState.Partial);
        var pending = _tweaks.Count(t => t.State == TweakState.Pending);

        ScanSummary.Text =
            $"{ours + external} de {_tweaks.Count} aplicados: {ours} por ForjaFPS, {external} por Windows u otra herramienta. " +
            $"{partial} parciales, {pending} pendientes.";

        if (external > 0)
            Log($"Escaneo: {external} tweaks ya estaban aplicados por otra herramienta (por ejemplo WinUtil). Se respetan y no se vuelven a aplicar.");

        UpdateBackupSummary();
    }

    async void Rescan_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(ScanAsync);

    void SelectPending_Click(object sender, RoutedEventArgs e)
    {
        foreach (var t in _tweaks)
            t.IsSelected = t.State is TweakState.Pending or TweakState.Partial;
        Log($"Seleccionados {_tweaks.Count(t => t.IsSelected)} tweaks pendientes o parciales. Revisa los avanzados antes de aplicar.");
    }

    // ===================== Debloat =====================

    async void RefreshAppx_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(RefreshAppxAsync);

    void SelectInstalledAppx_Click(object sender, RoutedEventArgs e)
    {
        foreach (var a in _appx) a.IsSelected = a.IsInstalled == true;
    }

    async Task RefreshAppxAsync()
    {
        foreach (var a in _appx) a.IsInstalled = null;

        var (code, output) = await PowerShellRunner.RunAsync("Get-AppxPackage | ForEach-Object { $_.Name }");
        if (code != 0)
        {
            Log($"No se pudo leer la lista de apps: {output}");
            return;
        }

        var installed = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var a in _appx)
            a.IsInstalled = installed.Contains(a.Package);

        var present = _appx.Count(a => a.IsInstalled == true);
        var gone = _appx.Count - present;
        AppxSummary.Text = present == 0
            ? $"✔ Las {_appx.Count} apps del catálogo ya están quitadas o nunca estuvieron."
            : $"{gone} de {_appx.Count} apps ya quitadas · {present} siguen instaladas.";
        Log($"Debloat: {gone} de {_appx.Count} apps del catálogo ya no están en el sistema.");
    }

    async void RemoveAppx_Click(object sender, RoutedEventArgs e)
    {
        var selected = _appx.Where(a => a.IsSelected && a.IsInstalled == true).ToList();
        if (selected.Count == 0)
        {
            Log("Selecciona apps instaladas para quitar.");
            return;
        }

        var answer = MessageBox.Show(
            "Se quitarán para tu usuario:\n\n" + string.Join("\n", selected.Select(a => "• " + a.Name)) +
            "\n\nPuedes reinstalarlas desde la Microsoft Store. ¿Continuar?",
            "ForjaFPS", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        await RunBusyAsync(async () =>
        {
            foreach (var app in selected)
            {
                var (code, output) = await PowerShellRunner.RunAsync(
                    $"Get-AppxPackage -Name {PowerShellRunner.Quote(app.Package)} | Remove-AppxPackage");
                Log(code == 0 ? $"🗑 {app.Name}: quitada" : $"✖ {app.Name}: {output}");
                app.IsSelected = false;
            }
            await RefreshAppxAsync();
        });
    }

    // ===================== Respaldo =====================

    async void CreateRestorePoint_Click(object sender, RoutedEventArgs e) =>
        await RunBusyAsync(async () => await CreateRestorePointAsync(askToContinue: false));

    /// <summary>Devuelve false si falló y el usuario decidió no continuar.</summary>
    async Task<bool> CreateRestorePointAsync(bool askToContinue)
    {
        Log("Creando punto de restauración...");
        var (code, output) = await PowerShellRunner.RunAsync(
            "Enable-ComputerRestore -Drive ($env:SystemDrive + '\\') -ErrorAction SilentlyContinue; " +
            "Checkpoint-Computer -Description 'ForjaFPS' -RestorePointType MODIFY_SETTINGS");

        if (code == 0 && !output.Contains("1440", StringComparison.Ordinal))
        {
            Log("Punto de restauración creado.");
            return true;
        }

        // Windows avisa (código 1440 minutos) cuando ya hay uno de las últimas 24 h.
        Log("No se creó un punto nuevo (Windows solo permite uno cada 24 h o la protección está desactivada).");
        if (!askToContinue) return true;

        var answer = MessageBox.Show(
            "No se pudo crear un punto de restauración nuevo. Los tweaks igual se pueden deshacer desde ForjaFPS.\n\n¿Continuar?",
            "ForjaFPS", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        return answer == MessageBoxResult.Yes;
    }

    void UpdateBackupSummary()
    {
        var count = _tweaks.Count(t => _engine.HasBackup(t.Tweak.Id));
        BackupSummary.Text = count == 0
            ? "No hay tweaks aplicados por ForjaFPS."
            : $"Hay {count} tweaks aplicados por ForjaFPS con su valor original guardado.";
    }

    void OpenBackupFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", _engine.Folder) { UseShellExecute = true });

    // ===================== Utilidades =====================

    async Task RunBusyAsync(Func<Task> work)
    {
        ContentArea.IsEnabled = false;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
        }
        finally
        {
            ContentArea.IsEnabled = true;
            Mouse.OverrideCursor = null;
        }
    }

    void Log(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => Log(message));
            return;
        }
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }
}
