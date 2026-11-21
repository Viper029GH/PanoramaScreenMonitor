# Panorama Screen Monitor

[Русский](../../README.md) · [English](README.en.md) · **Deutsch** · [Français](README.fr.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

**Automatische Steuerung der Hintergrundbeleuchtung des TRYX-Panorama-Displays unter Windows.**

Das Panorama-Display kann weiterleuchten, wenn der Hauptmonitor nach dem Verlassen des PCs bereits ausgeschaltet ist. Panorama Screen Monitor läuft im Infobereich, schaltet die Beleuchtung bei Inaktivität aus und bei neuer Eingabe wieder ein.

**Getestet mit TRYX Panorama `391A:1011` und KANALI 2.7.0.** Weitere USB-Modelle werden experimentell unterstützt. Die Steuerung wird erst nach erfolgreicher Leseprüfung verfügbar. [USB-Kompatibilität](README.en.md#usb-compatibility)

Vor etwa einem halben Jahr bat ich die KANALI-Entwickler darum, die Hintergrundbeleuchtung bei Inaktivität automatisch auszuschalten. Als diese Version vorbereitet wurde, fehlte die Funktion noch. Deshalb habe ich dieses Programm geschrieben.

[Download für Windows x64](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest) · [Changelog](../../CHANGELOG.md) · [CC0 1.0](../../LICENSE)

<p align="center">
  <img src="../images/panorama-demo.png" alt="Panorama Screen Monitor im Demomodus" width="420">
</p>

## Erste Schritte

**Voraussetzungen:** Windows x64, ein TRYX-Panorama-Gerät aus der [USB-Kompatibilitätstabelle](README.en.md#usb-compatibility) und regulär gestartetes KANALI.

1. Laden Sie die EXE oder ZIP für Windows x64 aus der [neuesten Version](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest) herunter. Entpacken Sie gegebenenfalls die ZIP.
2. Starten Sie die EXE. Das Programm ist portabel und enthält .NET. Falls KANALI später startet, wartet das Programm automatisch darauf.
3. Die Standard-Leerlaufzeit beträgt **10 Minuten**. Um das Bildschirmtimeout von Windows zu übernehmen, öffnen Sie **Einstellungen**, aktivieren Sie **Windows-Bildschirmtimeout verwenden** und wählen Sie **Speichern**.

Beim Schließen des Fensters bleibt das Programm im Infobereich aktiv. Zum Beenden wählen Sie **Beenden** im dortigen Menü. Beenden Sie zuvor laufende Panorama-Skripte zur Steuerung der Hintergrundbeleuchtung.

Die EXE ist nicht digital signiert. Die Prüfsummen stehen in `SHA256SUMS.txt`.

## Funktionen

| Funktion | Beschreibung |
| --- | --- |
| Automatik | Eigene Leerlaufzeit von 1 bis 1440 Minuten oder Windows-Bildschirmtimeout. Die Einstellung für Netz- und Akkubetrieb wird berücksichtigt. **Nie** deaktiviert das automatische Ausschalten. |
| Manuell und Pause | Schalten Sie das Display im Fenster oder Infobereich ein oder aus. Die Pause hält den aktuellen Zustand. Die Automatik lässt sich jederzeit fortsetzen. |
| Helligkeit | Der Regler für 0–100 % ist bei eingeschaltetem Display verfügbar. Änderungen werden durch Auslesen des Geräts überprüft. |
| Diagnose | Modell, Firmware, Display-Software und -Betriebssystem, USB- und UDB-Status, Helligkeit und Zeit der letzten erfolgreichen Abfrage. |
| Windows-Einstellungen | Start mit Windows oder im Infobereich. Helles, dunkles oder systemabhängiges Design. |
| Oberflächensprachen | Russisch, Englisch, Deutsch, Französisch, Spanisch, brasilianisches Portugiesisch, vereinfachtes Chinesisch und Japanisch. |

## Funktionsweise

Das Programm ermittelt die Leerlaufzeit anhand der letzten Eingabe in Windows. Es sendet Befehle über die reguläre USB-Schnittstelle mit gemeinsamem Zugriff. KANALI läuft weiter, während Panorama Screen Monitor die passende Antwort im UDB-Protokoll sucht und den tatsächlichen Zustand prüft. Vor Änderungen wird die aktuelle Konfiguration neu gelesen. Andere Felder bleiben erhalten. Bei nicht eindeutig lesbarer Konfiguration wird nicht geschrieben.

Die Synchronisierung verwendet das Bildschirmtimeout des aktiven Windows-Energiesparplans. Videowiedergabe, manuelles Ausschalten und andere Ereignisse können den Hauptmonitor unabhängig davon beeinflussen.

Einstellungen und ein kleines Diagnoseprotokoll liegen in `%LOCALAPPDATA%\PanoramaScreenMonitor`. Geben Sie bei einem Fehlerbericht die KANALI-Version, die angezeigte Firmware-Version und die Schritte zur Reproduktion an.

## Aus dem Quellcode bauen

Voraussetzung ist das **.NET 10 SDK**. Das Projekt verwendet C# und WPF.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
.\artifacts\publish\win-x64\PanoramaScreenMonitor.exe --demo
```

Im Demomodus können Sie ohne Kühler Leerlauf, Eingaben, Fehler und Zeitüberschreitungen simulieren. `PanoramaScreenMonitor.exe --self-test "<Berichtspfad>"` prüft Protokoll, UDB-Protokollauswertung und Steuerung mit simulierten Daten.

Starten Sie `./scripts/build-release.ps1` in PowerShell im Stammverzeichnis. Das [Skript](../../scripts/build-release.ps1) prüft die Version und erzeugt EXE, ZIP, .NET-Hinweise und SHA-256-Prüfsummen in `artifacts/releases/`. Die Versionsnummer steht in der [Projektdatei](../../PanoramaScreenMonitor.csproj).

Die Gerätebefehle und Datenfelder folgen der veröffentlichten Dokumentation. [TRYX-Transportschema](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/transport.proto) · [Konfigurationsschema](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/configuration.proto)

## Lizenz

Quellcode und Dokumentation stehen unter [CC0 1.0 Universal](../../LICENSE). Für die mitgelieferte .NET-Laufzeit gelten die Bedingungen in `THIRD-PARTY-NOTICES.txt` der Veröffentlichung.
