using System;
using System.IO;
using System.Threading;
using System.Xml;
using MiFlash.Core.Auth;
using MiFlash.Core.Firehose;
using MiFlash.Core.Images;
using MiFlash.Core.Io;
using MiFlash.Core.Protocol;
using MiFlash.Core.Runtime;
using MiFlash.Core.Sahara;
using MiFlash.Core.Transfer;
using MiFlash.Core.Transport;

namespace MiFlash.Edl;

/// <summary>
/// Moteur EDL complet (M3) — port de <c>SerialPortDevice.cs</c> (l. 45–575,
/// 973–1336, 1448–1530). Séquence : Sahara (téléversement du programmer avec
/// récupération « switch mode back ») → ping firehose → configure DDR
/// (+ auth NoAuth via <see cref="IEdlAuthenticator"/> si le loader exige
/// <c>sig</c>) → peek CPUID (choix DDR4/5 si nécessaire) →
/// <c>setbootablestoragedrive</c> (UFS) → rawprogram*.xml + patch*.xml →
/// <c>partial_reset</c>.
/// Écarts v1 documentés : moteur synchrone (Thread.Sleep, fidèle à
/// l'original — async reporté au durcissement M6) ; backup/provision/
/// firmwarewrite/report SHA-256 au prochain passage (todo M3.4/M3.5/M3.8).
/// </summary>
public sealed class EdlFlasher
{
    private const int BUFFER_SECTORS = 256;

    private const int TIME_WAIT_FIREHOSE_START = 1000;

    /// <summary>Programmer demandé : 0=motif générique (défaut, comme l'original),
    /// 1=lite, 2=DDR4, 3=DDR5 (cf. FirehoseCommands).</summary>
    public int programmerName;

    public string storageType = Storage.ufs;

    public bool isLite;

    private int m_iSkipStorageInit;

    public bool verbose = true;

    public bool readBackVerify;

    public Comm comm;

    public string deviceName;

    public string swPath;

    /// <summary>Authentificateur EDL (v1 : blob statique NoAuth 256 o).</summary>
    public IEdlAuthenticator Authenticator { get; set; } = new StaticBlobAuthenticator();

    public EdlFlasher(string deviceName, string swPath, ITransport transport)
    {
        this.deviceName = deviceName ?? throw new ArgumentNullException(nameof(deviceName));
        this.swPath = swPath ?? throw new ArgumentNullException(nameof(swPath));
        comm = new Comm(transport ?? throw new ArgumentNullException(nameof(transport)));
    }

    /// <summary>Point d'entrée : flashe le répertoire d'images (RF-3→RF-11).</summary>
    public void Flash()
    {
        if (string.IsNullOrEmpty(deviceName))
        {
            return;
        }
        if (!Directory.Exists(swPath))
        {
            throw new Exception("sw path is not valid");
        }
        // « images » sous-répertoire prioritaire (flash() l.58–67).
        foreach (DirectoryInfo directoryInfo in new DirectoryInfo(swPath).GetDirectories())
        {
            if (directoryInfo.Name.ToLowerInvariant().Contains("images"))
            {
                swPath = directoryInfo.FullName;
                break;
            }
        }
        if (NeedProvision(swPath))
        {
            m_iSkipStorageInit = 1;
        }
        try
        {
            comm.StartReading();
            SaharaDownloadProgrammer(programmerName);
            comm.StopReading();
            Thread.Sleep(1000);
            Ping();
            ConfigureDDR(comm.intSectorSize, BUFFER_SECTORS, storageType, m_iSkipStorageInit);

            // Peek CPUID : si les deux programmers DDR existent et qu'aucune
            // génération n'est encore détectée, interroger la cible puis
            // re-Sahara avec le programmer adapté (RF-4).
            if (NeedDifferentiateDdr(swPath) && !comm.isDdr4 && !comm.isDdr5)
            {
                DetectDdrGeneration();
                int? target = comm.isDdr4 ? FirehoseCommands.PROGRAMMER_DDR4
                          : comm.isDdr5 ? FirehoseCommands.PROGRAMMER_DDR5
                          : (int?)null;
                if (target.HasValue && target.Value != programmerName)
                {
                    ResetToEdl();
                    programmerName = target.Value;
                    SaharaDownloadProgrammer(programmerName);
                    Thread.Sleep(TIME_WAIT_FIREHOSE_START);
                    Ping();
                    ConfigureDDR(comm.intSectorSize, BUFFER_SECTORS, storageType, m_iSkipStorageInit);
                }
            }

            if (storageType == Storage.ufs)
            {
                SetBootPartition();
            }
            FirehoseDownloadImg(swPath);
            PartialReset();
            FlashStatus.UpdateDeviceStatus(deviceName, 1f, "flash done", "success", isDone: true);
        }
        catch (Exception ex)
        {
            FlashStatus.UpdateDeviceStatus(deviceName, null, ex.Message, "error", isDone: true);
            throw;
        }
        finally
        {
            comm.Close();
        }
    }

    // ------------------------------------------------------------- Sahara ----

    /// <summary>
    /// Téléverse le programmer via Sahara (M3.1), avec la boucle de récupération
    /// « switch mode back » (l. 390–444) : HELLO RESPONSE mode 3 puis
    /// SWITCH MODE, jusqu'à 10 essais.
    /// </summary>
    public void SaharaDownloadProgrammer(int programmertype)
    {
        string msg;
        if (!comm.IsOpen)
        {
            Log.w(comm.PortName, "port " + comm.PortName + " is not open.");
            throw new Exception("port " + comm.PortName + " is not open.");
        }
        var sahara_packet = default(SaharaPackets.sahara_packet);
        var sahara_hello_packet = default(SaharaPackets.sahara_hello_packet);
        var sahara_hello_response = default(SaharaPackets.sahara_hello_response);
        var sahara_switch_Mode_packet = default(SaharaPackets.sahara_switch_Mode_packet);
        var sahara_end_transfer_packet = default(SaharaPackets.sahara_end_transfer_packet);
        var sahara_done_response = default(SaharaPackets.sahara_done_response);
        int num = 10;
        while (num-- > 0 && sahara_hello_packet.Command != SaharaCommand.SAHARA_HELLO_PACKET)
        {
            msg = "cannot receive hello packet,MiFlash is trying to reset status!";
            Log.w(comm.PortName, msg);
            FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, msg, "flashing", isDone: false);
            comm.getRecDataIgnoreExcep();
            if (comm.recData == null || comm.recData.Length == 0)
            {
                comm.recData = new byte[48];
            }
            sahara_hello_packet = CommandFormat.BytesToStuct<SaharaPackets.sahara_hello_packet>(comm.recData);
            Thread.Sleep(500);
            if (sahara_hello_packet.Command == SaharaCommand.SAHARA_HELLO_PACKET)
            {
                continue;
            }
            msg = "try to reset status.";
            Log.w(comm.PortName, msg);
            FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, msg, "flashing", isDone: false);
            if (!comm.transport.IsOpen)
            {
                comm.transport.Close();
                Log.w(comm.PortName, "port ferme, tentative de reouverture");
                comm.transport.Open();
            }
            if (comm.IsOpen)
            {
                Log.w(comm.PortName, "nettoyage des tampons, envoi SAHARA_SWITCH_MODE_PACKET");
                comm.transport.DiscardBuffers();
            }
            // Switch mode back (l. 425–443) : HELLO RESPONSE version 2/min 1/mode 3.
            sahara_hello_response = default(SaharaPackets.sahara_hello_response);
            sahara_hello_response.Reserved = new uint[6];
            sahara_hello_response.Command = SaharaCommand.SAHARA_HELLO_RESPONSE;
            sahara_hello_response.Length = 48u;
            sahara_hello_response.Version = 2u;
            sahara_hello_response.Version_min = 1u;
            sahara_hello_response.Mode = 3u;
            byte[] array2 = CommandFormat.StructToBytes(sahara_hello_response);
            comm.WritePort(array2, 0, array2.Length);
            comm.getRecDataIgnoreExcep();
            msg = "Switch mode back";
            Log.w(comm.PortName, msg);
            FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, msg, "flashing", isDone: false);
            sahara_switch_Mode_packet.Command = SaharaCommand.SAHARA_SWITCH_MODE_PACKET;
            sahara_switch_Mode_packet.Length = 12u;
            sahara_switch_Mode_packet.Mode = 0u;
            array2 = CommandFormat.StructToBytes(sahara_switch_Mode_packet, 12);
            comm.WritePort(array2, 0, array2.Length);
        }
        if (sahara_hello_packet.Command != SaharaCommand.SAHARA_HELLO_PACKET)
        {
            msg = "cannot receive hello packet";
            comm.Close();
            throw new Exception(msg);
        }
        msg = "received hello packet";
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, msg, "flashing", isDone: false);
        Log.w(comm.PortName, msg);
        // HELLO RESPONSE (chemin nominal, l. 450–457) : version 2 / min 1.
        sahara_hello_response = default(SaharaPackets.sahara_hello_response);
        sahara_hello_response.Reserved = new uint[6];
        sahara_hello_response.Command = SaharaCommand.SAHARA_HELLO_RESPONSE;
        sahara_hello_response.Length = 48u;
        sahara_hello_response.Version = 2u;
        sahara_hello_response.Version_min = 1u;
        byte[] array3 = CommandFormat.StructToBytes(sahara_hello_response);
        comm.WritePort(array3, 0, array3.Length);
        Log.w(comm.PortName, "programmertype: " + programmertype);
        string[] array4;
        switch (programmertype)
        {
            case FirehoseCommands.PROGRAMMER_LITE:
                array4 = FileSearcher.SearchFiles(swPath, SoftwareImage.ProgrammerLite);
                break;
            case FirehoseCommands.PROGRAMMER_DDR4:
                array4 = FileSearcher.SearchFiles(swPath, SoftwareImage.ProgrammerDDR4);
                break;
            case FirehoseCommands.PROGRAMMER_DDR5:
                array4 = FileSearcher.SearchFiles(swPath, SoftwareImage.ProgrammerDDR5);
                break;
            default:
                Log.w(comm.PortName, "swPath:" + swPath);
                array4 = FileSearcher.SearchFiles(swPath, SoftwareImage.ProgrammerPattern);
                break;
        }
        if (array4.Length == 0)
        {
            throw new Exception("can not found programmer file.");
        }
        Log.w(comm.PortName, "download programmer file: " + array4[0]);
        string text = array4[0];
        FileInfo fileInfo = new FileInfo(text);
        if (fileInfo.Name.ToLowerInvariant().Contains("firehose"))
        {
            // programmerType = firehose (seul mode supporté par ce port).
        }
        if (fileInfo.Name.ToLowerInvariant().Contains("ufs"))
        {
            storageType = Storage.ufs;
        }
        else if (fileInfo.Name.ToLowerInvariant().Contains("emmc"))
        {
            storageType = Storage.emmc;
        }
        if (fileInfo.Name.ToLowerInvariant().Contains("lite"))
        {
            isLite = true;
        }
        comm.intSectorSize = storageType == Storage.ufs ? comm.SECTOR_SIZE_UFS : comm.SECTOR_SIZE_EMMC;
        Log.w(comm.PortName, "download programmer " + text);
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, "download programmer " + text, "flashing", isDone: false);
        using var fileTransfer = new FileTransfer(comm.PortName, text);
        bool flag;
        do
        {
            flag = false;
            comm.getRecData();
            sahara_packet = CommandFormat.BytesToStuct<SaharaPackets.sahara_packet>(comm.recData);
            switch (sahara_packet.Command)
            {
                case SaharaCommand.SAHARA_READDATA_PACKET:
                    {
                        var sahara_readdata_packet = CommandFormat.BytesToStuct<SaharaPackets.sahara_readdata_packet>(comm.recData);
                        msg = $"sahara read data:imgID {sahara_readdata_packet.Image_id}, offset {sahara_readdata_packet.Offset},length {sahara_readdata_packet.SLength}";
                        fileTransfer.transfer(comm.transport, (int)sahara_readdata_packet.Offset, (int)sahara_readdata_packet.SLength);
                        Log.w(comm.PortName, msg);
                        break;
                    }
                case SaharaCommand.SAHARA_64B_READDATA_PACKET:
                    {
                        var sahara_64b_readdata_packet = CommandFormat.BytesToStuct<SaharaPackets.sahara_64b_readdata_packet>(comm.recData);
                        msg = $"sahara read 64b data:imgID {sahara_64b_readdata_packet.Image_id},offset {sahara_64b_readdata_packet.Offset},length {sahara_64b_readdata_packet.SLength}";
                        fileTransfer.transfer(comm.transport, (int)sahara_64b_readdata_packet.Offset, (int)sahara_64b_readdata_packet.SLength);
                        Log.w(comm.PortName, msg);
                        break;
                    }
                case SaharaCommand.SAHARA_END_TRANSFER_PACKET:
                    {
                        sahara_end_transfer_packet = CommandFormat.BytesToStuct<SaharaPackets.sahara_end_transfer_packet>(comm.recData);
                        msg = $"sahara read end  imgID:{sahara_end_transfer_packet.Image_id} status:{sahara_end_transfer_packet.Status}";
                        if (sahara_end_transfer_packet.Status != 0)
                        {
                            Log.w(comm.PortName, $"sahara read end error with status:{sahara_end_transfer_packet.Status}");
                        }
                        flag = true;
                        break;
                    }
                default:
                    msg = $"invalid command:{sahara_packet.Command}";
                    Log.w(comm.PortName, msg);
                    break;
            }
        }
        while (!flag);
        Log.w(comm.PortName, "Send done packet");
        sahara_packet.Command = SaharaCommand.SAHARA_DONE_PACKET;
        sahara_packet.Length = 8u;
        byte[] array5 = CommandFormat.StructToBytes(sahara_packet, 8);
        comm.WritePort(array5, 0, array5.Length);
        comm.getRecData();
        if (comm.recData.Length == 0)
        {
            comm.recData = new byte[48];
        }
        sahara_done_response = CommandFormat.BytesToStuct<SaharaPackets.sahara_done_response>(comm.recData);
        if (sahara_done_response.Command == SaharaCommand.SAHARA_DONE_RESPONSE)
        {
            msg = "file " + text + " transferred successfully";
            Log.w(comm.PortName, msg);
            FlashStatus.UpdateDeviceStatus(comm.PortName, 1f, msg, "flashing", isDone: false);
            Thread.Sleep(TIME_WAIT_FIREHOSE_START);
            return;
        }
        throw new Exception("programmer transfer error " + sahara_done_response.Command);
    }

    // ----------------------------------------------------------- Firehose ----

    /// <summary>Ping firehose (<c>nop</c>) — PropareFirehose/ping() l.625–638.</summary>
    public void Ping()
    {
        Log.w(comm.PortName, "send nop command");
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, "ping target via firehose", "flashing", isDone: false);
        string command = string.Format(FirehoseCommands.Nop, verbose ? "1" : "0");
        if (!comm.SendCommand(command, checkAck: true))
        {
            throw new Exception("ping target failed");
        }
    }

    /// <summary>
    /// Configure DDR (M3.2) : envoie <c>&lt;configure&gt;</c> ; en cas d'échec
    /// avec auth requise, déclenche <see cref="DlAuth"/> puis rejoue la
    /// commande (ConfigureDDR l.702–738).
    /// </summary>
    public void ConfigureDDR(int intSectorSize, int buffer_sectors, string ddrType, int m_iSkipStorageInit)
    {
        Log.w(comm.PortName, "send configure command");
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, "send configure command", "flashing", isDone: false);
        string command = string.Format(FirehoseCommands.Configure, verbose ? "1" : "0", intSectorSize * buffer_sectors, ddrType, m_iSkipStorageInit);
        bool flag = comm.SendCommand(command, checkAck: true);
        if (!flag)
        {
            if (!comm.needEdlAuth)
            {
                Log.w(comm.PortName, "configure failed!!!");
                throw new Exception("configure failed!!!");
            }
            Log.w(comm.PortName, "edl authentication");
            FlashStatus.UpdateDeviceStatus(comm.PortName, null, "authentication EDL (NoAuth)", "flashing", isDone: false);
            if (!DlAuth())
            {
                Log.w(comm.PortName, "authorize failed!!!");
                throw new Exception("authorize failed!!!");
            }
            flag = comm.SendCommand(command, checkAck: true);
        }
        if (Storage.ufs.ToLowerInvariant() == ddrType && !isLite && !flag)
        {
            throw new Exception("send configure command failed");
        }
        Log.w(comm.PortName, "max buffer sector is " + comm.m_dwBufferSectors);
    }

    /// <summary>
    /// Authentification EDL NoAuth (M3.2) — port de dlAuth() l.673–700 :
    /// AUTHP(size) → ACK → blob brut → ACK. Le blob vient de
    /// <see cref="IEdlAuthenticator"/> (256 o en v1).
    /// </summary>
    public bool DlAuth()
    {
        Log.w(comm.PortName, "authentication edl.");
        byte[] array = Authenticator.GetAuthBlob();
        string command = string.Format(FirehoseCommands.AUTHP, array.Length);
        bool flag = comm.SendCommand(command, checkAck: true);
        if (!flag)
        {
            throw new Exception("authentication failed");
        }
        comm.WritePort(array, 0, array.Length);
        if (!comm.GetResponse(waiteACK: true))
        {
            throw new Exception("authentication failed");
        }
        return flag;
    }

    /// <summary>Peek CPUID (RF-4) : PEEKCPUID puis lecture — la détection
    /// DDR4/DDR5 est faite par <see cref="Comm.GetResponse"/> (0xC3 0x00 …).</summary>
    public void DetectDdrGeneration()
    {
        Log.w(comm.PortName, "peek cpuid");
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, "peek cpuid", "flashing", isDone: false);
        comm.SendCommand(FirehoseCommands.PEEKCPUID);
        comm.GetResponse(waiteACK: true);
        Log.w(comm.PortName, $"ddr4={comm.isDdr4} ddr5={comm.isDdr5}");
    }

    /// <summary>setbootablestoragedrive=1 (UFS) — SetBootPartition l.973–984.</summary>
    public void SetBootPartition()
    {
        const string text = "Set Boot Partition ";
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, text, "flashing", isDone: false);
        string command = string.Format(FirehoseCommands.SetBootPartition, verbose ? "1" : "0");
        if (!comm.SendCommand(command, checkAck: true))
        {
            throw new Exception("set boot partition failed");
        }
        FlashStatus.UpdateDeviceStatus(comm.PortName, 1f, text, "flashing", isDone: false);
        Log.w(comm.PortName, text);
    }

    /// <summary>rawprogram*.xml + patch*.xml (M3.6/M3.7) — FirehoseDownloadImg l.986–1022.</summary>
    public void FirehoseDownloadImg(string swPath)
    {
        string[] array = FileSearcher.SearchFiles(swPath, SoftwareImage.RawProgramPattern);
        string[] array2 = FileSearcher.SearchFiles(swPath, SoftwareImage.PatchPattern);
        for (int j = 0; j < array.Length; j++)
        {
            if (WriteFilesToDevice(swPath, array[j]))
            {
                ApplyPatchesToDevice(array2.Length > j ? array2[j] : null);
            }
        }
    }

    /// <summary>Écrit un rawprogram XML (program par program) — l.1214–1336.</summary>
    public bool WriteFilesToDevice(string swPath, string rawFilePath)
    {
        bool result = true;
        Log.w(comm.PortName, "open program file " + rawFilePath);
        FlashStatus.UpdateDeviceStatus(comm.PortName, null, rawFilePath, "flashing", isDone: false);
        var xmlDocument = new XmlDocument();
        using var xmlReader = XmlReader.Create(rawFilePath, new XmlReaderSettings { IgnoreComments = true });
        xmlDocument.Load(xmlReader);
        XmlNodeList childNodes = xmlDocument.SelectSingleNode("data").ChildNodes;
        bool is_sparse = false;
        string filename = "";
        string file_sector_offset = "0";
        string start_sector = "0";
        string num_partition_sectors = "0";
        string physical_partition_number = "0";
        string sector_size_in_bytes = "512";
        string label = "";
        foreach (XmlNode item in childNodes)
        {
            if (item.Name.ToLowerInvariant() == "xblgpt")
            {
                foreach (XmlAttribute attribute in item.Attributes)
                {
                    if (attribute.Name.ToLowerInvariant() == "lun")
                    {
                        string command = string.Format(FirehoseCommands.XBLGPT, attribute.Value);
                        comm.SendCommand(command, checkAck: true);
                    }
                }
            }
            if (item.Name.ToLowerInvariant() != "program")
            {
                continue;
            }
            foreach (XmlAttribute attribute2 in item.Attributes)
            {
                switch (attribute2.Name.ToLowerInvariant())
                {
                    case "file_sector_offset":
                        file_sector_offset = attribute2.Value;
                        break;
                    case "filename":
                        filename = attribute2.Value;
                        break;
                    case "num_partition_sectors":
                        num_partition_sectors = attribute2.Value;
                        break;
                    case "start_sector":
                        start_sector = attribute2.Value;
                        break;
                    case "sparse":
                        is_sparse = attribute2.Value == "true";
                        break;
                    case "sector_size_in_bytes":
                        sector_size_in_bytes = attribute2.Value;
                        break;
                    case "physical_partition_number":
                        physical_partition_number = attribute2.Value;
                        break;
                    case "label":
                        label = attribute2.Value;
                        break;
                }
            }
            if (string.IsNullOrEmpty(filename))
            {
                continue;
            }
            comm.CleanBuffer();
            filename = Path.Combine(swPath, filename);
            if (!File.Exists(filename))
            {
                throw new Exception("file " + filename + " not found.");
            }
            if (filename.Contains("gpt_main1") || filename.Contains("gpt_main2"))
            {
                Thread.Sleep(1000);
            }
            string addtionalFirehose = readBackVerify ? "read_back_verify=\"1\"" : "";
            DateTime now = DateTime.Now;
            is_sparse = CekSparse(filename);
            if (is_sparse)
            {
                Log.w(comm.PortName, "Write sparse file " + filename + " to partition[" + label + "] sector " + start_sector);
                using (var fileTransfer = new FileTransfer(comm.PortName, filename))
                {
                    fileTransfer.WriteSparseFileToDevice(comm, start_sector, num_partition_sectors, filename, file_sector_offset, sector_size_in_bytes, physical_partition_number, addtionalFirehose);
                }
            }
            else
            {
                Log.w(comm.PortName, "Write file " + filename + " to partition[" + label + "] sector " + start_sector);
                using (var fileTransfer2 = new FileTransfer(comm.PortName, filename))
                {
                    fileTransfer2.WriteFile(comm, start_sector, num_partition_sectors, filename, file_sector_offset, "0", sector_size_in_bytes, physical_partition_number, addtionalFirehose, chkAck: true, 1);
                }
            }
            Log.w(comm.PortName, "Image " + filename + " transferred successfully,elapse " + (DateTime.Now - now));
        }
        return result;
    }

    /// <summary>Applique un patch XML — ApplyPatchesToDevice l.1448–1518.</summary>
    public bool ApplyPatchesToDevice(string patchFilePath)
    {
        if (string.IsNullOrEmpty(patchFilePath))
        {
            return true;
        }
        bool result = true;
        Log.w(comm.PortName, "open patch file " + patchFilePath);
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, patchFilePath, "flashing", isDone: false);
        var xmlDocument = new XmlDocument();
        using var xmlReader = XmlReader.Create(patchFilePath, new XmlReaderSettings { IgnoreComments = true });
        xmlDocument.Load(xmlReader);
        XmlNodeList childNodes = xmlDocument.SelectSingleNode("patches").ChildNodes;
        string text = "";
        string pszPatchSize = "0";
        string pszPatchValue = "0";
        string pszDiskOffsetSector = "0";
        string pszSectorOffsetByte = "0";
        string pszPhysicalPartitionNumber = "0";
        string pszSectorSizeInBytes = "512";
        foreach (XmlNode item in childNodes)
        {
            if (item.Name.ToLowerInvariant() != "patch")
            {
                continue;
            }
            foreach (XmlAttribute attribute in item.Attributes)
            {
                switch (attribute.Name.ToLowerInvariant())
                {
                    case "byte_offset":
                        pszSectorOffsetByte = attribute.Value;
                        break;
                    case "filename":
                        text = attribute.Value;
                        break;
                    case "size_in_bytes":
                        pszPatchSize = attribute.Value;
                        break;
                    case "start_sector":
                        pszDiskOffsetSector = attribute.Value;
                        break;
                    case "value":
                        pszPatchValue = attribute.Value;
                        break;
                    case "sector_size_in_bytes":
                        pszSectorSizeInBytes = attribute.Value;
                        break;
                    case "physical_partition_number":
                        pszPhysicalPartitionNumber = attribute.Value;
                        break;
                }
            }
            if (text.ToLowerInvariant() == "disk")
            {
                ApplyPatch(pszDiskOffsetSector, pszSectorOffsetByte, pszPatchValue, pszPatchSize, pszSectorSizeInBytes, pszPhysicalPartitionNumber);
            }
        }
        FlashStatus.UpdateDeviceStatus(comm.PortName, 1f, patchFilePath, "flashing", isDone: false);
        return result;
    }

    private void ApplyPatch(string pszDiskOffsetSector, string pszSectorOffsetByte, string pszPatchValue, string pszPatchSize, string pszSectorSizeInBytes, string pszPhysicalPartitionNumber)
    {
        Log.w(comm.PortName, "ApplyPatch sector " + pszDiskOffsetSector + ", offset " + pszSectorOffsetByte + ", value " + pszPatchValue + ", size " + pszPatchSize);
        string text = readBackVerify ? "read_back_verify=\"1\"" : "";
        string command = string.Format(FirehoseCommands.FIREHOSE_PATCH, pszSectorSizeInBytes, pszSectorOffsetByte, pszPhysicalPartitionNumber, pszPatchSize, pszDiskOffsetSector, pszPatchValue, text);
        comm.SendCommand(command);
        Thread.Sleep(50);
        comm.GetResponseXml(waiteACK: false);
    }

    /// <summary>partial_reset final (l.218–222).</summary>
    public void PartialReset()
    {
        Log.w(comm.PortName, "Partial_Reset target");
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, "Partial_Reset target", "flashing", isDone: false);
        string text = string.Format(FirehoseCommands.Partial_Reset, verbose ? "1" : "0");
        comm.SendCommand(text, checkAck: true);
        Thread.Sleep(1000);
    }

    /// <summary>reset_to_edl (utilisé par le flux provision, exposé pour M3.5).</summary>
    public void ResetToEdl()
    {
        Log.w(comm.PortName, "Reset_To_Edl target");
        FlashStatus.UpdateDeviceStatus(comm.PortName, 0f, "Reset_To_Edl target", "flashing", isDone: false);
        string text = string.Format(FirehoseCommands.Reset_To_Edl, verbose ? "1" : "0");
        comm.SendCommand(text, checkAck: true);
        Thread.Sleep(2000);
        comm.Close();
        Thread.Sleep(2000);
        comm.transport.Close();
        comm.transport.Open();
    }

    // -------------------------------------------------------- Utilitaires ----

    public bool NeedProvision(string swpath)
    {
        return FileSearcher.SearchFiles(swpath, SoftwareImage.ProvisionPattern).Length != 0;
    }

    /// <summary>Vrai si les programmers DDR4 ET DDR5 sont présents (choix par peek CPUID).</summary>
    public bool NeedDifferentiateDdr(string swpath)
    {
        return FileSearcher.SearchFiles(swpath, SoftwareImage.ProgrammerDDR4).Length != 0
            && FileSearcher.SearchFiles(swpath, SoftwareImage.ProgrammerDDR5).Length != 0;
    }

    /// <summary>Détection d'image sparse par magic (CekSparse l.1187–1210).</summary>
    public static bool CekSparse(string fileName)
    {
        byte[] buffer = new byte[28];
        using (var stream = File.OpenRead(fileName))
        using (var reader = new BinaryReader(stream))
        {
            reader.Read(buffer, 0, 28);
        }
        var header = CommandFormat.BytesToStuct<SparseImageHeader>(buffer);
        return header.uMagic == Sparse.SPARSE_HEADER_MAGIC;
    }
}
