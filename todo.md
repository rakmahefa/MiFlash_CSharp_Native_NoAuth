# TODO — Portage Linux natif de MiFlash

> Checklist exécutable liée à `plan.md` (jalons M0→M6) et `spec-linux.md` (exigences RF/RNF).
> Cocher avec `git commit` référençant l'ID de la tâche (ex. `feat(core): M1.3`).

## État initial (fait)

- [x] A0.1 Analyse complète du code source (120 fichiers C#, ~14 350 lignes) — commit base `5450f4f`
- [x] A0.2 Inventaire des dépendances Windows (P/Invoke ×80+, WMI, Registry, setupapi, cmd.exe, MiUSB.dll, SLA_Challenge.dll…) → spec §3
- [x] A0.3 Identification du flux EDL complet (Sahara → Firehose → auth NoAuth blob 319 o → rawprogram/patch) → spec §2.2
- [x] A0.4 Création de la branche `dev` + rédaction `spec-linux.md`, `plan.md`, `todo.md`

## M0 — Hygiène du dépôt & socle build

- [ ] M0.1 Écrire `.gitignore` (bin/, obj/, *.user, *.suo, log/, *.pdb) et supprimer `src/bin` (59 Mo), `src/obj`, `*.csproj.user` du dépôt
- [ ] M0.2 Conserver : sources C de `ThirdParty/Qualcomm/fh_loader/QSaharaServer` (référence), `.inf` (doc), `LanguageLibrary.xml`
- [ ] M0.3 Créer la solution .NET 8 multi-projets : `MiFlash.Core`, `MiFlash.Edl`, `MiFlash.Transport.Usb`, `MiFlash.Transport.Serial`, `MiFlash.Fastboot`, `MiFlash.Cli`
- [ ] M0.4 CI GitHub Actions (ubuntu-latest) : `dotnet build` + `dotnet test` + `dotnet format --verify-no-changes`
- [ ] M0.5 README-linux (fr/en) : usage éducatif, avertissement, prérequis

## M1 — MiFlash.Core (protocoles, OS-agnostique)

- [ ] M1.1 Porter `XiaoMiFlash.code.module/**` (paquets Sahara, commandes Firehose XML, `CommandFormat`, `SoftwareImage`, `Sparse*`, `FlashType`, `Storage`, `UserInfo`, `FlashResult`) — supprimer les références WinForms/Win32
- [ ] M1.2 Définir `ITransport` (Open/Read/Write/Close/DiscardBuffers/IsOpen/PortName) et `IEdlAuthenticator`
- [ ] M1.3 Porter `Comm.cs` sur `ITransport` : ACK/NAK (`chkRspAck` simple + chunkCount), `GetResponse`/`GetResponseXml`, détection DDR4/DDR5 (peek `0xC3 0x00 …`), chip serial, `needEdlAuth`
- [ ] M1.4 Porter `FileTransfer.cs` : `WriteFile`/`ReadFile`/`WriteSparseFileToDevice`/`WriteFfuFile`/`SetBytesToFile`, chunk ACK
- [ ] M1.5 Remplacer `Encoding.Default` par UTF-8 explicite (audit : `Comm.SendCommand`, dumps)
- [ ] M1.6 `AppPaths` XDG (`~/.local/share|state/miflash/`) remplaçant `AppDomain…ApplicationBase + "Source\\…"`
- [ ] M1.7 Tests unitaires : golden packets (HELLO 48 o, HELLO_RESPONSE v2/mode 3, READ DATA 32/64 bits), fixtures `rawprogram/patch/provision` XML, image sparse de test, blob auth 319 o, patterns `SoftwareImage`
- [ ] M1.8 Tag `v0.1-m1` si CI verte

## M2 — Transport USB (libusb) + détection

- [ ] M2.1 Intégrer LibUsbDotNet ; énumération des 9008 (VID `05c6:9008`) + appareils fastboot (VIDs `2717`, `18d1`, `05c6`, `8087`, `0955`, `31ef` ; produit `android`/`fastboot` ; fabricant `xiaomi inc`)
- [ ] M2.2 `UsbTransport : ITransport` : claim interface, bulk IN/OUT, ZLP (`ZlpAwareHost="1"`), timeouts, discard buffers
- [ ] M2.3 Index appareil = (bus, port physique) — remplace `rundll32 qcCoInstaller` et parsing hub MiUSB
- [ ] M2.4 Hotplug insert/remove (libusb ou udev monitor) — remplace `ManagementEventWatcher`
- [ ] M2.5 `MiFlash.Transport.Serial` fallback (`qcserial` → `/dev/ttyUSB*`) via `System.IO.Ports`
- [ ] M2.6 Règles udev (`51-miflash.rules` : 05c6:9008 + 2717/18d1 fastboot, `MODE="0660"`, `TAG+="uaccess"`) + doc installation
- [ ] M2.7 Test matériel : HELLO reçu + programmer téléversé sur un 9008 réel

## M3 — Moteur EDL complet

- [ ] M3.1 Porter `SaharaDownloadProgrammer` (avec boucle de récupération « switch mode back » l. 390–444) en `async`
- [ ] M3.2 `ConfigureDDR` + gestion `needEdlAuth` → `StaticBlobAuthenticator` (blob 319 o de `dlAuth()`)
- [ ] M3.3 Peek CPUID → sélection programmer lite/DDR4/DDR5 (`prog_firehose_lite.elf`, `prog_ufs_firehose_sm8250_ddr_4/5.elf`) + détection UFS/eMMC
- [ ] M3.4 `BackupRom` / restore (`rawprogram_backup/restore.xml`) + `NeedBackupRom/NeedRestoreRom/NeedProvision/NeedDifferentiateDdr`
- [ ] M3.5 `Provision` (SkipStorageInit=1, partial_reset ou reset_to_edl + re-Sahara)
- [ ] M3.6 `FirehoseDownloadImg` : rawprogram*.xml → programs par secteurs (sparse + bruts), progression (`DummyProgress`)
- [ ] M3.7 `ApplyPatchesToDevice` : patch*.xml → `<patch>` (vérifier correctifs récents « Fix Bugs at PATCH Operations »)
- [ ] M3.8 `GetSha256Digest`, `Firmwarewrite`, `SetBootPartition` (UFS), `Reboot` (reset/partial_reset/reset_to_edl)
- [ ] M3.9 Multi-appareils parallèles (Task/appareil, CancellationToken, kill propre)
- [ ] M3.10 Validation matérielle matrice README : SD 636 / 680 / 845 / 855 / 860 / 870 → rapport de test dans `docs/`

## M4 — CLI

- [ ] M4.1 Squelette `miflash` (System.CommandLine + Spectre.Console) : `devices|flash|backup|restore|provision|reboot|doctor|version`
- [ ] M4.2 `miflash devices [--json] [--watch]` (RF-1/RF-2)
- [ ] M4.3 `miflash flash <dir> [--programmer <f>] [--device <sn>] [--read-dump] [--write-dump]` (RF-3→RF-11)
- [ ] M4.4 Confirmations erase/program (`--yes`), avertissement usage éducatif
- [ ] M4.5 Logs par appareil `~/.local/state/miflash/log/` + log résultat global (port `Log.cs`)
- [ ] M4.6 i18n : port `LanguageProvider` + `LanguageLibrary.xml` (fr/en/zh)
- [ ] M4.7 `miflash doctor` : libusb, udev, permissions, fastboot présent, .NET OK

## M5 — Fastboot

- [ ] M5.1 `ProcessRunner` POSIX (port de `Cmd.cs` sans cmd.exe) : sortie ligne à ligne, timeout, kill arborescence
- [ ] M5.2 Détection fastboot : `fastboot devices` + corrélation libusb (serial ↔ bus/port)
- [ ] M5.3 Moteur de scripts natif : parsing `flash_all*.bat`/`.sh` en opérations (`fastboot flash -s …`), choix clean / except-storage / lock
- [ ] M5.4 Timeout global 11 min 40 s + kill (port `ScriptDevice.flash()`)
- [ ] M5.5 Cas Intel DNX : `fastboot boot loader.efi` (kernelflinger)
- [ ] M5.6 `miflash flash-fastboot <rom> [--script auto]`

## M6 — Packaging / GUI / durcissement

- [ ] M6.1 `dotnet publish` self-contained linux-x64 (+ arm64 si supporté), tarball
- [ ] M6.2 Paquets `.deb` / `.rpm`, AUR ; installation udev rules + reload
- [ ] M6.3 (option) GUI Avalonia 11 : liste appareils, progression, sélection répertoire
- [ ] M6.4 Docs utilisateur fr/en complètes + matrice matériel
- [ ] M6.5 Audit sécurité final (chemins, encodages, droits, clés) + tag `v1.0.0`

## Hors périmètre v1 (explicitement écartés — ne pas porter)

- MES/X5Mes (`X5MesLibrary.dll`), `iacj_interprocess_dll`, `PUB_TEST_FUNC_DLL`, `RoutingObject`
- Tegra `nvflash`, MediaTek `FlashToolLib`, JR/NSL `flash-loader`
- `ShareMemory` (IPC usine), OleDb/Excel, login compte Xiaomi (`Authentication.cs`), FTP usine (`FtpHelper`/`SwDownloader`)
- Installation de drivers Windows (`MiDriver`, `Driver`, setupapi) → remplacée par udev

## Blocages courants

| Symptôme | Piste |
|---|---|
| `error: only nop and sig tag…` | Auth NoAuth non déclenchée → vérifier `StaticBlobAuthenticator` (taille 319 o annoncée via `AUTHP`) |
| Pas de ACK après `<configure>` | Payload trop grand pour le loader (NAK 16384) → gérer comme `Comm.GetResponse` l. 374 |
| HELLO jamais reçu | Port 9008 non ouvert (permissions udev) ou interface non claimée → `miflash doctor` |
| Mauvais programmer | Fichier ne matchant pas `prog_.*_firehose_.*.elf` / DDR choisi → `--programmer` explicite |
