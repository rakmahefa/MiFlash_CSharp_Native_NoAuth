# MiFlash — Portage natif Linux

> **FR** · [English below](#english)

## ⚠️ Avertissement (lire en premier)

Outil à **usage éducatif uniquement**. Le flash EDL écrase la mémoire de
l'appareil et peut le rendre **définitivement inutilisable (brick)**. Ce projet
n'est ni affilié à Xiaomi ni à Qualcomm. Vous êtes seul responsable de ce que
vous flashez. Les loaders firehose (`prog_*_firehose_*.elf`) ne sont pas
distribués ici : vous devez fournir les vôtres.

## État du portage

| Jalon | Contenu | État |
|---|---|---|
| M0 | Hygiène du dépôt, solution .NET 8, CI | ✅ fait |
| M1 | `MiFlash.Core` : protocoles Sahara/Firehose, `ITransport`, auth NoAuth, tests golden | 🚧 en cours |
| M2 | Transport USB libusb (9008), udev, hotplug | ⏳ |
| M3 | Moteur EDL complet (rawprogram/patch, provision, backup) | ⏳ |
| M4 | CLI `miflash` | ⏳ |
| M5 | ROM fastboot (`flash_all*`) | ⏳ |
| M6 | Packaging (deb/rpm/tarball), durcissement | ⏳ |

Détails : [`spec-linux.md`](spec-linux.md) · [`plan.md`](plan.md) · [`todo.md`](todo.md).

## Démarrage rapide (développeur)

```bash
# Prérequis : .NET SDK 8, libusb-1.0-0
cd miflash-linux
dotnet build MiFlash.Linux.sln
dotnet test  MiFlash.Linux.sln
```

La CI (GitHub Actions, `ubuntu-latest`) build + teste chaque push : voir
`.github/workflows/ci.yml`.

## Structure du dépôt

```
miflash-linux/   solution .NET 8 du port Linux (code actif)
src/             code C#/WinForms Windows historique (référence, ne pas modifier)
reference/       sources C Qualcomm (fh_loader/QSaharaServer), .inf, LanguageLibrary.xml
spec-linux.md    spécification du portage (dépendances Windows → Linux)
plan.md          plan d'implémentation M0→M6
todo.md          checklist exécutable
```

---

## English

### ⚠️ Warning (read first)

**Educational use only.** EDL flashing overwrites device storage and can
**permanently brick** your device. This project is not affiliated with Xiaomi
or Qualcomm. You are solely responsible for what you flash. Firehose loaders
(`prog_*_firehose_*.elf`) are **not** distributed here — bring your own.

### Status

See the table above (M0 done → M6 planned). Details in
[`spec-linux.md`](spec-linux.md), [`plan.md`](plan.md), [`todo.md`](todo.md)
(French).

### Quick start (developer)

```bash
# Prerequisites: .NET SDK 8, libusb-1.0-0
cd miflash-linux
dotnet build MiFlash.Linux.sln
dotnet test  MiFlash.Linux.sln
```

CI (GitHub Actions, `ubuntu-latest`) builds and tests every push:
`.github/workflows/ci.yml`.
