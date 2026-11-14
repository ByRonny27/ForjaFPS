# ForjaFPS

Optimizador de Windows para gaming, **open source y reversible**. Hecho en C# con WPF (.NET 8).

La idea es simple: cada cambio guarda antes el valor original, asÃ­ que todo se puede deshacer con un clic. Nada de "ejecuta este script y reza".

## QuÃ© hace

- **Monitor**: CPU, RAM, GPU NVIDIA (uso, temperatura, VRAM) en tiempo real.
- **Tweaks**: 17 ajustes de gaming, rendimiento, privacidad e interfaz, cada uno con su nivel de riesgo.
- **Perfiles**: Seguro, Gaming (recomendado) y Extremo.
- **Debloat**: quita apps preinstaladas que no sirven para jugar. Las apps de Xbox no se tocan.
- **Respaldo**: punto de restauraciÃ³n, deshacer por tweak o deshacer todo.

## Niveles de riesgo

| Nivel | QuÃ© significa |
|---|---|
| ðŸŸ¢ Seguro | Sin efectos secundarios conocidos. |
| ðŸŸ¡ Moderado | Mejora real, pero cambia algo que podrÃ­as notar (por ejemplo, notificaciones de apps de la Store). |
| ðŸ”´ Avanzado | Puede subir temperatura y consumo, o empeorar ciertos juegos. Pide confirmaciÃ³n. |

## Requisitos

- Windows 10 u 11 de 64 bits.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar.
- Se ejecuta como administrador (lo pide solo al abrir).

## Compilar

```powershell
git clone https://github.com/ByRonny27/ForjaFPS.git
cd ForjaFPS
dotnet run --project src/ForjaFPS
```

Para generar un `.exe` listo para compartir:

```powershell
dotnet publish src/ForjaFPS -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

El ejecutable queda en `publish\ForjaFPS.exe` junto con la carpeta `Data`. Quien lo use necesita el runtime de .NET 8 Desktop. Si prefieres que funcione sin instalar nada, cambia a `--self-contained true` (el archivo pesa bastante mÃ¡s).

## Detecta lo que ya estÃ¡ aplicado

Al abrir, ForjaFPS escanea el sistema real (registro y comprobaciones de PowerShell) y marca cada tweak:

| Estado | Significado |
|---|---|
| ðŸŸ¢ Aplicado | Lo aplicÃ³ ForjaFPS y tiene el valor original: se puede deshacer. |
| ðŸ”µ Ya aplicado | Ya estaba asÃ­ por Windows u otra herramienta (WinUtil, por ejemplo). Se respeta y no se vuelve a aplicar. |
| ðŸŸ  Parcial | Solo una parte coincide. Pasa cuando otra herramienta aplicÃ³ algo parecido o una actualizaciÃ³n de Windows revirtiÃ³ algo. |
| âšª Pendiente | Sin aplicar. |

Los perfiles y el botÃ³n "Seleccionar pendientes" ignoran lo que ya estÃ¡ aplicado. AsÃ­ ForjaFPS nunca guarda como "original" un valor que otra herramienta ya habÃ­a cambiado, y el deshacer siempre vuelve al estado real de antes.

En Debloat tambiÃ©n se ve cuÃ¡ntas apps del catÃ¡logo ya no estÃ¡n en el sistema.

## CÃ³mo funciona el deshacer

Antes de aplicar un tweak, ForjaFPS lee cada valor del registro que va a cambiar y lo guarda en:

```
%LocalAppData%\ForjaFPS\state.json
```

Si el valor no existÃ­a, se anota eso tambiÃ©n, y al deshacer se borra en vez de dejar basura. Si un tweak falla a mitad, se revierte solo y no queda marcado como aplicado.

Los servicios (por ejemplo DiagTrack) se cambian por registro (`HKLM\SYSTEM\CurrentControlSet\Services\<servicio>\Start`), asÃ­ que usan el mismo sistema de respaldo.

## AÃ±adir tweaks

Todo el catÃ¡logo estÃ¡ en [`src/ForjaFPS/Data/tweaks.json`](src/ForjaFPS/Data/tweaks.json). No hace falta tocar C#:

```json
{
  "id": "mi-tweak",
  "name": "Nombre visible",
  "description": "QuÃ© hace y quÃ© efecto secundario tiene.",
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

Las contribuciones son bienvenidas. Antes de proponer un tweak, explica en el PR quÃ© hace, de dÃ³nde sale y cÃ³mo comprobaste que funciona.

## Limitaciones conocidas

- **Temperatura de CPU**: se lee de la zona tÃ©rmica ACPI, que es aproximada y muchas laptops no la exponen. No se usan drivers de terceros a propÃ³sito (los drivers tipo WinRing0 los marca Windows Defender como vulnerables).
- **GPU**: solo NVIDIA por ahora, vÃ­a `nvidia-smi`. En laptops con grÃ¡fica hÃ­brida, consultar la GPU dedicada puede impedir que se duerma, por eso el monitor solo lee mientras su secciÃ³n estÃ¡ abierta.
- **HKCU**: como la app corre como administrador, los ajustes de `HKCU` se aplican al usuario que la abriÃ³. Si tu cuenta de uso diario no es administradora, se aplicarÃ­an a la cuenta de administrador.
- **Plan Alto rendimiento**: no existe en equipos con Modern Standby. En ese caso usa ConfiguraciÃ³n > Sistema > EnergÃ­a > Mejor rendimiento.

## Ideas para prÃ³ximas versiones

- Temperaturas de GPU AMD e Intel.
- Perfiles personalizados guardados por el usuario.
- Exportar e importar configuraciÃ³n.
- Limpieza de archivos temporales y cachÃ© de shaders.

## Aviso

Ãšsalo bajo tu propia responsabilidad. Crea un punto de restauraciÃ³n antes de aplicar cambios (la app lo hace por defecto).

## Licencia

[MIT](LICENSE) Â© 2026 Ronny Geraldino
