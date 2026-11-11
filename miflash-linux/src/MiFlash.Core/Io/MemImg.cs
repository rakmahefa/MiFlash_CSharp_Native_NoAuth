// Port direct de XiaoMiFlash.code.module — fidélité protocole (spec-linux.md D3).
// origine : MemImg.cs (data), Destroy() renommé depuis distory()
using System.Collections.Generic;
using System.IO;

namespace MiFlash.Core.Io;

/// <summary>
/// Cache d'images en mémoire (« quick transfer »). Port direct de MemImg.cs :
/// par défaut <c>isHighSpeed = false</c> (comportement identique au code
/// Windows d'origine), le transfert passe alors par FileStream.
/// </summary>
public static class MemImg
{
    private static readonly object ObjLock = new object();

    public static bool isHighSpeed = false;

    public static Dictionary<string, MemoryStream> shareMemTable = new Dictionary<string, MemoryStream>();

    /// <summary>Filtre d'exclusion du quick transfer (remplace MiAppConfig "noquick").</summary>
    public static System.Func<string, bool> IsQuickExcluded = static _ => false;

    public static long mapImg(string filePath)
    {
        lock (ObjLock)
        {
            try
            {
                if (!shareMemTable.ContainsKey(filePath))
                {
                    using FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                    int num = (int)fileStream.Length;
                    byte[] buffer = new byte[num];
                    fileStream.Read(buffer, 0, num);
                    MemoryStream value = new MemoryStream(buffer);
                    shareMemTable[filePath] = value;
                    return num;
                }
                return shareMemTable[filePath].Length;
            }
            catch (System.Exception ex)
            {
                throw new System.Exception(ex.Message, ex);
            }
        }
    }

    public static byte[] GetBytesFromFile(string filePath, long offset, int size, out float percent)
    {
        lock (ObjLock)
        {
            MemoryStream memoryStream = shareMemTable[filePath];
            byte[] array = new byte[size];
            memoryStream.Position = offset;
            memoryStream.Read(array, 0, size);
            percent = (float)offset / (float)memoryStream.Length;
            return array;
        }
    }

    public static void Destroy()
    {
        if (!isHighSpeed)
        {
            return;
        }
        foreach (KeyValuePair<string, MemoryStream> item in shareMemTable)
        {
            item.Value.Close();
            item.Value.Dispose();
        }
        shareMemTable.Clear();
    }
}
