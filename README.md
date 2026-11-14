# ForjaFPS

Optimizador de Windows para gaming, **open source y reversible**. Hecho en C# con WPF (.NET 8).

La idea es simple: cada cambio guarda antes el valor original, así que todo se puede deshacer con un clic. Nada de "ejecuta este script y reza".

## Qué hace

- **Monitor**: CPU, RAM, GPU NVIDIA (uso, temperatura, VRAM) en tiempo real.
- **Tweaks**: 17 ajustes de gaming, rendimiento, privacidad e interfaz, cada uno con su nivel de riesgo.
- **Perfiles**: Seguro, Gaming (recomendado) y Extremo.
- **Debloat**: quita apps preinstaladas que no sirven para jugar. Las apps de Xbox no se tocan.
- **Respaldo**: punto de restauración, deshacer por tweak o deshacer todo.

## Niveles de riesgo

| Nivel | Qué significa |
|---|---|
| 🟢 Seguro | Sin efectos secundarios conocidos. |
| 🟡 Moderado | Mejora real, pero cambia algo que podrías notar (por ejemplo, notificaciones de apps de la Store). |
| 🔴 Avanzado | Puede subir temperatura y consumo, o empeorar ciertos juegos. Pide confirmación. |

## Requisitos

- Windows 10 u 11 de 64 bits.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar.
- Se ejecuta como administrador (lo pide solo al abrir).

## Compilar

```powershell
git clone https://github.com/<tu-usuario>/ForjaFPS.git
cd ForjaFPS
dotnet run --project src/ForjaFPS
```

Para generar un `.exe` listo para compartir:

```powershell
dotnet publish src/ForjaFPS -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

El ejecutable queda en `publish\ForjaFPS.exe` junto con la carpeta `Data`. Quien lo use necesita el runtime de .NET 8 Desktop. Si prefieres que funcione sin instalar nada, cambia a `--self-contained true` (el archivo pesa bastante más).

## Detecta lo que ya está aplicado

Al abrir, ForjaFPS escanea el sistema real (registro y comprobaciones de PowerShell) y marca cada tweak:

| Estado | Significado |
|---|---|
| 🟢 Aplicado | Lo aplicó ForjaFPS y tiene el valor original: se puede deshacer. |
| 🔵 Ya aplicado | Ya estaba así por Windows u otra herramienta (WinUtil, por ejemplo). Se respeta y no se vuelve a aplicar. |
| 🟠 Parcial | Solo una parte coincide. Pasa cuando otra herramienta aplicó algo parecido o una actualización de Windows revirtió algo. |
| ⚪ Pendiente | Sin aplicar. |

Los perfiles y el botón "Seleccionar pendientes" ignoran lo que ya está aplicado. Así ForjaFPS nunca guarda como "original" un valor que otra herramienta ya había cambiado, y el deshacer siempre vuelve al estado real de antes.

En Debloat también se ve cuántas apps del catálogo ya no están en el sistema.

## Cómo funciona el deshacer

Antes de aplicar un tweak, ForjaFPS lee cada valor del registro que va a cambiar y lo guarda en:

```
%LocalAppData%\ForjaFPS\state.json
```

Si el valor no existía, se anota eso también, y al deshacer se borra en vez de dejar basura. Si un tweak falla a mitad, se revierte solo y no queda marcado como aplicado.

Los servicios (por ejemplo DiagTrack) se cambian por registro (`HKLM\SYSTEM\CurrentControlSet\Services\<servicio>\Start`), así que usan el mismo sistema de respaldo.

## Añadir tweaks

Todo el catálogo está en [`src/ForjaFPS/Data/tweaks.json`](src/ForjaFPS/Data/tweaks.json). No hace falta tocar C#:

```json
{
  "id": "mi-tweak",
  "name": "Nombre visible",
  "description": "Qué hace y qué efecto secundario tiene.",
  "category": "Gaming",
  "risk": "Seguro",
  "requiresRestart": false,
  "actions": [
    { "type": "registry", "path": "HKCU\\Software\\Ejemplo", "name": "Valor", "kind": "DWord", "value": "1" },
    { "type": "powershell", "apply": "comando al aplicar", "undo": "comando al deshacer" }
  ]
}
```

- `kind`: `DWord`, `QWord`, `String`, `ExpandString`, `MultiString` o `Binary` (en base64).
- `risk`: `Seguro`, `Moderado` o `Avanzado`.
- Usa `HKCU` o `HKLM`.

Las contribuciones son bienvenidas. Antes de proponer un tweak, explica en el PR qué hace, de dónde sale y cómo comprobaste que funciona.

## Limitaciones conocidas

- **Temperatura de CPU**: se lee de la zona térmica ACPI, que es aproximada y muchas laptops no la exponen. No se usan drivers de terceros a propósito (los drivers tipo WinRing0 los marca Windows Defender como vulnerables).
- **GPU**: solo NVIDIA por ahora, vía `nvidia-smi`. En laptops con gráfica híbrida, consultar la GPU dedicada puede impedir que se duerma, por eso el monitor solo lee mientras su sección está abierta.
- **HKCU**: como la app corre como administrador, los ajustes de `HKCU` se aplican al usuario que la abrió. Si tu cuenta de uso diario no es administradora, se aplicarían a la cuenta de administrador.
- **Plan Alto rendimiento**: no existe en equipos con Modern Standby. En ese caso usa Configuración > Sistema > Energía > Mejor rendimiento.

## Ideas para próximas versiones

- Temperaturas de GPU AMD e Intel.
- Perfiles personalizados guardados por el usuario.
- Exportar e importar configuración.
- Limpieza de archivos temporales y caché de shaders.

## Aviso

Úsalo bajo tu propia responsabilidad. Crea un punto de restauración antes de aplicar cambios (la app lo hace por defecto).

## Licencia

[MIT](LICENSE) © 2026 Ronny Geraldino
