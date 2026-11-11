using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace MiFlash.Core.Io;

/// <summary>Recherche de fichiers par motif (port direct de FileSearcher.cs) :
/// regex en correspondance pleine sur le nom, répertoire plat.</summary>
public static class FileSearcher
{
    public static string[] SearchFiles(string destinationDic, string pattern)
    {
        var list = new List<string>();
        if (!Directory.Exists(destinationDic))
        {
            return list.ToArray();
        }
        FileInfo[] files = new DirectoryInfo(destinationDic).GetFiles();
        foreach (FileInfo fileInfo in files)
        {
            Match match = new Regex(pattern).Match(fileInfo.Name);
            if (match.Groups.Count > 0 && match.Groups[0].Value == fileInfo.Name)
            {
                list.Add(fileInfo.FullName);
            }
        }
        return list.ToArray();
    }
}
