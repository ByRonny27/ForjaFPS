using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ForjaFPS.Models;

/// <summary>Nivel de riesgo de un tweak. El orden importa: los perfiles usan &lt;=.</summary>
public enum Risk { Seguro, Moderado, Avanzado }

/// <summary>Estado de un tweak según el escaneo del sistema.</summary>
public enum TweakState
{
    /// <summary>Ningún valor está como lo deja el tweak.</summary>
    Pending,
    /// <summary>Solo algunos valores coinciden (otra herramienta aplicó una parte, o Windows revirtió algo).</summary>
    Partial,
    /// <summary>Lo aplicó ForjaFPS y tiene el valor original guardado: se puede deshacer.</summary>
    AppliedByForja,
    /// <summary>Ya estaba aplicado por Windows u otra herramienta (WinUtil, etc.). No hay valor original.</summary>
    AppliedExternally
}

/// <summary>
/// Una acción dentro de un tweak.
/// type = "registry": escribe Path\Name = Value (Kind: DWord, QWord, String, ExpandString, MultiString, Binary).
/// type = "powershell": ejecuta Apply al aplicar y Undo al deshacer. Check (opcional) debe imprimir 1 si ya está aplicado.
/// Los servicios se cambian por registro (HKLM\SYSTEM\CurrentControlSet\Services\X\Start: 2 auto, 3 manual, 4 deshabilitado),
/// así el respaldo y el deshacer funcionan igual que cualquier otro valor.
/// </summary>
public sealed class TweakAction
{
    public string Type { get; set; } = "registry";
    public string? Path { get; set; }
    public string? Name { get; set; }
    public string? Kind { get; set; }
    public string? Value { get; set; }
    public string? Apply { get; set; }
    public string? Undo { get; set; }
    public string? Check { get; set; }
}

public sealed class Tweak
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
    public Risk Risk { get; set; }
    public bool RequiresRestart { get; set; }
    public List<TweakAction> Actions { get; set; } = new();
}

public sealed class AppxEntry
{
    public string Package { get; set; } = "";
    public string Name { get; set; } = "";
}

public sealed class Catalog
{
    public List<Tweak> Tweaks { get; set; } = new();
    public List<AppxEntry> Appx { get; set; } = new();
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name!);
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public static class Palette
{
    public static readonly Brush Green = Make(0x22, 0xC5, 0x5E);
    public static readonly Brush Amber = Make(0xF5, 0x9E, 0x0B);
    public static readonly Brush Red = Make(0xEF, 0x44, 0x44);
    public static readonly Brush Blue = Make(0x60, 0xA5, 0xFA);
    public static readonly Brush Muted = Make(0x8B, 0x93, 0xA7);

    static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class TweakItem : Observable
{
    public TweakItem(Tweak tweak) => Tweak = tweak;

    public Tweak Tweak { get; }
    public string Name => Tweak.Name;
    public string Description => Tweak.Description;
    public string Category => Tweak.Category;
    public Risk RiskLevel => Tweak.Risk;
    public string RiskText => Tweak.Risk.ToString();
    public string RestartText => Tweak.RequiresRestart ? "Requiere reinicio" : "";

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    TweakState _state;
    public TweakState State
    {
        get => _state;
        set
        {
            Set(ref _state, value);
            Raise(nameof(StatusText));
            Raise(nameof(StatusBrush));
            Raise(nameof(StatusTip));
            Raise(nameof(IsApplied));
        }
    }

    public bool IsApplied => State is TweakState.AppliedByForja or TweakState.AppliedExternally;

    public string StatusText => State switch
    {
        TweakState.AppliedByForja => "Aplicado",
        TweakState.AppliedExternally => "Ya aplicado",
        TweakState.Partial => "Parcial",
        _ => "Pendiente"
    };

    public Brush StatusBrush => State switch
    {
        TweakState.AppliedByForja => Palette.Green,
        TweakState.AppliedExternally => Palette.Blue,
        TweakState.Partial => Palette.Amber,
        _ => Palette.Muted
    };

    public string StatusTip => State switch
    {
        TweakState.AppliedByForja => "Aplicado por ForjaFPS. El valor original está guardado y se puede deshacer.",
        TweakState.AppliedExternally => "Ya estaba aplicado antes de ForjaFPS (por Windows o por otra herramienta, como WinUtil). Como no se conoce el valor original, ForjaFPS no lo puede deshacer.",
        TweakState.Partial => "Solo una parte está aplicada. Puede que otra herramienta aplicara algo parecido o que una actualización de Windows lo revirtiera. Aplicarlo lo completa.",
        _ => "Sin aplicar."
    };
}

public sealed class AppxItem : Observable
{
    public AppxItem(string package, string name)
    {
        Package = package;
        Name = name;
    }

    public string Package { get; }
    public string Name { get; }

    bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    bool? _isInstalled;
    public bool? IsInstalled
    {
        get => _isInstalled;
        set
        {
            Set(ref _isInstalled, value);
            Raise(nameof(StatusText));
            Raise(nameof(StatusBrush));
        }
    }

    public string StatusText => IsInstalled switch
    {
        true => "Instalada",
        false => "No presente",
        _ => "Comprobando..."
    };

    public Brush StatusBrush => IsInstalled == true ? Palette.Amber : Palette.Muted;
}
