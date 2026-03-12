using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace AI_Chat.Utils
{
    public static class PlatformHelper
    {
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        public static bool IsRunningAsAdmin()
        {
            try
            {
                if (IsWindows)
                {
                    return IsWindowsAdmin();
                }
                else if (IsLinux)
                {
                    return IsLinuxRoot();
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunningInDocker()
        {
            try
            {
                // 检查环境变量
                if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
                    return true;
                
                // 检查是否存在 /.dockerenv 文件
                if (File.Exists("/.dockerenv"))
                    return true;
                
                // 检查 cgroup 中是否包含 docker
                if (File.Exists("/proc/1/cgroup"))
                {
                    var cgroupContent = File.ReadAllText("/proc/1/cgroup");
                    if (cgroupContent.Contains("docker"))
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

#if WINDOWS
        private static bool IsWindowsAdmin()
        {
            try
            {
                using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                {
                    var principal = new System.Security.Principal.WindowsPrincipal(identity);
                    return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }
#else
        private static bool IsWindowsAdmin()
        {
            return false;
        }
#endif

        private static bool IsLinuxRoot()
        {
            try
            {
                return Environment.UserName == "root" || Environment.GetEnvironmentVariable("USER") == "root";
            }
            catch
            {
                return false;
            }
        }

        public static void ShowAdminWarning()
        {
            string message = "Software running without administrator privileges; some functions may not work properly.";
            
#if WINDOWS
            if (IsWindows)
            {
                try
                {
                    System.Windows.Forms.MessageBox.Show(
                        message,
                        "Warning",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning
                    );
                    return;
                }
                catch
                {
                    // Fall through to console output
                }
            }
#endif
            Console.WriteLine($"[WARNING] {message}");
        }

        public static bool ShowControlPanelPrompt()
        {
#if WINDOWS
            if (IsWindows)
            {
                try
                {
                    var result = System.Windows.Forms.MessageBox.Show(
                        "Do you want to open the control panel?",
                        "Control Panel",
                        System.Windows.Forms.MessageBoxButtons.YesNo,
                        System.Windows.Forms.MessageBoxIcon.Question
                    );
                    return result == System.Windows.Forms.DialogResult.Yes;
                }
                catch
                {
                    return false;
                }
            }
#endif
            return false;
        }

        public static void OpenBrowser(string url)
        {
            try
            {
                if (IsWindows)
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (IsLinux)
                {
                    Process.Start("xdg-open", url);
                }
                else if (IsMacOS)
                {
                    Process.Start("open", url);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Failed to open browser: {ex.Message}");
            }
        }
    }
}
