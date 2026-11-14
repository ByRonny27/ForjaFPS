using System.IO;
using System.Globalization;
using System.Text.Json;
using ForjaFPS.Models;
using Microsoft.Win32;

namespace ForjaFPS.Services;

/// <summary>Valor original de una entrada del registro antes de que ForjaFPS la tocara.</summary>
public sealed class RegistryBackup
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
    public string? Kind { get; set; }
    public string? Value { get; set; }
}

public sealed class TweakBackup
{
    public string TweakId { get; set; } = "";
    public DateTime AppliedAt { get; set; }
    public List<RegistryBackup> Values { get; set; } = new();
}

public static class RegistryHelper
{
    static (RegistryKey Root, string Sub) Split(string path)
    {
        var i = path.IndexOf('\\');
        if (i <= 0) throw new ArgumentException($"Ruta de registro inválida: {path}");

        var hive = path[..i].ToUpperInvariant();
        var sub = path[(i + 1)..];
        RegistryKey root = hive switch
        {
            "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            _ => throw new NotSupportedException($"Hive no soportado: {hive}")
        };
        return (root, sub);
    }

    public static RegistryBackup Snapshot(string path, string name)
    {
        var backup = new RegistryBackup { Path = path, Name = name };
        var (root, sub) = Split(path);
        using var key = root.OpenSubKey(sub, false);
        if (key is null) return backup;

        var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return backup;

        var kind = key.GetValueKind(name);
        backup.Existed = true;
        backup.Kind = kind.ToString();
        backup.Value = Serialize(raw, kind);
        return backup;
    }

    static string Serialize(object raw, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => unchecked((uint)(int)raw).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.QWord => ((long)raw).ToString(CultureInfo.InvariantCulture),
        RegistryValueKind.MultiString => string.Join('\0', (string[])raw),
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])raw),
        _ => raw.ToString() ?? ""
    };

    static RegistryValueKind ParseKind(string kind) =>
        Enum.TryParse<RegistryValueKind>(kind, true, out var k)
        && k != RegistryValueKind.Unknown
        && k != RegistryValueKind.None
            ? k
            : RegistryValueKind.String;

    public static void Write(string path, string name, string kind, string value)
    {
        var (root, sub) = Split(path);
        using var key = root.CreateSubKey(sub, true)
            ?? throw new InvalidOperationException($"No se pudo abrir {path}");

        var k = ParseKind(kind);
        object data = k switch
        {
            RegistryValueKind.DWord => unchecked((int)uint.Parse(value, CultureInfo.InvariantCulture)),
            RegistryValueKind.QWord => long.Parse(value, CultureInfo.InvariantCulture),
            RegistryValueKind.MultiString => value.Split('\0'),
            RegistryValueKind.Binary => Convert.FromBase64String(value),
            _ => value
        };
        key.SetValue(name, data, k);
    }

    public static void Delete(string path, string name)
    {
        var (root, sub) = Split(path);
        using var key = root.OpenSubKey(sub, true);
        key?.DeleteValue(name, false);
    }

    public static void Restore(RegistryBackup b)
    {
        if (b.Existed && b.Kind is not null && b.Value is not null)
            Write(b.Path, b.Name, b.Kind, b.Value);
        else
            Delete(b.Path, b.Name);
    }

    public static bool Matches(string path, string name, string kind, string expected)
    {
        try
        {
            var current = Snapshot(path, name);
            if (!current.Existed) return false;

            var k = ParseKind(kind);
            if (k is RegistryValueKind.DWord or RegistryValueKind.QWord)
                return long.TryParse(current.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
                    && long.TryParse(expected, NumberStyles.Integer, CultureInfo.InvariantCulture, out var e)
                    && a == e;

            return string.Equals(current.Value, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Aplica y deshace tweaks. Antes de tocar nada guarda el valor original de cada entrada
/// del registro en %LocalAppData%\ForjaFPS\state.json, y desde ahí se restaura al deshacer.
/// </summary>
public sealed class TweakEngine
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    readonly Dictionary<string, TweakBackup> _state;

    public TweakEngine()
    {
        Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForjaFPS");
        Directory.CreateDirectory(Folder);
        _state = Load();
    }

    public string Folder { get; }
    string StateFile => Path.Combine(Folder, "state.json");

    public bool HasBackup(string tweakId) => _state.ContainsKey(tweakId);

    Dictionary<string, TweakBackup> Load()
    {
        if (!File.Exists(StateFile)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, TweakBackup>>(File.ReadAllText(StateFile))
                   ?? new();
        }
        catch
        {
            // No sobrescribir un respaldo dañado: se aparta para poder revisarlo a mano.
            var corrupt = Path.Combine(Folder, $"state.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(StateFile, corrupt);
            return new();
        }
    }

    void Save()
    {
        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_state, JsonOptions));
        File.Move(tmp, StateFile, true);
    }

    static bool IsRegistry(TweakAction a) =>
        string.Equals(a.Type, "registry", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(a.Path) && !string.IsNullOrWhiteSpace(a.Name);

    static bool IsPowerShell(TweakAction a) =>
        string.Equals(a.Type, "powershell", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Mira el sistema real (registro y comprobaciones de PowerShell) para saber si el tweak
    /// está aplicado, sin importar quién lo aplicó.
    /// </summary>
    public async Task<TweakState> GetStateAsync(Tweak tweak)
    {
        var ours = _state.ContainsKey(tweak.Id);
        var total = 0;
        var matched = 0;

        foreach (var a in tweak.Actions)
        {
            if (IsRegistry(a))
            {
                total++;
                if (RegistryHelper.Matches(a.Path!, a.Name!, a.Kind ?? "DWord", a.Value ?? "")) matched++;
            }
            else if (IsPowerShell(a) && !string.IsNullOrWhiteSpace(a.Check))
            {
                total++;
                if (await CheckAsync(a.Check)) matched++;
            }
        }

        // Sin forma de comprobarlo: solo sabemos lo que aplicó ForjaFPS.
        if (total == 0) return ours ? TweakState.AppliedByForja : TweakState.Pending;

        if (matched == total) return ours ? TweakState.AppliedByForja : TweakState.AppliedExternally;
        return matched == 0 ? TweakState.Pending : TweakState.Partial;
    }

    static async Task<bool> CheckAsync(string script)
    {
        try
        {
            var (code, output) = await PowerShellRunner.RunAsync(script);
            return code == 0 && output.Trim().EndsWith('1');
        }
        catch
        {
            return false;
        }
    }

    public async Task ApplyAsync(Tweak tweak)
    {
        var createdBackup = false;
        if (!_state.ContainsKey(tweak.Id))
        {
            var backup = new TweakBackup { TweakId = tweak.Id, AppliedAt = DateTime.Now };
            foreach (var a in tweak.Actions.Where(IsRegistry))
                backup.Values.Add(RegistryHelper.Snapshot(a.Path!, a.Name!));
            _state[tweak.Id] = backup;
            Save();
            createdBackup = true;
        }

        try
        {
            foreach (var a in tweak.Actions)
            {
                if (IsRegistry(a))
                {
                    RegistryHelper.Write(a.Path!, a.Name!, a.Kind ?? "DWord", a.Value ?? "");
                }
                else if (IsPowerShell(a) && !string.IsNullOrWhiteSpace(a.Apply))
                {
                    var (code, output) = await PowerShellRunner.RunAsync(a.Apply);
                    if (code != 0) throw new InvalidOperationException(Shorten(output));
                }
            }
        }
        catch when (createdBackup)
        {
            // Si falla a mitad, se deja todo como estaba y no queda marcado como aplicado.
            foreach (var b in _state[tweak.Id].Values)
            {
                try { RegistryHelper.Restore(b); } catch { /* se intenta con el resto */ }
            }
            _state.Remove(tweak.Id);
            Save();
            throw;
        }
    }

    public async Task<bool> UndoAsync(Tweak tweak)
    {
        if (!_state.TryGetValue(tweak.Id, out var backup)) return false;

        foreach (var b in backup.Values)
            RegistryHelper.Restore(b);

        foreach (var a in tweak.Actions.Where(IsPowerShell))
        {
            if (string.IsNullOrWhiteSpace(a.Undo)) continue;
            var (code, output) = await PowerShellRunner.RunAsync(a.Undo);
            if (code != 0) throw new InvalidOperationException(Shorten(output));
        }

        _state.Remove(tweak.Id);
        Save();
        return true;
    }

    static string Shorten(string text)
    {
        text = text.Trim();
        return text.Length <= 400 ? text : "..." + text[^400..];
    }
}
