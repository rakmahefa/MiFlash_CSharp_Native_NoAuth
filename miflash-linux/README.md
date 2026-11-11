# miflash-linux — solution .NET 8 du portage natif Linux

> Référentiels : [`spec-linux.md`](../spec-linux.md) (spécification) ·
> [`plan.md`](../plan.md) (jalons M0→M6) · [`todo.md`](../todo.md) (checklist).
> Code Windows historique : [`../src/`](../src/) (lecture seule, référence de portage).

## Layout

| Projet | Rôle | Jalon |
|---|---|---|
| `src/MiFlash.Core` | Protocoles Sahara/Firehose, formats d'images, `ITransport`, `IEdlAuthenticator` — **aucune dépendance OS** | M1 |
| `src/MiFlash.Transport.Usb` | Transport libusb (05c6:9008 + fastboot), hotplug | M2 |
| `src/MiFlash.Transport.Serial` | Repli `qcserial` → `/dev/ttyUSB*` | M2 |
| `src/MiFlash.Edl` | Moteur EDL : port de `SerialPortDevice.cs` | M3 |
| `src/MiFlash.Fastboot` | Exécuteur POSIX + parsing `flash_all*` | M5 |
| `src/MiFlash.Cli` | `miflash devices\|flash\|backup\|…` | M4 |
| `tests/MiFlash.Core.Tests` | Golden tests protocole (sans matériel) | M1+ |

## Prérequis

- .NET SDK 8.0 (`dotnet --version` ≥ 8.0)
- Pour l'exécution sur matériel : `libusb-1.0-0`, règles udev (M2.6), `android-tools` (M5)

## Commandes

```bash
dotnet build MiFlash.Linux.sln                 # build debug
dotnet test  MiFlash.Linux.sln                 # tests unitaires
dotnet format MiFlash.Linux.sln --verify-no-changes   # comme en CI
dotnet run --project src/MiFlash.Cli -- version       # bannière
```

## Conventions

- Branches `feat/<module>` → PR vers `dev` ; `main` = merges taggés.
- Messages de commit : `type(périmètre): M<jalon>.<tâche> — description`
  (identifiants de `todo.md`).
- Aucun binaire tiers commité (`*.elf`, `*.dll`, loaders firehose fournis par
  l'utilisateur final — RNF-8).
- Le code sous `src/` (Windows) ne doit **jamais** être modifié : c'est la
  référence de portage.
