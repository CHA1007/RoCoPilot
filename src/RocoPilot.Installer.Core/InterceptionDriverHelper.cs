using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace RocoPilot.Installer.Core;

public static class InterceptionDriverHelper
{
    private const string DownloadUrl =
        "https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip";

    public static bool IsInstalled()
    {
        try
        {
            var context = NativeInterception.interception_create_context();
            if (context == IntPtr.Zero)
            {
                return false;
            }

            NativeInterception.interception_destroy_context(context);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    private static class NativeInterception
    {
        [DllImport("interception", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr interception_create_context();

        [DllImport("interception", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void interception_destroy_context(IntPtr context);
    }

    public static async Task InstallAsync(Action<string>? report = null)
    {
        var installer = await DownloadInstallerAsync(report);
        report?.Invoke("正在安装内核驱动（需要管理员权限）…");
        RunInstaller(installer, "/install", "驱动安装程序失败");
    }

    public static void Uninstall(Action<string>? report = null)
    {
        report?.Invoke("正在卸载内核驱动（需要管理员权限）…");
        var script = string.Join(
            " & ",
            "sc stop Interception >nul 2>&1",
            "sc delete Interception",
            "del /f /q \"%SystemRoot%\\System32\\drivers\\interception.sys\"",
            "del /f /q \"%SystemRoot%\\System32\\drivers\\mouse.sys\"",
            "del /f /q \"%SystemRoot%\\System32\\drivers\\keyboard.sys\"");
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c " + script,
            UseShellExecute = true,
            Verb = "runas",
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动驱动卸载程序。");
        p.WaitForExit();
    }

    private static async Task<string> DownloadInstallerAsync(Action<string>? report)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"interception-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, "interception.zip");

        report?.Invoke("正在下载 Interception 驱动…");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RocoPilot");
        using (var resp = await http.GetAsync(DownloadUrl))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zipPath);
            await resp.Content.CopyToAsync(fs);
        }

        report?.Invoke("正在解压驱动…");
        ZipFile.ExtractToDirectory(zipPath, tempDir);

        return Directory.EnumerateFiles(tempDir, "install-interception.exe", SearchOption.AllDirectories)
            .FirstOrDefault()
            ?? throw new InvalidOperationException("未找到驱动安装程序。");
    }

    private static void RunInstaller(string installer, string arguments, string failureMessage)
    {
        var psi = new ProcessStartInfo
        {
            FileName = installer,
            Arguments = arguments,
            UseShellExecute = true,
            Verb = "runas",
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("无法启动驱动安装程序。");
        p.WaitForExit();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException($"{failureMessage}（退出码 {p.ExitCode}）。");
        }
    }
}