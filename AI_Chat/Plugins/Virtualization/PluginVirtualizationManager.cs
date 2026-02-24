using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI_Chat.Services;

namespace AI_Chat.Plugins.Virtualization
{
    public class PluginVirtualizationManager
    {
        private readonly string _baseDataPath;
        private readonly VirtualizationConfig _config;
        private readonly VirtualRegistryService _registryService;
        private readonly VirtualFileSystemService _fileSystemService;
        private readonly VirtualProcessService _processService;
        private readonly Dictionary<string, bool> _pluginVirtualizationEnabled;
        private readonly object _lock = new object();
        private IPluginManager _pluginManager;

        public event Action<string, string> OnVirtualizationEvent;

        public PluginVirtualizationManager(string baseDataPath, VirtualizationConfig config = null)
        {
            _baseDataPath = baseDataPath;
            _config = config ?? new VirtualizationConfig();
            _registryService = new VirtualRegistryService(baseDataPath, _config);
            _fileSystemService = new VirtualFileSystemService(baseDataPath, _config);
            _processService = new VirtualProcessService(_config);
            _pluginVirtualizationEnabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            EnsureDirectoriesExist();
        }

        public void SetPluginManager(IPluginManager pluginManager)
        {
            _pluginManager = pluginManager;
        }

        private void EnsureDirectoriesExist()
        {
            string[] directories = new string[]
            {
                Path.Combine(_baseDataPath, "VirtualRegistry"),
                Path.Combine(_baseDataPath, "VirtualFileSystem")
            };

            using (PluginExecutionContext.BeginPluginScope(null))
            {
                foreach (var dir in directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }
            }
        }

        public void EnableVirtualization(string pluginId)
        {
            lock (_lock)
            {
                _pluginVirtualizationEnabled[pluginId] = true;
            }
            LogEvent(pluginId, "Virtualization enabled");
        }

        public void DisableVirtualization(string pluginId)
        {
            lock (_lock)
            {
                _pluginVirtualizationEnabled[pluginId] = false;
            }
            LogEvent(pluginId, "Virtualization disabled");
        }

        public void RemoveVirtualization(string pluginId)
        {
            lock (_lock)
            {
                _pluginVirtualizationEnabled.Remove(pluginId);
            }
            LogEvent(pluginId, "Virtualization removed");
        }

        public bool IsVirtualizationEnabled(string pluginId)
        {
            lock (_lock)
            {
                return _pluginVirtualizationEnabled.TryGetValue(pluginId, out bool enabled) && enabled;
            }
        }

        public VirtualRegistryService Registry => _registryService;
        public VirtualFileSystemService FileSystem => _fileSystemService;
        public VirtualProcessService Process => _processService;
        public VirtualizationConfig Config => _config;

        #region Registry Operations

        public RegistryAccessResult ReadRegistryValue(string pluginId, string keyPath, string valueName)
        {
            _processService.RecordRegistryRead(pluginId);
            var result = _registryService.ReadValue(pluginId, keyPath, valueName);
            LogEvent(pluginId, $"Registry read: {keyPath}\\{valueName} -> {(result.IsVirtualized ? "Virtual" : "Real")}");
            return result;
        }

        public RegistryAccessResult WriteRegistryValue(string pluginId, string keyPath, string valueName, object value, string valueKind)
        {
            var result = _registryService.WriteValue(pluginId, keyPath, valueName, value, valueKind);
            _processService.RecordRegistryWrite(pluginId, result.IsVirtualized);
            LogEvent(pluginId, $"Registry write: {keyPath}\\{valueName} -> {(result.IsVirtualized ? "Virtual" : "Real")}");
            return result;
        }

        public RegistryAccessResult DeleteRegistryValue(string pluginId, string keyPath, string valueName)
        {
            var result = _registryService.DeleteValue(pluginId, keyPath, valueName);
            LogEvent(pluginId, $"Registry delete value: {keyPath}\\{valueName} -> {(result.Allowed ? (result.IsVirtualized ? "Virtual" : "Real") : "Denied: " + result.ErrorMessage)}");
            return result;
        }

        public RegistryAccessResult DeleteRegistryKey(string pluginId, string keyPath)
        {
            var result = _registryService.DeleteKey(pluginId, keyPath);
            LogEvent(pluginId, $"Registry delete key: {keyPath} -> {(result.Allowed ? (result.IsVirtualized ? "Virtual" : "Real") : "Denied: " + result.ErrorMessage)}");
            return result;
        }

        #endregion

        #region File System Operations

        public FileAccessResult CheckFileRead(string pluginId, string filePath)
        {
            _processService.RecordFileRead(pluginId);
            var result = _fileSystemService.CheckReadAccess(pluginId, filePath);
            LogEvent(pluginId, $"File read: {filePath} -> {(result.IsVirtualized ? "Virtual" : "Real")}");
            return result;
        }

        public FileAccessResult CheckFileWrite(string pluginId, string filePath)
        {
            var result = _fileSystemService.CheckWriteAccess(pluginId, filePath);
            _processService.RecordFileWrite(pluginId, result.IsVirtualized, !result.Allowed);
            LogEvent(pluginId, $"File write: {filePath} -> {(result.Allowed ? (result.IsVirtualized ? "Virtual" : "Real") : "Blocked")}");
            return result;
        }

        public FileAccessResult CheckFileDelete(string pluginId, string filePath)
        {
            var result = _fileSystemService.CheckDeleteAccess(pluginId, filePath);
            LogEvent(pluginId, $"File delete: {filePath} -> {(result.IsVirtualized ? "Virtual" : "Real")}");
            return result;
        }

        public FileAccessResult CheckDirectoryCreate(string pluginId, string dirPath)
        {
            var result = _fileSystemService.CheckDirectoryCreateAccess(pluginId, dirPath);
            LogEvent(pluginId, $"Directory create: {dirPath} -> {(result.IsVirtualized ? "Virtual" : "Real")}");
            return result;
        }

        public FileAccessResult CheckDirectoryDelete(string pluginId, string dirPath)
        {
            var result = _fileSystemService.CheckDirectoryDeleteAccess(pluginId, dirPath);
            LogEvent(pluginId, $"Directory delete: {dirPath} -> {(result.IsVirtualized ? "Virtual" : "Real")}");
            return result;
        }

        #endregion

        #region Process Operations

        public ProcessAccessResult CheckProcessMemoryAccess(string pluginId, int processId, string accessType)
        {
            var result = _processService.CheckProcessAccess(pluginId, processId, accessType);
            LogEvent(pluginId, $"Process memory access: PID {processId} ({accessType}) -> {(result.Allowed ? "Allowed" : "Blocked")}");
            return result;
        }

        public ProcessAccessResult CheckProcessStart(string pluginId, string processName, string arguments)
        {
            var result = _processService.CheckProcessStart(pluginId, processName, arguments);
            LogEvent(pluginId, $"Process start: {processName} -> {(result.Allowed ? "Allowed" : "Blocked")}");
            return result;
        }

        public ProcessAccessResult CheckProcessKill(string pluginId, int processId)
        {
            var result = _processService.CheckProcessKill(pluginId, processId);
            LogEvent(pluginId, $"Process kill: PID {processId} -> {(result.Allowed ? "Allowed" : "Blocked")}");
            return result;
        }

        #endregion

        #region Data Retrieval

        public List<VirtualRegistryEntry> GetVirtualRegistryEntries(string pluginId)
        {
            return _registryService.GetVirtualEntries(pluginId);
        }

        public Dictionary<string, List<VirtualRegistryEntry>> GetAllVirtualRegistryEntries()
        {
            return _registryService.GetAllVirtualEntries();
        }

        public List<VirtualFileEntry> GetVirtualFileEntries(string pluginId)
        {
            return _fileSystemService.GetVirtualEntries(pluginId);
        }

        public Dictionary<string, List<VirtualFileEntry>> GetAllVirtualFileEntries()
        {
            return _fileSystemService.GetAllVirtualEntries();
        }

        public VirtualizationStatistics GetStatistics(string pluginId)
        {
            return _processService.GetStatistics(pluginId);
        }

        public Dictionary<string, VirtualizationStatistics> GetAllStatistics()
        {
            return _processService.GetAllStatistics();
        }

        public List<PluginActivityRecord> GetActivityRecords(string pluginId)
        {
            return _processService.GetActivityRecords(pluginId);
        }

        public Dictionary<string, List<PluginActivityRecord>> GetAllActivityRecords()
        {
            return _processService.GetAllActivityRecords();
        }

        public void RecordActivity(string pluginId, string activityType, string category, string target, string detail, bool isVirtualized, bool isBlocked, string result)
        {
            _processService.RecordActivity(pluginId, activityType, category, target, detail, isVirtualized, isBlocked, result);
        }

        public PluginVirtualizationData GetPluginVirtualizationData(string pluginId)
        {
            bool supportSandbox = true;
            if (_pluginManager != null)
            {
                var pluginInfo = _pluginManager.GetPluginInfo(pluginId);
                if (pluginInfo != null)
                {
                    supportSandbox = pluginInfo.SupportSandbox;
                }
            }
            
            return new PluginVirtualizationData
            {
                PluginId = pluginId,
                IsVirtualizationEnabled = IsVirtualizationEnabled(pluginId),
                SupportSandbox = supportSandbox,
                RegistryEntries = GetVirtualRegistryEntries(pluginId),
                FileEntries = GetVirtualFileEntries(pluginId),
                Statistics = GetStatistics(pluginId),
                ActivityRecords = GetActivityRecords(pluginId)
            };
        }

        public List<PluginVirtualizationData> GetAllPluginVirtualizationData()
        {
            var result = new List<PluginVirtualizationData>();
            var allStats = GetAllStatistics();
            var allRegistry = GetAllVirtualRegistryEntries();
            var allFiles = GetAllVirtualFileEntries();
            var allActivities = GetAllActivityRecords();

            var allPluginIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            // 添加所有已加载的插件（包括禁用的）
            if (_pluginManager != null)
            {
                foreach (var pluginInfo in _pluginManager.GetAllPluginInfos())
                {
                    if (pluginInfo?.Id != null)
                    {
                        allPluginIds.Add(pluginInfo.Id);
                    }
                }
            }
            
            // 添加有虚拟化数据的插件
            foreach (var id in allStats.Keys) allPluginIds.Add(id);
            foreach (var id in allRegistry.Keys) allPluginIds.Add(id);
            foreach (var id in allFiles.Keys) allPluginIds.Add(id);
            foreach (var id in allActivities.Keys) allPluginIds.Add(id);
            
            lock (_lock)
            {
                foreach (var id in _pluginVirtualizationEnabled.Keys)
                {
                    allPluginIds.Add(id);
                }
            }

            foreach (var pluginId in allPluginIds)
            {
                result.Add(GetPluginVirtualizationData(pluginId));
            }

            return result;
        }

        #endregion

        #region Cleanup

        public void ClearPluginData(string pluginId)
        {
            _registryService.ClearPluginStore(pluginId);
            _fileSystemService.ClearPluginStore(pluginId);
            _processService.ClearPluginData(pluginId);
            LogEvent(pluginId, "Virtualization data cleared");
        }

        public void ClearAllData()
        {
            lock (_lock)
            {
                foreach (var pluginId in _pluginVirtualizationEnabled.Keys.ToList())
                {
                    ClearPluginData(pluginId);
                }
            }
        }

        #endregion

        private void LogEvent(string pluginId, string message)
        {
            using (PluginExecutionContext.BeginPluginScope(null))
            {
                Logger.LogInfo("Virtualization", $"[{pluginId}] {message}");
            }
            OnVirtualizationEvent?.Invoke(pluginId, message);
        }
    }
}
