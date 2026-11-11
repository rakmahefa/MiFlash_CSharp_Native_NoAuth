// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : SparseImageHeader.cs
using System.Runtime.InteropServices;

namespace MiFlash.Core.Images;

/// <summary>En-tête d'image sparse (28 octets, layout strict).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SparseImageHeader
{
    public uint uMagic;
    public ushort uMajorVersion;
    public ushort uMinorVersion;
    public ushort uFileHeaderSize;
    public ushort uChunkHeaderSize;
    public uint uBlockSize;
    public uint uTotalBlocks;
    public uint uTotalChunks;
    public uint uImageChecksum;
}
