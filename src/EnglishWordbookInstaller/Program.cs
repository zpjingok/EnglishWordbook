using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace EnglishWordbookInstaller;

internal static class Program
{
    private const string ProductName = "英语单词簿";
    private const string PayloadResource = "EnglishWordbookInstaller.payload.EnglishWordbook.exe";

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        var installFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnglishWordbook");
        var targetFile = Path.Combine(installFolder, "EnglishWordbook.exe");

        try
        {
            if (File.Exists(targetFile))
            {
                var answer = MessageBox.Show(
                    "检测到已安装的英语单词簿。\n\n点击“是”更新程序；点击“否”取消。\n（你的 API 设置和 Markdown 单词簿不会被覆盖。）",
                    ProductName + "安装程序",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (answer != DialogResult.Yes)
                    return;
                if (IsInstalledAppRunning(targetFile))
                {
                    MessageBox.Show(
                        "请先在右下角托盘图标中选择“退出”，再重新运行安装程序。",
                        ProductName + "安装程序",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
            }

            Directory.CreateDirectory(installFolder);
            ExtractPayload(targetFile);
            CreateDesktopShortcut(targetFile);

            MessageBox.Show(
                "安装完成，已在桌面创建“英语单词簿”快捷方式。\n\n为了保护账号安全，API Key 不会随安装包携带；请在新电脑打开“设置”后自行填写。",
                ProductName + "安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            Process.Start(new ProcessStartInfo(targetFile) { UseShellExecute = true });
        }
        catch (Exception error)
        {
            MessageBox.Show(
                "安装失败：\n" + error.Message,
                ProductName + "安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void ExtractPayload(string targetFile)
    {
        using var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("安装包内容不完整，请重新获取安装程序。");
        using var output = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None);
        payload.CopyTo(output);
    }

    private static bool IsInstalledAppRunning(string targetFile)
    {
        foreach (var process in Process.GetProcessesByName("EnglishWordbook"))
        {
            try
            {
                if (string.Equals(process.MainModule?.FileName, targetFile, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // A process can end or deny module inspection while the installer is checking it.
            }
            finally
            {
                process.Dispose();
            }
        }
        return false;
    }

    private static void CreateDesktopShortcut(string targetFile)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var shortcutPath = Path.Combine(desktop, ProductName + ".lnk");
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
                throw new InvalidOperationException("无法创建桌面快捷方式。");

            shell = Activator.CreateInstance(shellType);
            if (shell is null)
                throw new InvalidOperationException("无法创建桌面快捷方式。");

            dynamic automation = shell;
            shortcut = automation.CreateShortcut(shortcutPath);
            dynamic link = shortcut;
            link.TargetPath = targetFile;
            link.WorkingDirectory = Path.GetDirectoryName(targetFile)!;
            link.IconLocation = targetFile + ",0";
            link.Description = "英语单词、短语翻译与 Markdown 单词簿";
            link.Save();
        }
        finally
        {
            if (shortcut is not null && Marshal.IsComObject(shortcut))
                Marshal.FinalReleaseComObject(shortcut);
            if (shell is not null && Marshal.IsComObject(shell))
                Marshal.FinalReleaseComObject(shell);
        }
    }
}
