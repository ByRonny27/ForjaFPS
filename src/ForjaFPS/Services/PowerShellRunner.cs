using System.Diagnostics;
using System.Text;

namespace ForjaFPS.Services;

/// <summary>
/// Ejecuta scripts en Windows PowerShell 5.1. Usa -EncodedCommand para evitar
/// problemas de comillas al pasar el script por la línea de comandos.
/// </summary>
public static class PowerShellRunner
{
    const string Prelude =
        "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " +
        "$ProgressPreference = 'SilentlyContinue'; " +
        "$ErrorActionPreference = 'Stop'; ";

    public static async Task<(int ExitCode, string Output)> RunAsync(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Prelude + script));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(encoded);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("No se pudo iniciar PowerShell.");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = (await stdout) + (await stderr);
        return (process.ExitCode, output.Trim());
    }

    /// <summary>Escapa un texto para usarlo dentro de comillas simples de PowerShell.</summary>
    public static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
}
