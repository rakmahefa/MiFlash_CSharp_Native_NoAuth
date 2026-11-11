using System;
using System.Runtime.InteropServices;

namespace MiFlash.Core.Protocol;

/// <summary>
/// Sérialisation structure ↔ octets (port direct de CommandFormat.cs).
/// Repose sur <c>Marshal</c> avec les layouts <c>StructLayout(Pack=1)</c> des
/// paquets — comportement identique sur Linux (little-endian x86/arm).
/// </summary>
public static class CommandFormat
{
    /// <summary>Sérialise une structure sur 48 octets (taille du HELLO/HELLO RESPONSE).</summary>
    public static byte[] StructToBytes(object structObj)
    {
        int num = 48;
        byte[] array = new byte[num];
        IntPtr intPtr = Marshal.AllocHGlobal(num);
        try
        {
            Marshal.StructureToPtr(structObj, intPtr, fDeleteOld: false);
            Marshal.Copy(intPtr, array, 0, num);
            return array;
        }
        finally
        {
            Marshal.FreeHGlobal(intPtr);
        }
    }

    public static byte[] StructToBytes(object structObj, int length)
    {
        byte[] array = new byte[length];
        IntPtr intPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(structObj, intPtr, fDeleteOld: false);
            Marshal.Copy(intPtr, array, 0, length);
            return array;
        }
        finally
        {
            Marshal.FreeHGlobal(intPtr);
        }
    }

    /// <summary>Désérialise une structure (null si tampon trop court).</summary>
    public static object BytesToStuct(byte[] bytes, Type type)
    {
        int num = Marshal.SizeOf(type);
        if (num > bytes.Length)
        {
            return null;
        }
        IntPtr intPtr = Marshal.AllocHGlobal(num);
        try
        {
            Marshal.Copy(bytes, 0, intPtr, num);
            return Marshal.PtrToStructure(intPtr, type);
        }
        finally
        {
            Marshal.FreeHGlobal(intPtr);
        }
    }

    /// <summary>Variante générique typée de <see cref="BytesToStuct(byte[], Type)"/>.</summary>
    public static T BytesToStuct<T>(byte[] bytes) where T : struct
    {
        return (T)BytesToStuct(bytes, typeof(T));
    }
}
