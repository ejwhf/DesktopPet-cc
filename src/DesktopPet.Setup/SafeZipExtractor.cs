using System.IO.Compression;
using System.Reflection;

namespace DesktopPet.Setup;

internal static class SafeZipExtractor
{
    private const string PayloadResourceName = "DesktopPet.Setup.Payload.zip";
    private const int MaximumEntryCount = 4096;
    private const long MaximumEntryBytes = 64L * 1024 * 1024;
    private const long MaximumArchiveBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> ReservedDeviceNames = new(
        new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        },
        StringComparer.OrdinalIgnoreCase);

    internal static void ExtractEmbeddedPayload(string destinationDirectory)
    {
        var destination = InstallerPaths.ValidateTransientDirectory(destinationDirectory);
        if (Directory.Exists(destination))
        {
            throw new InvalidOperationException("安装临时目录已存在，已中止以避免覆盖未知文件。");
        }

        Directory.CreateDirectory(destination);

        using var payload = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException("安装器中缺少嵌入式桌宠程序包。");
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);

        if (archive.Entries.Count == 0 || archive.Entries.Count > MaximumEntryCount)
        {
            throw new InvalidDataException("程序包文件数量异常。");
        }

        var root = destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var prefix = root + Path.DirectorySeparatorChar;
        var extractedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long declaredTotal = 0;

        foreach (var entry in archive.Entries)
        {
            if (entry.Length < 0 || entry.Length > MaximumEntryBytes)
            {
                throw new InvalidDataException($"程序包条目大小异常：{entry.FullName}");
            }

            declaredTotal = checked(declaredTotal + entry.Length);
            if (declaredTotal > MaximumArchiveBytes)
            {
                throw new InvalidDataException("程序包解压后大小超过安全限制。");
            }

            var relativePath = ValidateRelativeEntryPath(entry.FullName);
            var outputPath = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!outputPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"程序包条目越过安装目录：{entry.FullName}");
            }

            if (!extractedPaths.Add(outputPath))
            {
                throw new InvalidDataException($"程序包包含重复路径：{entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(outputPath);
                continue;
            }

            var parent = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidDataException($"程序包条目缺少父目录：{entry.FullName}");
            Directory.CreateDirectory(parent);

            using var input = entry.Open();
            using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.SequentialScan);
            CopyExactly(input, output, entry.Length, entry.FullName);
        }

        ValidateExtractedApplication(destination);
    }

    private static string ValidateRelativeEntryPath(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) || entryName.IndexOf('\0') >= 0 ||
            entryName.StartsWith('/') || entryName.StartsWith('\\') || Path.IsPathRooted(entryName))
        {
            throw new InvalidDataException($"程序包包含无效路径：{entryName}");
        }

        var normalized = entryName.Replace('\\', '/');
        var isDirectory = normalized.EndsWith('/');
        var segments = normalized.Split('/', StringSplitOptions.None);
        var segmentCount = isDirectory ? segments.Length - 1 : segments.Length;
        if (segmentCount == 0)
        {
            throw new InvalidDataException($"程序包包含空路径：{entryName}");
        }

        for (var index = 0; index < segmentCount; index++)
        {
            var segment = segments[index];
            if (string.IsNullOrWhiteSpace(segment) || segment is "." or ".." ||
                segment.EndsWith(' ') || segment.EndsWith('.') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                ReservedDeviceNames.Contains(segment.Split('.', 2)[0]))
            {
                throw new InvalidDataException($"程序包包含不安全路径段：{entryName}");
            }
        }

        if (segments.Skip(segmentCount).Any(static segment => segment.Length != 0))
        {
            throw new InvalidDataException($"程序包包含无效目录路径：{entryName}");
        }

        return string.Join(Path.DirectorySeparatorChar, segments.Take(segmentCount));
    }

    private static void CopyExactly(Stream input, Stream output, long expectedLength, string entryName)
    {
        var buffer = new byte[81920];
        long copied = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            copied = checked(copied + read);
            if (copied > expectedLength || copied > MaximumEntryBytes)
            {
                throw new InvalidDataException($"程序包条目实际大小异常：{entryName}");
            }

            output.Write(buffer, 0, read);
        }

        if (copied != expectedLength)
        {
            throw new InvalidDataException($"程序包条目未完整读取：{entryName}");
        }
    }

    private static void ValidateExtractedApplication(string root)
    {
        string[] requiredFiles =
        {
            "DesktopPet.exe",
            "DesktopPet.dll",
            "DesktopPet.runtimeconfig.json",
            Path.Combine("assets", "manifest", "assets.json")
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(root, relativePath);
            if (!File.Exists(fullPath) || new FileInfo(fullPath).Length == 0)
            {
                throw new InvalidDataException($"程序包缺少必需文件：{relativePath}");
            }
        }

        var spriteDirectory = Path.Combine(root, "assets", "sprites");
        var spriteCount = Directory.Exists(spriteDirectory)
            ? Directory.GetFiles(spriteDirectory, "*.png", SearchOption.TopDirectoryOnly).Length
            : 0;
        if (spriteCount != 18)
        {
            throw new InvalidDataException($"程序包应包含 18 个动作精灵，实际为 {spriteCount} 个。");
        }
    }
}
