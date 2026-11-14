using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using ForjaFPS.Models;

namespace ForjaFPS;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Error inesperado:\n\n{args.Exception.Message}", "ForjaFPS",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        base.OnStartup(e);
    }
}

/// <summary>Color de la etiqueta de riesgo: verde, ámbar o rojo.</summary>
public sealed class RiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Risk risk
            ? risk switch
            {
                Risk.Seguro => Palette.Green,
                Risk.Moderado => Palette.Amber,
                _ => Palette.Red
            }
            : Palette.Muted;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
