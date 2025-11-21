using System.IO.Compression;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
.AddJsonFile("appsettings.json").Build();

var appInstallConfiguration = configuration.GetSection("App").Get<AppInstallConfiguration>()!;

// 检查路径
appInstallConfiguration.Path.ThrowIfPathNotExist();

using var cts = new CancellationTokenSource();

// app 处理
foreach (var item in appInstallConfiguration.Items)
{
    if (item.Type == "zip")
    {
        var destPath = Path.Combine(appInstallConfiguration.Path, item.Name);
        if (!Directory.Exists(destPath)) Directory.CreateDirectory(destPath);

        // 下载并解压到目录
        await item.Url.DownloadZipFileAndExtractToDirectoryAsync(destPath, cts.Token);

        // 添加环境变量Path
        foreach (var env_path in item.EnvPaths)
        {
            Path.Combine(destPath, env_path).RegisterEnvPath();
        }
    }
    else
    {
        throw new NotSupportedException($"不受支持的压缩格式类型：{item.Type}");
    }
}


public record AppInstallConfiguration(string Path, AppInstallItem[] Items);

public record AppInstallItem(string Name, string Url, string Type, string[] EnvPaths);


public static class Extensions
{
    public static void RegisterEnvPath(this string path)
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException(path);

        var existPath = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.Machine)!;
        var paths = existPath.Split(";");
        if (paths.Contains(path)) return;

        if (!existPath.EndsWith(';')) existPath += ";";

        Environment.SetEnvironmentVariable("Path", existPath + path, EnvironmentVariableTarget.Machine);
    }

    public static void ThrowIfPathNotExist(this string path)
    {
        if (!Path.Exists(path)) throw new Exception($"path: {path} does not exist");
    }

    public static async Task DownloadZipFileAndExtractToDirectoryAsync(this string url, string path, CancellationToken ct = default)
    {
        using var client = new HttpClient();
        using var stream = await client.GetStreamAsync(url, ct);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        // 判断存在顶级目录
        var normalizedDir = Path.GetFileNameWithoutExtension(url) + "/";
        var normalizedEntry = zip.Entries.FirstOrDefault(x => x.FullName.Equals(normalizedDir, StringComparison.OrdinalIgnoreCase));

        if (normalizedEntry is not null)
        {
            foreach (var entry in zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                string relativePath = entry.FullName.Split('/', 2)[1];
                string targetPath = Path.Combine(path, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }
        }
        else
        {
            await zip.ExtractToDirectoryAsync(path, true, ct);
        }
    }
}