using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AI_Chat.Plugins.Virtualization
{
    public class PluginVirtualizationData
    {
        public string PluginId { get; set; }
        public bool IsVirtualizationEnabled { get; set; }
        public bool SupportSandbox { get; set; } = true;
        public List<VirtualRegistryEntry> RegistryEntries { get; set; }
        public List<VirtualFileEntry> FileEntries { get; set; }
        public VirtualizationStatistics Statistics { get; set; }
        public List<PluginActivityRecord> ActivityRecords { get; set; }
    }

    public class PluginActivityRecord
    {
        public string PluginId { get; set; }
        public DateTime Timestamp { get; set; }
        public string ActivityType { get; set; }
        public string Category { get; set; }
        public string Target { get; set; }
        public string Detail { get; set; }
        public bool IsVirtualized { get; set; }
        public bool IsBlocked { get; set; }
        public string Result { get; set; }
    }

    public class VirtualRegistryEntry
    {
        public string KeyPath { get; set; }
        public string ValueName { get; set; }
        public object Value { get; set; }
        public string ValueKind { get; set; }
        public DateTime LastModified { get; set; }
        public string PluginId { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsKeyDeleted { get; set; }
    }

    public class VirtualFileEntry
    {
        public string VirtualPath { get; set; }
        public string RealPath { get; set; }
        public string PluginId { get; set; }
        public DateTime CreatedTime { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsDirectory { get; set; }
        public bool IsDeleted { get; set; }

        // 序列化时使用的大小字段
        [JsonProperty("Size")]
        public long SerializedSize { get; set; }

        // 动态获取文件大小
        [JsonIgnore]
        public long Size
        {
            get
            {
                if (IsDirectory || IsDeleted || string.IsNullOrEmpty(RealPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Size: skipped - IsDir={IsDirectory}, IsDel={IsDeleted}, RealPath={RealPath}");
                    return 0;
                }
                try
                {
                    var fileInfo = new FileInfo(RealPath);
                    System.Diagnostics.Debug.WriteLine($"Size: checking {RealPath}, Exists={fileInfo.Exists}, Length={fileInfo.Length}");
                    return fileInfo.Exists ? fileInfo.Length : 0;
                }
                catch (Exception ex) 
                { 
                    System.Diagnostics.Debug.WriteLine($"Size: error for {RealPath} - {ex.Message}");
                    return 0; 
                }
            }
        }

        // 序列化前调用，更新序列化值
        public void UpdateSerializedSize()
        {
            SerializedSize = Size;
        }
    }

    public class VirtualProcessEntry
    {
        public int VirtualProcessId { get; set; }
        public string ProcessName { get; set; }
        public string PluginId { get; set; }
        public DateTime CreatedTime { get; set; }
        public bool IsActive { get; set; }
    }

    public class VirtualizationStatistics
    {
        public string PluginId { get; set; }
        public int RegistryReads { get; set; }
        public int RegistryWrites { get; set; }
        public int RegistryVirtualWrites { get; set; }
        public int FileReads { get; set; }
        public int FileWrites { get; set; }
        public int FileVirtualWrites { get; set; }
        public int FileBlockedWrites { get; set; }
        public int ProcessAccessAttempts { get; set; }
        public int ProcessAccessBlocked { get; set; }
        public DateTime LastActivity { get; set; }
    }

    public class VirtualizationConfig
    {
        public bool EnableRegistryVirtualization { get; set; } = true;
        public bool EnableFileVirtualization { get; set; } = true;
        public bool EnableProcessInterception { get; set; } = true;
        public bool BlockExeWrites { get; set; } = true;
        public List<string> AllowedFileExtensions { get; set; } = new List<string>();
        public List<string> BlockedFileExtensions { get; set; } = new List<string> { ".exe", ".dll", ".bat", ".cmd", ".ps1", ".msi", ".msp", ".scr", ".com", ".vbs", ".js", ".wsf", ".wsh", ".jar", ".py", ".rb", ".pl", ".sh" };
        public List<string> ProtectedRegistryKeys { get; set; } = new List<string>();
        public List<string> ReadOnlyRegistryKeys { get; set; } = new List<string>();
        public List<string> ExcludedPaths { get; set; } = new List<string>();
    }

    public class FileAccessResult
    {
        public bool Allowed { get; set; }
        public string VirtualPath { get; set; }
        public string RealPath { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsVirtualized { get; set; }
    }

    public class RegistryAccessResult
    {
        public bool Allowed { get; set; }
        public bool ExistsInVirtual { get; set; }
        public bool ExistsInReal { get; set; }
        public object Value { get; set; }
        public string ValueKind { get; set; }
        public string ErrorMessage { get; set; }
        public bool IsVirtualized { get; set; }
        public bool WasDeleted { get; set; }
    }

    public class ProcessAccessResult
    {
        public bool Allowed { get; set; }
        public string ErrorMessage { get; set; }
        public int? VirtualProcessId { get; set; }
    }
}
