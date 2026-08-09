using System.IO.Compression;

namespace RocoPilot.Installer.Core;

public static class PayloadExtractor
{
    public static void Extract(string archivePath, string destination)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException($"应用负载未找到: {archivePath}");
        }

        Directory.CreateDirectory(destination);
        ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true);
    }
}