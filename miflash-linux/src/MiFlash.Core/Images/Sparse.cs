// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : Sparse.cs
namespace MiFlash.Core.Images;

/// <summary>Constantes du format sparse Android (android sparse image).</summary>
public static class Sparse
{
    public const uint SPARSE_HEADER_MAGIC = 3978755898u; // 0xED26FF3A
    public const uint SPARSE_HEADER_MAJOR_VER = 1u;
    public const ushort SPARSE_CHUNK_TYPE_RAW = 51905;       // 0xCAC5
    public const ushort SPARSE_CHUNK_TYPE_FILL = 51906;      // 0xCAC6
    public const ushort SPARSE_CHUNK_TYPE_DONTCARE = 51907;  // 0xCAC7
    public const int SPARSE_CHUNK_TYPE_FILL_DATA_SIZE = 4;
}
