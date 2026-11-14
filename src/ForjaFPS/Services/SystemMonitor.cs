using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;

namespace ForjaFPS.Services;

public sealed class SystemSnapshot
{
    public double CpuPercent { get; init; }
    public double? CpuTempC { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
    public string? GpuName { get; init; }
    public double? GpuPercent { get; init; }
    public double? GpuTempC { get; init; }
    public double? VramUsedMb { get; init; }
    public double? VramTotalMb { get; init; }
}

/// <summary>
/// Lecturas sin drivers de terceros:
/// - CPU: contadores de rendimiento de Windows.
/// - RAM: GlobalMemoryStatusEx.
/// - Temperatura CPU: zona térmica ACPI (aproximada; muchas laptops no la exponen).
/// - GPU NVIDIA: nvidia-smi, que viene con el driver.
/// </summary>
public sealed class SystemMonitor : IDisposable
{
    readonly PerformanceCounter? _cpu;
    bool _acpiAvailable = true;
    bool _nvidiaAvailable = true;

    sealed record NvidiaInfo(string Name, double Util, double Temp, double MemUsed, double MemTotal);

    public SystemMonitor()
    {
        _cpu = TryCounter("Processor Information", "% Processor Utility")
               ?? TryCounter("Processor", "% Processor Time");
    }

    static PerformanceCounter? TryCounter(string category, string counter)
    {
        try
        {
            var c = new PerformanceCounter(category, counter, "_Total", true);
            c.NextValue(); // la primera lectura siempre es 0
            return c;
        }
        catch
        {
            return null;
        }
    }

    public SystemSnapshot Read()
    {
        double cpu = 0;
        try { cpu = Math.Clamp(_cpu?.NextValue() ?? 0, 0, 100); } catch { }

        var (used, total) = ReadRam();
        var gpu = ReadNvidia();

        return new SystemSnapshot
        {
            CpuPercent = cpu,
            CpuTempC = ReadAcpiTemp(),
            RamUsedGb = used,
            RamTotalGb = total,
            GpuName = gpu?.Name,
            GpuPercent = gpu?.Util,
            GpuTempC = gpu?.Temp,
            VramUsedMb = gpu?.MemUsed,
            VramTotalMb = gpu?.MemTotal
        };
    }

    // ---------- RAM ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    sealed class MemoryStatusEx
    {
        public uint dwLength = (uint)Marshal.SizeOf(typeof(MemoryStatusEx));
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatusEx buffer);

    static (double UsedGb, double TotalGb) ReadRam()
    {
        var status = new MemoryStatusEx();
        if (!GlobalMemoryStatusEx(status)) return (0, 0);
        const double gb = 1024d * 1024 * 1024;
        return ((status.ullTotalPhys - status.ullAvailPhys) / gb, status.ullTotalPhys / gb);
    }

    // ---------- Temperatura CPU (ACPI) ----------

    double? ReadAcpiTemp()
    {
        if (!_acpiAvailable) return null;
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
            using var results = searcher.Get();

            double max = double.MinValue;
            foreach (ManagementBaseObject o in results)
            {
                using (o)
                {
                    var kelvinTenths = Convert.ToDouble(o["CurrentTemperature"], CultureInfo.InvariantCulture);
                    var celsius = kelvinTenths / 10.0 - 273.15;
                    if (celsius > max) max = celsius;
                }
            }
            return max is > 0.0 and < 150.0 ? max : null;
        }
        catch
        {
            _acpiAvailable = false; // el equipo no la expone; no se vuelve a intentar
            return null;
        }
    }

    // ---------- GPU NVIDIA ----------

    NvidiaInfo? ReadNvidia()
    {
        if (!_nvidiaAvailable) return null;
        try
        {
            var psi = new ProcessStartInfo("nvidia-smi",
                "--query-gpu=name,utilization.gpu,temperature.gpu,memory.used,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p is null) { _nvidiaAvailable = false; return null; }

            var line = p.StandardOutput.ReadLine();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            if (string.IsNullOrWhiteSpace(line)) return null;

            var parts = line.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 5) return null;

            return new NvidiaInfo(parts[0], Num(parts[1]), Num(parts[2]), Num(parts[3]), Num(parts[4]));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            _nvidiaAvailable = false; // nvidia-smi no existe en este equipo
            return null;
        }
        catch
        {
            return null;
        }
    }

    static double Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public void Dispose() => _cpu?.Dispose();
}
