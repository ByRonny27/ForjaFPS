# ForjaFPS — contexto para Claude Code

Optimizador de Windows para gaming, open source (MIT) y **reversible**. C# + WPF sobre .NET 8.
Repo: https://github.com/ByRonny27/ForjaFPS · Autor: Ronny Geraldino. Proyecto para el canal de YouTube "Código Futuro IA".

Idioma: todo el texto visible (UI, logs, README, mensajes de commit) va en **español**.

## Comandos

```powershell
dotnet build src\ForjaFPS                 # compilar
dotnet run --project src\ForjaFPS         # ejecutar (la terminal debe ser de Administrador)
dotnet publish src\ForjaFPS -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

La app tiene `requireAdministrator` en `app.manifest`: desde una terminal sin elevar falla al arrancar.

## Estructura

```
src/ForjaFPS/
  App.xaml(.cs)            Tema oscuro (colores, estilos de botones, nav, ProgressBar) y RiskToBrushConverter
  MainWindow.xaml(.cs)     UI: Monitor, Tweaks, Debloat, Respaldo + log inferior. Code-behind, sin MVVM framework
  Models/Models.cs         Tweak, TweakAction, Catalog, Risk, TweakState, TweakItem, AppxItem, Palette
  Services/TweakEngine.cs  RegistryHelper + TweakEngine (aplicar, deshacer, escanear estado)
  Services/PowerShellRunner.cs  Ejecuta Windows PowerShell 5.1 con -EncodedCommand
  Services/SystemMonitor.cs     CPU (PerformanceCounter), RAM (GlobalMemoryStatusEx), temp ACPI (WMI), GPU (nvidia-smi)
  Data/tweaks.json         Catálogo de tweaks y apps de debloat (se copia a la salida)
```

## Principios (no romper)

1. **Todo es reversible.** Antes de escribir un valor del registro se guarda el original en
   `%LocalAppData%\ForjaFPS\state.json` (incluido "no existía", que al deshacer se borra).
   Si un tweak falla a mitad, se revierte solo y no queda marcado como aplicado.
2. **No reaplicar lo que ya está aplicado.** Si otra herramienta (WinUtil) ya dejó el valor, el estado es
   `AppliedExternally` y se omite; si no, el "original" guardado sería el valor ya modificado.
3. **Servicios por registro**, no con `sc.exe`: `HKLM\SYSTEM\CurrentControlSet\Services\<x>\Start`
   (2 auto, 3 manual, 4 deshabilitado), así usan el mismo respaldo.
4. **Sin drivers de terceros** (WinRing0, LibreHardwareMonitor): Defender los marca como vulnerables.
5. **No tocar apps de Xbox ni Gaming Services** en el debloat: Game Pass las necesita.
6. Los tweaks nuevos van en `tweaks.json`, no en C#. Cada uno con `risk` honesto y una descripción que diga el efecto secundario.

## Estados de un tweak (TweakState)

`Pending` · `Partial` · `AppliedByForja` (tiene respaldo, se puede deshacer) · `AppliedExternally` (ya estaba así, sin respaldo).
`TweakEngine.GetStateAsync` compara el registro real; las acciones `powershell` pueden traer `check` que imprime `1` si está aplicado.

## Trampas conocidas

- **WPF excluye `System.IO` de los ImplicitUsings** (para evitar el choque con `System.Windows.Shapes.Path`).
  Cualquier archivo que use `Path`, `File` o `Directory` necesita `using System.IO;` explícito.
- **Windows PowerShell 5.1 y la codificación:** `Get-Content`/`Set-Content` sin `-Encoding` leen UTF-8 sin BOM como ANSI
  y rompen tildes y emojis. Para editar archivos de texto usa las herramientas de edición o
  `[IO.File]::ReadAllText` / `WriteAllText` (UTF-8 sin BOM). Ya pasó una vez con el README.
- `PowerShellRunner` antepone `$ErrorActionPreference='Stop'`: un error no capturado da exit code 1.
  Los ejecutables nativos (powercfg) no lanzan: revisa `$LASTEXITCODE` y usa `throw`.
- `nvidia-smi` puede impedir que la GPU dedicada de una laptop se duerma: el monitor solo lee mientras su sección está visible.
- La temperatura de CPU por ACPI es aproximada y en muchos equipos no existe; la UI lo indica.
- Como la app corre elevada, `HKCU` es el hive del usuario que la abrió.

## Equipo de pruebas

Acer Nitro 5 AN515-57: i7-11800H, 16 GB, RTX 3050 Ti Laptop + Intel UHD, Windows 11 Pro 26H2.
Ya tiene tweaks de WinUtil aplicados (sirve para probar la detección de `AppliedExternally`).

## Pendiente / ideas

- [x] Arreglar la codificación del README (corregido en "README: corrige codificación").
- [ ] Release v0.1.0 en GitHub con el zip de `dotnet publish`.
- [ ] Captura de pantalla de la app en el README.
- [ ] GPU AMD e Intel en el monitor.
- [ ] Perfiles personalizados guardados por el usuario; exportar/importar configuración.
- [ ] Limpieza de temporales y caché de shaders.

## Commits

Mensajes cortos en español, en imperativo o descriptivos ("Monitor: añade GPU AMD"). No subir `bin/`, `obj/` ni `publish/`.
