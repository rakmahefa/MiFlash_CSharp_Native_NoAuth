# Spécification — Portage natif Linux de MiFlash (MiFlash_CSharp_Native_NoAuth)

> **Statut :** v0.1 (draft) — Branche `dev`
> **Base d'analyse :** commit `5450f4f` (« Fix Bugs at PATCH Operations »), 120 fichiers C#, ~14 350 lignes
> **Langue cible :** C# / .NET 8 sur Linux (port natif, sans Wine, sans VM)

---

## 1. Contexte et objectifs

`MiFlash_CSharp_Native_NoAuth` est une reconstruction C# de l'outil Xiaomi MiFlash
(décompilé), orientée **Qualcomm EDL (mode 9008)** avec contournement de l'authentification
Xiaomi (« NoAuth ») via un blob de signature statique et des firehose loaders « eng ».
Le README indique un bypass validé sur SD 636 / 680 / 845 / 855 / 860 / 870.

**Objectif du portage :** obtenir un outil **natif Linux** capable de :

1. détecter un appareil Qualcomm en mode EDL (9008) et en mode fastboot ;
2. téléverser le programmer (Sahara) puis flasher via Firehose (rawprogram/patch XML,
   images sparse, provision, backup/restore, vérification SHA-256, patches) ;
3. flasher des ROM fastboot (scripts `flash_all*.bat` transposés) ;
4. fonctionner sans Wine ni binaire Windows, en CLI d'abord, GUI ensuite.

**Non-objectifs (v1) :** MES/usine (X5Mes, Mes, SocketChatClient, EDL_SA_COMMUNICATION),
flashing Tegra (nvflash), MediaTek (FlashToolLib), outils Foxconn/Longcheer/Inventec
(`PUB_TEST_FUNC_DLL.dll`, `RoutingObject.dll`), login compte Xiaomi (`Authentication.cs`).

---

## 2. Inventaire du code existant

### 2.1 Structure (dossier `src/`)

| Dossier | Rôle | Portabilité Linux |
|---|---|---|
| `XiaoMiFlash/` | `Program.cs`, `MainFrm.cs` (2 768 l.), `MiInstaller.cs`, dialogs Vista (COM) | ❌ À remplacer (UI) |
| `XiaoMiFlash.form/` | 9 formulaires (config, process, driver, download…) | ❌ À remplacer (UI) |
| `XiaoMiFlash.code.MiControl/` | Contrôles WinForms custom | ❌ À remplacer (UI) |
| `XiaoMiFlash.code.module/` | Protocoles Sahara + Firehose, paquets, formats d'image (57 fichiers) | ✅ **Portable à ~95 %** |
| `XiaoMiFlash.code.bl/` | Moteurs de flash : `SerialPortDevice.cs` (1 548 l.), `ScriptDevice.cs`, `FileTransfer.cs` (622 l.), drivers, MES | ⚠️ Cœur portable, périphérie Windows |
| `XiaoMiFlash.code.Utility/` | `Comm.cs` (511 l.), `Cmd.cs` (643 l.), USB/WMI/setupapi, logs, FTP, INI | ⚠️ Mixte |
| `XiaoMiFlash.code.authFlash/` | `EDL_SLA_Challenge.cs` (P/Invoke `SLA_Challenge.dll`), `EDL_SA_COMMUNICATION.cs` | ⚠️ À remplacer par un signer logiciel |
| `XiaoMiFlash.code.data/` | `FlashingDevice`, `MiFlashGlobal`, `MemImg`, `SaveFfu` | ✅ Portable |
| `XiaoMiFlash.code.lan/` | i18n XML (`LanguageProvider`) | ✅ Portable (chemin à adapter) |
| `XiaoMiFlash.Properties/` | Resources/settings | ⚠️ Lié WinForms |

### 2.2 Flux EDL implémenté (à conserver tel quel)

Le flux complet observé dans `SerialPortDevice.XiaomiFlashNew()` (l. 117–235) :

1. Ouverture du port 9008 (`new SerialPort(deviceName, 9600)` — le baudrate est ignoré en pratique, le port est un bulk USB) ;
2. `SaharaDownloadProgrammer()` : lecture `SAHARA HELLO` (48 o, version 2/1, mode 3), réponse `HELLO RESPONSE`, puis boucle `READ DATA` → envoi du fichier `prog_*_firehose_*.elf` par blocs, jusqu'à `DONE` ;
3. `ConfigureDDR()` : commande XML `<configure>` (sector 4096 UFS / 512 eMMC, `MaxPayloadSizeToTargetInBytes` = 1 Mo, `SkipStorageInit`) ;
4. **Auth NoAuth** : si NAK `« error: only nop and sig tag can be received before authentication »` → `dlAuth()` renvoie un **blob statique de 319 octets** (hardcodé l. 678) via `AUTHP`/`AUTH` — c'est le cœur du bypass, 100 % protocole donc portable ;
5. `peek` CPUID → sélection DDR4 (`0xC3 0x00 0x40/0x31/0x02 0x10`) ou DDR5 (`0x00/0x51/0x32 0x10`) → `prog_ufs_firehose_sm8250_ddr_4/5.elf` ;
6. `BackupRom()` (rawprogram_backup.xml) puis `Provision()` (provision*.xml, SkipStorageInit=1, re-reset EDL) ;
7. `FirehoseDownloadImg()` : parsing `rawprogram[0-9]{1,20}.xml` → `<program>` par secteur, gestion des images **sparse** (`parsingheader`, `CekSparse`) et des fichiers `.mbn/.img/.elf` bruts via `FileTransfer` ;
8. `ApplyPatchesToDevice()` : `patch[0-9]{1,20}.xml` → `<patch>` (offsets, valeurs) ;
9. UFS : `SetBootPartition()` (`setbootablestoragedrive value="1"`) ;
10. `Partial_Reset` / `Reset_To_Edl` / `Reset` final.

Flux fastboot (`ScriptDevice.fastbootflash()`) : exécution de `flash_all*.bat`
(`pushd … && set PATH=…Android;%PATH% && script -s <SN>`), cas spécial Intel DNX
(`fastboot boot loader.efi`), collecte `oem lkmsg/lpmsg` vers `C:\fastbootlog`.

### 2.3 Authentification EDL

- `EDL_SLA_Challenge.cs` : 7 imports `SLA_Challenge.dll` (Xiaomi, fermé) —
  `StartLoginProcess`, `get_user_info`, `SLA_Challenge(Ex)`, `can_flash`… Utilisé par
  le MiFlash officiel ; **non requis** dans la variante NoAuth (blob statique).
- Le port Linux remplace cela par une interface `IEdlAuthenticator` avec, pour v1,
  l'implémentation « StaticBlobAuthenticator » (le blob 319 o du code actuel).

---

## 3. Cartographie des dépendances Windows → Linux

| # | Dépendance Windows | Fichiers concernés | Équivalent Linux retenu |
|---|---|---|---|
| 1 | **WinForms net40** (`Microsoft.NET.Sdk.WindowsDesktop`, x86) | `MiFlash.csproj`, tous les forms | CLI (`System.CommandLine` + `Spectre.Console`) en v1 ; GUI **Avalonia 11** en phase finale |
| 2 | **`System.IO.Ports` sur COMx** (driver `qcusbser.sys` expose le 9008 en série) | `Comm.cs`, `SerialPortDevice.cs`, `FileTransfer.cs` | **libusb-1.0** (bulk IN/OUT direct) via `LibUsbDotNet` ; option fallback `/dev/ttyUSB*` via module noyau `qcserial` |
| 3 | **WMI** (`Win32_PnPEntity` « Qualcomm HS-USB QDLoader 9008 », `ManagementEventWatcher` USB insert/remove) | `ComPortCtrl.cs`, `MainFrm.cs` | Énumération **libusb** (VID `05c6:9008`) + hotplug libusb / `udev` monitor (`NetLink`/`GUDev`) |
| 4 | **`MiUSB.dll`** (assembly .NET Xiaomi : `USB.AllHostControllers`, `UsbNodeConnectionInformation`, descriptors, `GetPDLUsbDevice`) | `UsbDevice.cs`, `TreeViewUsbItem.cs`, `MainFrm.cs` | `LibUsbDotNet` : énumération bus/ports, `descriptor.SerialNumber` ; index = bus/port physique |
| 5 | **`cmd.exe`** + scripts `.bat` | `Cmd.cs` (l. 594 : `FileName = "cmd.exe"`), `ScriptDevice.cs` | Exécuteur de commandes portable (`Process` + `/bin/sh`) **et** moteur de script natif : parsing direct de `flash_all*.bat/.sh` en opérations `fastboot` |
| 6 | **`fastboot.exe` / `adb.exe`** (bundled Google) | `Script.cs`, `UsbDevice.cs`, `ScriptDevice.cs` | `android-tools` (paquet distro) en v1 ; client fastboot natif C# (libusb) en v2 |
| 7 | **setupapi / cfgmgr32** (`SetupOEMInf`, `SetupDiGetClassDevs`…) | `Driver.cs`, `DeviceInfo.cs`, `DeviceClasses.cs` | **Supprimé** : pas de drivers à installer ; règles **udev** livrées (voir §8) |
| 8 | **Registry** (`HKLM\Software\XiaoMi\MiFlash`, `Wow6432Node`) | `MiDriver.cs`, `UsbDevice.cs` (l. 190) | Configuration fichier : `~/.config/miflash/config.json` (+ `/etc/miflash/`) |
| 9 | **`rundll32.exe qcCoInstaller.dll,qcGetDeviceIndex`** | `UsbDevice.cs` (l. 208) | Calcul direct : index = (bus USB, port) — aucune DLL requise |
| 10 | **`SLA_Challenge.dll`** (P/Invoke ×7) | `EDL_SLA_Challenge.cs` | Interface `IEdlAuthenticator` ; v1 = blob statique NoAuth (déjà en C# managé dans `dlAuth()`) |
| 11 | **Shared memory kernel32** (`CreateFileMapping`) | `ShareMemory.cs` | HORS PÉRIMÈTRE v1 (usage usine) ; si besoin : `MemoryMappedFile` POSIX |
| 12 | **`X5MesLibrary.dll`** (×14), **`iacj_interprocess_dll.dll`** (×3), **`PUB_TEST_FUNC_DLL`**, `RoutingObject.dll` | `X5Mes.cs`, `EDL_SA_COMMUNICATION.cs`, csproj | HORS PÉRIMÈTRE (intégrations usine) — désactiver par drapeaux de compilation |
| 13 | **OleDb + Jet/ACE** (lecture .xls/.xlsx) | `MainFrm.cs` (l. 2137–2146) | HORS PÉRIMÈTRE v1 ; si besoin : `NPOI`/`ClosedXML` |
| 14 | **`lsusb.exe` (Qualcomm fh_loader)** — parse ses sorties COM | `ComPortCtrl.cs` | `lsusb` réel / sysfs `/sys/bus/usb/devices` + `/dev/serial/by-id` |
| 15 | **`fh_loader.exe`, `QSaharaServer.exe`, `nvflash.exe`, flash-loader JR/NSL, msiexec, python-2.7.13.msi** | `Script.cs`, ThirdParty | **Inutiles** : Sahara/Firehose déjà implémentés en C# ; nvflash (Tegra) hors périmètre |
| 16 | **VistaDialogCore** (COM shell : IFileDialog, credui, dwmapi, UxTheme…) | `XiaoMiFlash/VistaDialogCore/*` | Supprimé — `Spectre.Console` / file pickers Avalonia |
| 17 | **INI par kernel32** (`GetPrivateProfileString`) | `IniFile.cs` | Parser INI managé ou JSON |
| 18 | **Chemins Windows** : `AppDomain…ApplicationBase + "Source\\ThirdParty\\…"`, `C:\fastbootlog`, `log\` sous l'exe | `Script.cs`, `ScriptDevice.cs` (l. 253), `Log.cs`, `LanguageProvider.cs` | `Path.Combine` + XDG : données sous `~/.local/share/miflash/`, logs sous `~/.local/state/miflash/log/` |
| 19 | **`requireAdministrator`** (app.manifest) | `app.manifest` | Pas de root nécessaire si règles udev ; sinon `sudo` explicite avec message clair |
| 20 | **`Encoding.Default`** (ANSI/GBK sur Windows) | `Comm.cs` (l. 215), `Authentication.cs` (gb2312) | `Encoding.UTF8` explicite partout ; vérifier les dumps hex/ASCII du protocole |
| 21 | **Threads bruts + `Thread.Sleep` de polling** | `SerialPortDevice.cs`, `Comm.cs` | `async/await` + `Task.Delay`, `CancellationToken` |
| 22 | **Binaires commités** (`src/bin` 59 Mo, `src/obj`, logs) | repo | À retirer (`.gitignore`) — les loaders firehose restent fournis par l'utilisateur |

### 3.1 Ce qui se porte quasi tel quel (code managé pur)

- `XiaoMiFlash.code.module/**` : paquets Sahara (`sahara_*_packet/response`), commandes
  Firehose XML (`Firehose.cs`), `CommandFormat` (struct↔bytes), `SoftwareImage`
  (patterns : `prog_.*_firehose_*.elf`, `rawprogram[0-9]+.xml`, `patch[0-9]+.xml`,
  `provision.*\.xml`, `MTK_AllInOne_DA*.bin`), `Sparse`, `FlashType`, `Storage`, `UserInfo`, `FlashResult` ;
- `FileTransfer.cs` (lecture/écriture par secteurs, sparse, FFU) — découpler de `SerialPort` via une interface `ITransport` ;
- `Comm.cs` (logique ACK/NAK, parsing XML des réponses, détection DDR4/5, chip serial) — idem ;
- `SerialPortDevice.cs` : l'orchestration (séquence §2.2) est réutilisable en remplaçant `SerialPort` par `ITransport` ;
- `FlashingDevice`, `MiFlashGlobal`, `Log.cs`, `FileSearcher.cs`, `TimeOut.cs`, `CRC.cs`, `LanguageProvider.cs`.

---

## 4. Architecture cible

```
miflash-linux/                     (solution .NET 8, C# 12)
├── MiFlash.Core/                  # netstandard2.0/net8.0 — AUCUNE dépendance OS
│   ├── Sahara/                    # paquets + machine à états (port direct code.module)
│   ├── Firehose/                  # commandes XML, parsing réponses (port direct)
│   ├── Images/                    # sparse, rawprogram/patch XML, provision, FFU, GPT
│   ├── Transport/ITransport.cs    # Open/Read/Write/Close + events data
│   └── Auth/IEdlAuthenticator.cs  # StaticBlobAuthenticator (NoAuth) par défaut
├── MiFlash.Transport.Usb/         # net8.0 + LibUsbDotNet : 9008 bulk, fastboot iface
├── MiFlash.Transport.Serial/      # net8.0 + System.IO.Ports (fallback qcserial)
├── MiFlash.Edl/                   # moteur EDL : port de SerialPortDevice/FileTransfer/Comm
├── MiFlash.Fastboot/              # exécuteur fastboot (process android-tools) + parsing .bat/.sh
├── MiFlash.Cli/                   # `miflash devices|flash|backup|provision|reboot|version`
└── MiFlash.Gui/                   # (phase ultérieure) Avalonia 11
```

**Décisions structurantes :**

- **D1 — CLI d'abord.** Le cœur de valeur est EDL ; la GUI WinForms n'est pas portable et n'apporte rien au flux usine/repair. La GUI Avalonia viendra en dernier.
- **D2 — libusb prioritaire sur qcserial.** Le 9008 est une interface bulk ; passer par le noyau (qcserial → /dev/ttyUSB*) reste possible mais dépend du module. L'approche libusb (identique à `bkerler/edl`) garantit le contrôle des ZLP et timeouts.
- **D3 — Protocole inchangé.** Aucune réécriture du protocole : on conserve les paquets, timeouts et séquences observés (y compris le handshake « switch mode back » de récupération, `SaharaDownloadProgrammer` l. 390–444).
- **D4 — Auth en interface.** Le blob statique 319 o est l'implémentation par défaut ; un futur signer (PKCS#11, fichier d'octets, service externe) se branchera sans toucher au moteur.
- **D5 — Fastboot par processus en v1** (le `fastboot` du système), natif C# en v2 ; le parsing des scripts `flash_all*` devient indépendant de l'OS.

---

## 5. Exigences fonctionnelles

| ID | Exigence | Source (code actuel) |
|---|---|---|
| RF-1 | Lister les appareils : EDL 9008 (VID `05c6:9008`), fastboot (VIDs `2717`, `18d1`, `05c6`, `8087`, `0955`, `31ef` ; produit `android`/`fastboot`), avec serial number et index bus/port | `UsbDevice.GetAndroidDevices()` |
| RF-2 | Détecter à chaud l'insertion/retrait (hotplug) | `MainFrm.USBInsertHandler` (WMI) |
| RF-3 | Téléverser le programmer Sahara (mode 3, version 2/1), avec récupération « switch mode back » | `SaharaDownloadProgrammer()` |
| RF-4 | Sélection auto du programmer : lite / DDR4 / DDR5 (peek CPUID), UFS vs eMMC (nom de fichier) | `SaharaDownloadProgrammer()` l. 459–498 |
| RF-5 | Authentifier en EDL via blob statique si le loader exige `sig` | `dlAuth()`, `Comm.needEdlAuth` |
| RF-6 | Flasher un répertoire d'images : `rawprogram*.xml` + `patch*.xml`, images brutes et sparse, secteurs 4096/512, payload 1 Mo | `FirehoseDownloadImg`, `FileTransfer` |
| RF-7 | Provision (provision*.xml) avec SkipStorageInit + re-reset EDL | `Provision()` |
| RF-8 | Backup/restore de partitions (`rawprogram_backup/restore.xml`) | `BackupRom`, `ReadFilesToDevice`, `WriteFilesToDevice` |
| RF-9 | Vérifier l'intégrité post-flash (`getsha256digest`) | `GetSha256Digest()` |
| RF-10 | `firmwarewrite` (mode spécial) | `Firmwarewrite()` |
| RF-11 | Actions finales : `setbootablestoragedrive` (UFS), `reset` / `partial_reset` / `reset_to_edl` | `SetBootPartition`, `Reboot` |
| RF-12 | Flasher une ROM fastboot : sélection du script (`flash_all.bat` ↔ `flash_all.sh`), exécution par appareil (`-s <SN>`), timeout global (11 min 40 s) + kill process tree | `ScriptDevice.flash()` |
| RF-13 | Barre de progression par appareil (mapping secteurs/partitions) | `SoftwareImage.DummyProgress`, `FlashingDevice` |
| RF-14 | Logs horodatés par appareil + log résultat global, dump hexa optionnel read/write | `Log.cs`, `comm.isReadDump/isWriteDump` |
| RF-15 | i18n : charger `LanguageLibrary.xml` (fr/en/zh) | `LanguageProvider.cs` |
| RF-16 | Multi-appareils simultanés (1 thread/appareil → 1 Task/appareil) | `Device.DThread` |

## 6. Exigences non fonctionnelles

- **RNF-1** .NET 8 LTS, publish single-file self-contained `linux-x64` (et `linux-arm64` si LibUsbDotNet le supporte).
- **RNF-2** Aucune élévation de privilèges requise quand les règles udev sont installées ; message d'aide sinon.
- **RNF-3** Débit EDL ≥ 8 Mo/s sustained sur USB 2.0 (payload 1 Mo, ZLP aware `ZlpAwareHost="1"`).
- **RNF-4** Encodages explicites (UTF-8) ; aucun `Encoding.Default`.
- **RNF-5** Code protocole testé unitairement sans matériel (paquets golden, fixtures XML).
- **RNF-6** Journalisation structurée, répertoires XDG, chemins 100 % `Path.Combine`.
- **RNF-7** Déterminisme : sorties CLI stables (`--json` pour intégration).
- **RNF-8** Licence : ne pas redistribuer les binaires Windows third-party ; les loaders firehose restent fournis par l'utilisateur final (usage éducatif, cf. README).

## 7. Prérequis système Linux

1. `libusb-1.0-0` (runtime) ; `udev` présent sur toutes les distros cibles.
2. Règles udev livrées (`/usr/lib/udev/rules.d/51-miflash.rules`) :
   `SUBSYSTEM=="usb", ATTRS{idVendor}=="05c6", ATTRS{idProduct}=="9008", MODE="0660", TAG+="uaccess"`
   (+ `2717` fastboot, `18d1`, et `MODE="0660"` pour adb).
3. Fallback série : module noyau `qcserial` (optionnel) → `/dev/ttyUSB*`.
4. `android-tools-adb` / `android-tools-fastboot` (v1 fastboot) dans le PATH.
5. .NET 8 runtime OU build self-contained (recommandé).

## 8. Critères d'acceptation (v1)

1. `miflash devices` liste un 9008 branché avec son serial, sans root, avec udev.
2. `miflash flash --dir images/ --device <sn>` flashe un jeu `rawprogram+patch` sur un
   appareil de la matrice (SD 636→870) et aboutit à « flash done » avec reset final.
3. Le handshake d'auth NoAuth se déclenche et réussit sur un loader exigeant `sig`.
4. `miflash backup/restore` et `getsha256digest` fonctionnent sur partition userdata.
5. `miflash flash-fastboot --rom <dir>` exécute le script équivalent `flash_all` sur
   un appareil fastboot, avec timeout et kill de l'arborescence de processus.
6. 100 % du code `MiFlash.Core` compile sur `dotnet build` Linux sans avertissement Windows-only.
7. CI GitHub Actions (ubuntu-latest) : build + tests unitaires verts sans matériel.

## 9. Risques et questions ouvertes

| Risque | Impact | Mitigation |
|---|---|---|
| Différences de timing bulk USB vs COM Windows (ACK groupés, ZLP) | Flash instable | Conserver les boucles de retry d'origine (`chkRspAck` ×10, écritures ×6) ; tester tôt sur matériel réel |
| Loaders firehose « eng » introuvables pour certains SoC | Appareils non supportés | Documenter la liste README (SD 636→870) ; permettre `--programmer` explicite |
| `Encoding.Default` → UTF-8 peut altérer les comparaisons de dumps | Parsing de réponses | Tests golden packets binaires |
| Le blob NoAuth statique peut être rejeté par des loaders plus récents (SD 8 Gen+) | Périmètre limité | Interface `IEdlAuthenticator` extensible |
| Suppression implicite de données (erase/program) | Sécurité utilisateur | Confirmation interactive + `--yes` en CLI, avertissements anti-rollback |
| Cadre légal du bypass EDL | Usage éducatif uniquement | Avertissement explicite dans README/CLI du port |
