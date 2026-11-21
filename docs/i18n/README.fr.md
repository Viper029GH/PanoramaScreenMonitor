# Panorama Screen Monitor

[Русский](../../README.md) · [English](README.en.md) · [Deutsch](README.de.md) · **Français** · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [简体中文](README.zh-CN.md) · [日本語](README.ja.md)

**Gestion automatique du rétroéclairage de l’écran du refroidisseur TRYX Panorama sous Windows.**

L’écran Panorama peut rester allumé après votre départ, alors que le moniteur principal s’est déjà éteint. Panorama Screen Monitor reste dans la zone de notification, éteint le rétroéclairage après une période d’inactivité et le rallume à votre retour.

**Testé sur TRYX Panorama `391A:1011` avec KANALI 2.7.0.**

Il y a environ six mois, j’ai demandé aux développeurs de KANALI d’ajouter l’extinction automatique du rétroéclairage en cas d’inactivité. La fonction manquait toujours au moment de préparer cette version, alors j’ai écrit cet utilitaire.

[Télécharger pour Windows x64](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest) · [Changelog](../../CHANGELOG.md) · [CC0 1.0](../../LICENSE)

<p align="center">
  <img src="../images/panorama-demo.png" alt="Panorama Screen Monitor en mode démo" width="420">
</p>

## Démarrage rapide

**Prérequis :** Windows x64, TRYX Panorama avec le périphérique USB RK PANO (`391A:1011`) et KANALI lancé normalement.

1. Téléchargez l’EXE ou le ZIP Windows x64 depuis la [dernière version](https://github.com/Viper029GH/PanoramaScreenMonitor/releases/latest). Décompressez le ZIP si vous l’avez choisi.
2. Lancez l’EXE. Il est portable et intègre .NET. Si KANALI démarre plus tard, l’utilitaire l’attend automatiquement.
3. Le délai par défaut est de **10 minutes**. Pour suivre le délai du moniteur principal, ouvrez **Paramètres**, cochez **Utiliser le délai d’extinction de Windows** et cliquez sur **Enregistrer**.

Fermer la fenêtre laisse l’application active dans la zone de notification. Pour quitter, choisissez **Quitter** dans le menu de l’icône. Arrêtez les autres scripts qui contrôlent déjà le rétroéclairage Panorama avant le lancement.

L’EXE n’est pas signé numériquement. Les sommes de contrôle figurent dans `SHA256SUMS.txt`.

## Fonctionnalités

| Fonction | Description |
| --- | --- |
| Mode automatique | Délai de 1 à 1440 minutes ou délai d’extinction de Windows. Le réglage secteur ou batterie est pris en compte. **Jamais** désactive l’extinction automatique. |
| Contrôle manuel et pause | Allumez ou éteignez l’écran depuis la fenêtre ou la zone de notification. La pause maintient l’état actuel. Le mode automatique peut être repris à tout moment. |
| Luminosité | Le curseur de 0 à 100 % est utilisable lorsque l’écran est allumé. La modification est confirmée par lecture de l’appareil. |
| Diagnostic | Modèle, micrologiciel, logiciel et système de l’écran, état USB et UDB, luminosité et heure de la dernière lecture réussie. |
| Intégration Windows | Démarrage avec Windows ou dans la zone de notification. Thème clair, sombre ou synchronisé avec le système. |
| Langues de l’interface | Russe, anglais, allemand, français, espagnol, portugais du Brésil, chinois simplifié et japonais. |

## Fonctionnement

L’utilitaire calcule l’inactivité à partir de la dernière interaction dans Windows. Il communique avec l’interface USB de l’appareil en accès partagé. KANALI continue de fonctionner pendant que Panorama Screen Monitor recherche la réponse correspondante dans le journal UDB et vérifie l’état de l’écran. Chaque réglage part d’une lecture de la configuration et conserve les autres champs. Si la configuration complète ne peut pas être interprétée, l’écriture est annulée.

La synchronisation utilise le délai d’extinction du mode de gestion de l’alimentation actif. Une vidéo, une extinction manuelle ou d’autres événements peuvent agir séparément sur le moniteur principal.

Les paramètres et un petit journal de diagnostic sont dans `%LOCALAPPDATA%\PanoramaScreenMonitor`. Pour signaler un problème, indiquez la version de KANALI, celle du micrologiciel affichée dans le diagnostic et les étapes de reproduction.

## Compiler depuis les sources

La compilation nécessite le **SDK .NET 10**. Le projet utilise C# et WPF.

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
.\artifacts\publish\win-x64\PanoramaScreenMonitor.exe --demo
```

Le mode démo permet de simuler l’inactivité, la saisie, les erreurs et les délais dépassés sans le refroidisseur. `PanoramaScreenMonitor.exe --self-test "<chemin du rapport>"` teste le protocole, le journal UDB et le contrôleur avec des données simulées.

Dans PowerShell, lancez `./scripts/build-release.ps1` depuis la racine. Le [script](../../scripts/build-release.ps1) vérifie la compilation et crée l’EXE, le ZIP, les notices .NET et les sommes SHA-256 dans `artifacts/releases/`. La version est définie dans le [fichier projet](../../PanoramaScreenMonitor.csproj).

L’implémentation utilise la documentation publiée des commandes et champs. [Schéma de transport TRYX](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/transport.proto) · [Schéma de configuration](https://github.com/DXVSI/Tryx-Linux-GUI/blob/production/protocol/wire-v1/configuration.proto)

## Licence

Le code source et la documentation sont disponibles sous [CC0 1.0 Universel](../../LICENSE). Les conditions distinctes de la version de .NET intégrée figurent dans `THIRD-PARTY-NOTICES.txt`.
