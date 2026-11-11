// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : SparseChunkHeader.cs
using System.Runtime.InteropServices;

namespace MiFlash.Core.Images;

/// <summary>En-tête de chunk sparse (12 octets, layout strict).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SparseChunkHeader
{
    public ushort uChunkType;
    public ushort uReserved1;
    public uint uChunkSize;
    public uint uTotalSize;
}
