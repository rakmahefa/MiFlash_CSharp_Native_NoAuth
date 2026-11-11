using System;
using System.IO;
using MiFlash.Core.Io;
using Xunit;

namespace MiFlash.Core.Tests;

/// <summary>
/// Durcissement M6.5 — <see cref="SecurePath"/> : les noms de fichiers lus
/// dans les XML externes (rawprogram/restore/FFU) doivent rester confinés au
/// répertoire d'images.
/// </summary>
public class SecurePathTests
{
    private static string TmpRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "miflash-securepath-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void Simple_name_resolves_under_base()
    {
        string root = TmpRoot();
        try
        {
            string combined = SecurePath.Combine(root, "system.img");
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "system.img")), combined);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Subdirectory_name_is_allowed()
    {
        string root = TmpRoot();
        try
        {
            string combined = SecurePath.Combine(root, Path.Combine("Images", "system.img"));
            Assert.StartsWith(
                root + Path.DirectorySeparatorChar,
                combined,
                StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("../escape.img")]
    [InlineData("sub/../../escape.img")]
    [InlineData("../../etc/passwd")]
    public void Traversal_is_rejected(string name)
    {
        string root = TmpRoot();
        try
        {
            Assert.Throws<IOException>(() => SecurePath.Combine(root, name));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("/boot/vmlinuz")]
    public void Absolute_paths_are_rejected(string name)
    {
        string root = TmpRoot();
        try
        {
            Assert.Throws<IOException>(() => SecurePath.Combine(root, name));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Windows_style_absolute_and_unc_paths_are_rejected_on_windows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // sous Linux, « C:\ » n'est qu'un nom de fichier exotique et inoffensif
        }

        string root = TmpRoot();
        try
        {
            Assert.Throws<IOException>(() => SecurePath.Combine(root, "C:\\Windows\\system32\\evil.dll"));
            Assert.Throws<IOException>(() => SecurePath.Combine(root, "\\\\server\\share\\evil.img"));
            Assert.Throws<IOException>(() => SecurePath.Combine(root, "..\\escape.img"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void Empty_names_are_rejected(string name)
    {
        string root = TmpRoot();
        try
        {
            Assert.Throws<IOException>(() => SecurePath.Combine(root, name));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Base_directory_itself_is_rejected()
    {
        // « . » résout vers la base elle-même : pas un fichier → refusé.
        string root = TmpRoot();
        try
        {
            Assert.Throws<IOException>(() => SecurePath.Combine(root, "."));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
