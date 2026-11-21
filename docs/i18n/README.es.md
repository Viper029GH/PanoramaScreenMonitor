# Panorama Screen Monitor

[Русский](../../README.md) · [English](README.en.md) · [Deutsch](README.de.md) · [Français](README.fr.md) · **Español** · [Português (Brasil)](README.pt-BR.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

**Control automático de la retroiluminación de la pantalla del refrigerador TRYX Panorama en Windows.**

La pantalla Panorama puede seguir encendida cuando te alejas y el monitor principal ya se ha apagado. Panorama Screen Monitor se ejecuta en la bandeja del sistema, apaga la retroiluminación al detectar inactividad y la enciende cuando vuelves.

**Probado con TRYX Panorama `391A:1011` y KANALI 2.7.0.** Los demás modelos USB tienen compatibilidad experimental. El control solo se habilita tras una comprobación de lectura correcta. [Compatibilidad USB](README.en.md#usb-compatibility)

Hace unos seis meses pedí a los desarrolladores de KANALI que añadieran el apagado automático de la retroiluminación cuando el equipo estuviera inactivo. La función seguía sin estar disponible al preparar esta versión, así que escribí esta utilidad.

[Descargar para Windows x64](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest) · [Changelog](../../CHANGELOG.md) · [CC0 1.0](../../LICENSE)

<p align="center">
  <img src="../images/panorama-demo.png" alt="Panorama Screen Monitor en modo demo" width="420">
</p>

## Inicio rápido

**Requisitos:** Windows x64, un TRYX Panorama de la [tabla de compatibilidad USB](README.en.md#usb-compatibility) y KANALI ejecutándose normalmente.

1. Descarga el EXE o ZIP para Windows x64 desde la [última versión](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest). Si elegiste el ZIP, descomprímelo.
2. Ejecuta el EXE. Es portátil e incluye .NET. Si KANALI se inicia después, la utilidad lo esperará automáticamente.
3. El tiempo predeterminado es de **10 minutos**. Para seguir el tiempo del monitor principal, abre **Ajustes**, activa **Usar el tiempo de espera de pantalla de Windows** y haz clic en **Guardar**.

Al cerrar la ventana, la utilidad permanece activa en la bandeja. Para salir, elige **Salir** en el menú de su icono. Detén otros scripts que controlen la retroiluminación de Panorama antes de abrir esta utilidad.

El EXE no tiene firma digital. Las sumas de comprobación se encuentran en `SHA256SUMS.txt`.

## Funciones

| Función | Cómo funciona |
| --- | --- |
| Control automático | Tiempo propio de 1 a 1440 minutos o configuración de pantalla de Windows. Se utiliza la configuración de corriente o batería según corresponda. **Nunca** desactiva el apagado automático. |
| Control manual y pausa | Enciende o apaga la pantalla desde la ventana o la bandeja. La pausa mantiene el estado actual. Puedes reanudar el control automático en cualquier momento. |
| Brillo | Control del 0 al 100 % mientras la pantalla esté encendida. El cambio se verifica leyendo el dispositivo. |
| Diagnóstico | Modelo, firmware, software y sistema de la pantalla, estado USB y UDB, brillo y última lectura correcta. |
| Integración con Windows | Inicio con Windows o en la bandeja. Tema claro, oscuro o según el sistema. |
| Idiomas | Ruso, inglés, alemán, francés, español, portugués de Brasil, chino simplificado y japonés. |

## Cómo funciona

La utilidad calcula la inactividad a partir de la última interacción en Windows. Envía los comandos por la interfaz USB normal con acceso compartido. KANALI sigue funcionando mientras Panorama Screen Monitor localiza su respuesta en el registro UDB y comprueba el estado de la pantalla. Cada cambio parte de una configuración recién leída y conserva el resto de campos. Si no puede interpretar la configuración completa, cancela la escritura.

La sincronización usa el tiempo de apagado de pantalla del plan de energía activo. La reproducción de vídeo, el apagado manual y otros eventos pueden afectar al monitor principal por separado.

Los ajustes y un pequeño registro se guardan en `%LOCALAPPDATA%\PanoramaScreenMonitor`. Al informar de un problema, incluye la versión de KANALI, el firmware indicado en el diagnóstico y los pasos para reproducirlo.

## Compilar desde el código fuente

Necesitas el **SDK de .NET 10**. El proyecto está escrito en C# y WPF.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
.\artifacts\publish\win-x64\PanoramaScreenMonitor.exe --demo
```

El modo demo permite simular inactividad, interacción, errores y tiempos de espera sin el refrigerador. `PanoramaScreenMonitor.exe --self-test "<ruta del informe>"` comprueba el protocolo, el registro UDB y el controlador con datos simulados.

Ejecuta `./scripts/build-release.ps1` desde la raíz en PowerShell. El [script](../../scripts/build-release.ps1) comprueba la compilación y crea el EXE, ZIP, avisos de .NET y sumas SHA-256 en `artifacts/releases/`. La versión se define en el [archivo del proyecto](../../PanoramaScreenMonitor.csproj).

La implementación se basa en la documentación publicada de los comandos y campos del dispositivo. [Esquema de transporte TRYX](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/transport.proto) · [Esquema de configuración](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/configuration.proto)

## Licencia

El código fuente y la documentación están disponibles bajo [CC0 1.0 Universal](../../LICENSE). Los términos independientes de .NET se incluyen en `THIRD-PARTY-NOTICES.txt`.
