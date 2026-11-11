// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : sahara_packet.cs, sahara_hello_packet.cs, sahara_hello_response.cs, sahara_readdata_packet.cs, sahara_64b_readdata_packet.cs, sahara_end_transfer_packet.cs, sahara_switch_Mode_packet.cs, sahara_done_response.cs
using System.Runtime.InteropServices;

namespace MiFlash.Core.Sahara;

/// <summary>Paquets bruts du protocole Sahara — layout strict (Pack=1), ne pas réordonner.</summary>
public static class SaharaPackets
{
    /// <summary>En-tête générique commande/longueur (8 octets).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_packet
    {
        public uint Command;
        public uint Length;
    }

    /// <summary>HELLO reçu de la cible (48 octets, commande 1).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_hello_packet
    {
        public uint Command;
        public uint Length;
        public uint Version;
        public uint Version_min;
        public uint Max_Command_Length;
        public uint Mode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] Reserved;
    }

    /// <summary>HELLO RESPONSE émis par l'hôte (48 octets, commande 2) — version 2 / min 1 / mode 3.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_hello_response
    {
        public uint Command;
        public uint Length;
        public uint Version;
        public uint Version_min;
        public uint Status;
        public uint Mode;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        public uint[] Reserved;
    }

    /// <summary>READ DATA 32 bits (commande 3, 20 octets).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_readdata_packet
    {
        public uint Command;
        public uint Length;
        public uint Image_id;
        public uint Offset;
        public uint SLength;
    }

    /// <summary>READ DATA 64 bits (commande 18, 32 octets).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_64b_readdata_packet
    {
        public uint Command;
        public uint Length;
        public ulong Image_id;
        public ulong Offset;
        public ulong SLength;
    }

    /// <summary>END IMAGE TRANSFER (commande 4, 16 octets).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_end_transfer_packet
    {
        public uint Command;
        public uint Length;
        public uint Image_id;
        public uint Status;
    }

    /// <summary>SWITCH MODE (commande 12, 12 octets) — mode 0 = image mode.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_switch_Mode_packet
    {
        public uint Command;
        public uint Length;
        public uint Mode;
    }

    /// <summary>DONE RESPONSE (commande 6, 12 octets).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct sahara_done_response
    {
        public uint Command;
        public uint Length;
        public uint Status;
    }
}
