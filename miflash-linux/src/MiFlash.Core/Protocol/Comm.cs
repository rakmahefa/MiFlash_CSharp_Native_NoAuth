using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using MiFlash.Core.Runtime;
using MiFlash.Core.Transport;

namespace MiFlash.Core.Protocol;

/// <summary>
/// Couche d'échange Firehose au-dessus d'un <see cref="ITransport"/> (M1.3) —
/// port de XiaoMiFlash.code.Utility/Comm.cs.
/// Boucles ACK/NAK, parsing XML des réponses, détection DDR4/DDR5 (peek CPUID),
/// numéro de série du chip. Différences volontaires par rapport au code Windows :
/// <c>Encoding.Default</c> (ANSI/GBK) → <c>Encoding.UTF8</c> explicite (M1.5, RNF-4),
/// <c>SerialPort</c> → <see cref="ITransport"/>, journalisation/status via abstractions.
/// </summary>
public class Comm
{
    public bool isReadDump = true;

    public bool isWriteDump;

    public bool ignoreResponse = true;

    /// <summary>Transport sous-jacent (libusb bulk, série ou faux transport de test).</summary>
    public ITransport transport;

    private bool _keepReading;

    public byte[] recData;

    private long received_count;

    public int MAX_SECTOR_STR_LEN = 20;

    public int SECTOR_SIZE_UFS = 4096;

    public int SECTOR_SIZE_EMMC = 512;

    public int m_dwBufferSectors;

    public int intSectorSize;

    public string auth = "";

    public string edlAuthErr { get; set; } = "error: only nop and sig tag can be recevied before authentication";

    /// <summary>Nombre d'itérations de polling de GetResponse (32 = fidèle au
    /// code Windows, ~16 s par réponse NAK ; réduit dans les tests).</summary>
    public int responsePolls = 32;

    public bool needEdlAuth = true;

    public bool isSupportPartialReset;

    public bool isDdr4;

    public bool isDdr5;

    public string storageInfo = "";

    public bool isGetChipNum;

    public string chipNum = "";

    /// <summary>Nom logique du transport (équivalent serialPort.PortName).</summary>
    public string PortName => transport.PortName;

    public bool IsOpen
    {
        get
        {
            int num = 20;
            while (num-- > 0 && !transport.IsOpen)
            {
                Log.w(transport.PortName, "wait for port open.");
                Thread.Sleep(50);
            }
            return transport.IsOpen;
        }
    }

    public Comm(ITransport transport)
    {
        this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _keepReading = false;
    }

    public bool isKeepReading()
    {
        return _keepReading;
    }

    public void StartReading()
    {
        if (!_keepReading)
        {
            _keepReading = true;
        }
    }

    public void StopReading()
    {
        if (_keepReading)
        {
            _keepReading = false;
        }
    }

    public byte[] ReadPortData()
    {
        byte[] result = null;
        if (transport.IsOpen)
        {
            int bytesToRead = transport.BytesToRead;
            if (bytesToRead > 0)
            {
                result = new byte[bytesToRead];
                try
                {
                    received_count += transport.Read(result, 0, bytesToRead);
                    return result;
                }
                catch (TimeoutException ex)
                {
                    Log.w(transport.PortName, ex, stopFlash: false);
                    return result;
                }
            }
        }
        return result;
    }

    public byte[] ReadPortData(int offset, int count)
    {
        byte[] array = new byte[count];
        try
        {
            transport.Read(array, offset, count);
            return array;
        }
        catch (TimeoutException ex)
        {
            Log.w(transport.PortName, ex, stopFlash: false);
            return array;
        }
    }

    public void Open()
    {
        Close();
        transport.Open();
        if (!transport.IsOpen)
        {
            string text = "open serial port failed!";
            Log.w(transport.PortName, text);
            FlashStatus.UpdateDeviceStatus(transport.PortName, null, text, "error", isDone: true);
        }
    }

    public void Close()
    {
        transport.Close();
    }

    public void CleanBuffer()
    {
        transport.DiscardBuffers();
    }

    public void WritePort(byte[] send, int offSet, int count)
    {
        if (IsOpen)
        {
            _ = _keepReading;
            int num = 0;
            Exception ex = new TimeoutException();
            bool flag = false;
            while (num++ <= 6 && ex != null && ex.GetType() == typeof(TimeoutException))
            {
                try
                {
                    transport.WriteTimeout = 2000;
                    transport.Write(send, offSet, count);
                    flag = true;
                    if (isWriteDump)
                    {
                        Log.w(transport.PortName, "write to port:");
                        Dump(send);
                    }
                    ex = null;
                }
                catch (TimeoutException ex2)
                {
                    ex = ex2;
                    Log.w(transport.PortName, "write time out try agian " + num);
                    Thread.Sleep(500);
                }
                catch (Exception ex3)
                {
                    Log.w(transport.PortName, "write failed:" + ex3.Message);
                }
            }
            if (!flag)
            {
                Log.w(transport.PortName, ex, stopFlash: true);
                throw new Exception("write time out,maybe device was disconnected.");
            }
        }
    }

    public bool SendCommand(string command)
    {
        return SendCommand(command, checkAck: false);
    }

    public bool SendCommand(string command, bool checkAck)
    {
        // M1.5 : Encoding.Default (ANSI dépendant de la locale Windows) → UTF-8 explicite.
        byte[] bytes = Encoding.UTF8.GetBytes(command);
        if (_keepReading || checkAck)
        {
            Log.w(transport.PortName, "send command:" + command);
        }
        WritePort(bytes, 0, bytes.Length);
        if (checkAck)
        {
            return GetResponse(checkAck);
        }
        return false;
    }

    private int SubstringCount(string str, string substring)
    {
        if (str.Contains(substring))
        {
            string text = str.Replace(substring, "");
            return (str.Length - text.Length) / substring.Length;
        }
        return 0;
    }

    public bool chkRspAck(out string msg)
    {
        msg = null;
        byte[] binary = ReadDataFromPort();
        string[] array = Dump(binary, waitACK: true);
        string value = "<response value=\"ACK\"";
        int num = 10;
        while ((array.Length != 2 || array[1].IndexOf(value) < 0) && num-- >= 0)
        {
            Thread.Sleep(10);
            binary = ReadDataFromPort();
            array = Dump(binary, waitACK: true);
        }
        if (array.Length == 2 && array[1].IndexOf(value) >= 0)
        {
            CleanBuffer();
            return true;
        }
        msg = "did not detect ACK from target.";
        return false;
    }

    public bool chkRspAck(out string msg, int chunkCount)
    {
        msg = null;
        byte[] binary = ReadDataFromPort();
        string[] array = Dump(binary, waitACK: true);
        string text = "<response value=\"ACK\"";
        int num = 10;
        while ((array.Length != 2 || array[1].IndexOf(text) < 0) && num-- >= 0)
        {
            Thread.Sleep(10);
            binary = ReadDataFromPort();
            array = Dump(binary, waitACK: true);
        }
        if (array.Length == 2 && array[1].IndexOf(text) >= 0)
        {
            int i = SubstringCount(array[1], text);
            num = 10;
            for (; i < chunkCount * 2; i += SubstringCount(array[1], text))
            {
                if (num-- <= 0)
                {
                    break;
                }
                Thread.Sleep(10);
                binary = ReadDataFromPort();
                array = Dump(binary, waitACK: true);
            }
            if (chunkCount * 2 > i)
            {
                Log.w(transport.PortName, "ACK count don't match!");
                throw new Exception("ACK count don't match!");
            }
            Log.w(transport.PortName, $"{chunkCount} chunks match {i} ack");
            CleanBuffer();
            return true;
        }
        msg = array[1];
        return false;
    }

    public byte[] getRecDataIgnoreExcep()
    {
        byte[] array = ReadDataFromPort();
        if (array != null && array.Length != 0 && isReadDump)
        {
            Log.w(transport.PortName, "read from port:");
            Dump(array);
        }
        return array;
    }

    public byte[] getRecData()
    {
        byte[] array = ReadDataFromPort();
        if (array == null)
        {
            throw new Exception("can not read from port " + transport.PortName);
        }
        if (array.Length != 0 && isReadDump)
        {
            Log.w(transport.PortName, "read from port:");
            Dump(array);
        }
        return array;
    }

    private byte[] ReadDataFromPort()
    {
        int num = 10;
        recData = null;
        recData = ReadPortData();
        while (num-- >= 0 && recData == null)
        {
            Thread.Sleep(50);
            recData = ReadPortData();
        }
        return recData;
    }

    public bool GetResponse(bool waiteACK)
    {
        bool flag = false;
        Log.w(transport.PortName, "get response from target");
        if (!waiteACK)
        {
            return ReadDataFromPort() != null;
        }
        int num = 2;
        if (waiteACK)
        {
            num = responsePolls;
        }
        while (num-- > 0 && !flag)
        {
            List<XmlDocument> responseXml = GetResponseXml(waiteACK);
            _ = responseXml.Count;
            foreach (XmlDocument item in responseXml)
            {
                foreach (XmlNode childNode in item.SelectSingleNode("data").ChildNodes)
                {
                    if (childNode.Name.ToLower() == "sig")
                    {
                        auth = childNode.OuterXml.Replace("blob", "sig");
                    }
                    foreach (XmlAttribute attribute in childNode.Attributes)
                    {
                        if (attribute.Name.ToLower() == "maxpayloadsizetotargetinbytes")
                        {
                            m_dwBufferSectors = Convert.ToInt32(attribute.Value) / intSectorSize;
                        }
                        if (attribute.Value.ToLower() == "ack")
                        {
                            flag = true;
                        }
                        if (attribute.Value == "WARN: NAK: MaxPayloadSizeToTargetInBytes sent by host 1048576 larger than supported 16384")
                        {
                            flag = true;
                        }
                        if (attribute.Value.Contains("0xC3 0x00 0x40 0x10 "))
                        {
                            Log.w(transport.PortName, "peek 0xc3 0x00 0x40 0x10,chose ddr4");
                            isDdr4 = true;
                        }
                        if (attribute.Value.Contains("0xC3 0x00 0x31 0x10 "))
                        {
                            Log.w(transport.PortName, "peek 0xc3 0x00 0x31 0x10,chose ddr4");
                            isDdr4 = true;
                        }
                        if (attribute.Value.Contains("0xC3 0x00 0x02 0x10 "))
                        {
                            Log.w(transport.PortName, "peek 0xc3 0x00 0x02 0x10,chose ddr4");
                            isDdr4 = true;
                        }
                        if (attribute.Value.Contains("0xC3 0x00 0x00 0x10 "))
                        {
                            Log.w(transport.PortName, "peek 00xC3\u00a00x00 0x00 0x10,chose dd5");
                            isDdr5 = true;
                        }
                        if (attribute.Value.Contains("0xC3 0x00 0x51 0x10 "))
                        {
                            Log.w(transport.PortName, "peek 00xC3\u00a00x00 0x51 0x10,chose dd5");
                            isDdr5 = true;
                        }
                        if (attribute.Value.Contains("0xC3 0x00 0x32 0x10 "))
                        {
                            Log.w(transport.PortName, "peek 00xC3\u00a00x00 0x32 0x10,chose dd5");
                            isDdr5 = true;
                        }
                        if (attribute.Value.Contains("UFS Inquiry Command Output"))
                        {
                            Log.w(transport.PortName, "StorageInfo: " + attribute.Value);
                            storageInfo = attribute.Value;
                        }
                        if (attribute.Value.Contains("INFO: quick_reset") && !isSupportPartialReset)
                        {
                            Log.w(transport.PortName, "quick_reset: " + attribute.Value);
                            isSupportPartialReset = true;
                        }
                        if (attribute.Value.Contains("Chip serial num") && !isGetChipNum)
                        {
                            Log.w(transport.PortName, "Chip serial num: " + attribute.Value);
                            isGetChipNum = true;
                            int num2 = attribute.Value.IndexOf('(') + 1;
                            int num3 = attribute.Value.IndexOf(')');
                            string text = attribute.Value.Substring(num2, num3 - num2);
                            if (text.Length < 10)
                            {
                                int length = 10 - text.Length;
                                string text2 = "00000000";
                                text = text.Substring(0, 2) + text2.Substring(0, length) + text.Substring(2, text.Length - 2);
                            }
                            chipNum = text;
                        }
                    }
                }
            }
            if (waiteACK)
            {
                Thread.Sleep(50);
            }
        }
        return flag;
    }

    public List<XmlDocument> GetResponseXml(bool waiteACK)
    {
        List<XmlDocument> list = new List<XmlDocument>();
        byte[] binary = ReadDataFromPort();
        string[] array = Dump(binary, waiteACK);
        if (array.Length >= 2)
        {
            foreach (string item in Regex.Split(array[1], "\\<\\?xml").ToList())
            {
                if (!string.IsNullOrEmpty(item))
                {
                    if (item.ToLower().IndexOf(edlAuthErr) >= 0)
                    {
                        needEdlAuth = true;
                    }
                    XmlDocument xmlDocument = new XmlDocument();
                    xmlDocument.LoadXml("<?xml " + item);
                    list.Add(xmlDocument);
                }
            }
            return list;
        }
        return list;
    }

    private string[] Dump(byte[] binary)
    {
        return Dump(binary, waitACK: false);
    }

    private string[] Dump(byte[] binary, bool waitACK)
    {
        if (binary == null)
        {
            Log.w(transport.PortName, "no Binary dump");
            return new string[2] { "", "" };
        }
        StringBuilder stringBuilder = new StringBuilder();
        StringBuilder stringBuilder2 = new StringBuilder();
        for (int i = 0; i < binary.Length; i++)
        {
            stringBuilder2.Append(Convert.ToChar(binary[i]).ToString());
        }
        if (waitACK)
        {
            var str = stringBuilder2.ToString();
            if (str.Contains("nop and sig tag"))
            {
                Log.w(transport.PortName, "bypassing authentication: please wait [3 min]...\r\n\r\n", throwEx: false);
            }
            else
            {
                Log.w(transport.PortName, "resdump:" + str + "\r\n\r\n", throwEx: false);
            }
        }
        if (_keepReading)
        {
            Log.debugString(transport.PortName, binary);
        }
        return new string[2]
        {
            stringBuilder.ToString(),
            stringBuilder2.ToString(),
        };
    }
}
