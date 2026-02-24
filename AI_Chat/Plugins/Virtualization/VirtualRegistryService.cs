using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace AI_Chat.Plugins.Virtualization
{
    public class VirtualRegistryStore
    {
        private readonly string _storePath;
        private readonly string _pluginId;
        private readonly Dictionary<string, VirtualRegistryEntry> _entries;
        private readonly object _lock = new object();

        public VirtualRegistryStore(string baseDataPath, string pluginId)
        {
            _pluginId = pluginId;
            _storePath = Path.Combine(baseDataPath, "VirtualRegistry", pluginId);
            _entries = new Dictionary<string, VirtualRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            EnsureDirectoryExists();
            LoadFromDisk();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(_storePath))
            {
                Directory.CreateDirectory(_storePath);
            }
        }

        private string GetEntryKey(string keyPath, string valueName)
        {
            return $"{keyPath}\\{valueName ?? "(default)"}";
        }

        public bool TryGetValue(string keyPath, string valueName, out VirtualRegistryEntry entry)
        {
            lock (_lock)
            {
                string key = GetEntryKey(keyPath, valueName);
                if (_entries.TryGetValue(key, out entry))
                {
                    if (entry.IsDeleted || entry.IsKeyDeleted)
                    {
                        entry = null;
                        return false;
                    }
                    return true;
                }
                return false;
            }
        }

        public bool IsValueDeleted(string keyPath, string valueName)
        {
            lock (_lock)
            {
                string key = GetEntryKey(keyPath, valueName);
                if (_entries.TryGetValue(key, out var entry))
                {
                    return entry.IsDeleted || entry.IsKeyDeleted;
                }
                return false;
            }
        }

        public bool IsKeyDeleted(string keyPath)
        {
            lock (_lock)
            {
                string deleteMarkerKey = GetEntryKey(keyPath, "$$KEY_DELETED$$");
                if (_entries.TryGetValue(deleteMarkerKey, out var entry))
                {
                    return entry.IsKeyDeleted;
                }
                return false;
            }
        }

        public void SetValue(string keyPath, string valueName, object value, string valueKind)
        {
            lock (_lock)
            {
                string key = GetEntryKey(keyPath, valueName);
                var entry = new VirtualRegistryEntry
                {
                    KeyPath = keyPath,
                    ValueName = valueName,
                    Value = value,
                    ValueKind = valueKind,
                    LastModified = DateTime.Now,
                    PluginId = _pluginId,
                    IsDeleted = false,
                    IsKeyDeleted = false
                };
                _entries[key] = entry;
                
                string deleteMarkerKey = GetEntryKey(keyPath, "$$KEY_DELETED$$");
                _entries.Remove(deleteMarkerKey);
                
                SaveToDisk();
            }
        }

        public bool DeleteValue(string keyPath, string valueName)
        {
            lock (_lock)
            {
                string key = GetEntryKey(keyPath, valueName);
                if (_entries.TryGetValue(key, out var entry))
                {
                    entry.IsDeleted = true;
                    entry.LastModified = DateTime.Now;
                }
                else
                {
                    _entries[key] = new VirtualRegistryEntry
                    {
                        KeyPath = keyPath,
                        ValueName = valueName,
                        IsDeleted = true,
                        LastModified = DateTime.Now,
                        PluginId = _pluginId
                    };
                }
                SaveToDisk();
                return true;
            }
        }

        public bool DeleteKey(string keyPath)
        {
            lock (_lock)
            {
                string deleteMarkerKey = GetEntryKey(keyPath, "$$KEY_DELETED$$");
                _entries[deleteMarkerKey] = new VirtualRegistryEntry
                {
                    KeyPath = keyPath,
                    ValueName = "$$KEY_DELETED$$",
                    IsKeyDeleted = true,
                    LastModified = DateTime.Now,
                    PluginId = _pluginId
                };

                foreach (var entry in _entries.Values.ToList())
                {
                    if (entry.KeyPath.StartsWith(keyPath, StringComparison.OrdinalIgnoreCase) &&
                        entry.KeyPath.Length > keyPath.Length)
                    {
                        entry.IsKeyDeleted = true;
                        entry.LastModified = DateTime.Now;
                    }
                }

                SaveToDisk();
                return true;
            }
        }

        public List<VirtualRegistryEntry> GetSubKeys(string parentKeyPath)
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.KeyPath.StartsWith(parentKeyPath, StringComparison.OrdinalIgnoreCase) &&
                                e.KeyPath.Length > parentKeyPath.Length)
                    .ToList();
            }
        }

        public List<VirtualRegistryEntry> GetAllEntries()
        {
            lock (_lock)
            {
                return _entries.Values
                    .Where(e => e.ValueName != "$$KEY_DELETED$$")
                    .ToList();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                SaveToDisk();
            }
        }

        private void LoadFromDisk()
        {
            string filePath = Path.Combine(_storePath, "registry.json");
            
            using (PluginExecutionContext.BeginPluginScope(null))
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        string json = File.ReadAllText(filePath);
                        var loadedEntries = JsonConvert.DeserializeObject<List<VirtualRegistryEntry>>(json);
                        if (loadedEntries != null)
                        {
                            foreach (var entry in loadedEntries)
                            {
                                string key = GetEntryKey(entry.KeyPath, entry.ValueName);
                                _entries[key] = entry;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private void SaveToDisk()
        {
            string filePath = Path.Combine(_storePath, "registry.json");
            try
            {
                var entriesList = _entries.Values.ToList();
                string json = JsonConvert.SerializeObject(entriesList, Formatting.Indented);
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    File.WriteAllText(filePath, json);
                }
            }
            catch
            {
            }
        }
    }

    public class VirtualRegistryService
    {
        private readonly string _baseDataPath;
        private readonly Dictionary<string, VirtualRegistryStore> _pluginStores;
        private readonly VirtualizationConfig _config;
        private readonly object _lock = new object();

        public VirtualRegistryService(string baseDataPath, VirtualizationConfig config)
        {
            _baseDataPath = baseDataPath;
            _config = config;
            _pluginStores = new Dictionary<string, VirtualRegistryStore>(StringComparer.OrdinalIgnoreCase);
        }

        public VirtualRegistryStore GetStore(string pluginId)
        {
            lock (_lock)
            {
                if (!_pluginStores.TryGetValue(pluginId, out var store))
                {
                    store = new VirtualRegistryStore(_baseDataPath, pluginId);
                    _pluginStores[pluginId] = store;
                }
                return store;
            }
        }

        public RegistryAccessResult ReadValue(string pluginId, string keyPath, string valueName)
        {
            var result = new RegistryAccessResult();

            if (!_config.EnableRegistryVirtualization)
            {
                result.Allowed = true;
                result.IsVirtualized = false;
                result.ExistsInReal = RealRegistryExists(keyPath, valueName);
                if (result.ExistsInReal)
                {
                    result.Value = GetRealRegistryValue(keyPath, valueName, out string kind);
                    result.ValueKind = kind;
                }
                return result;
            }

            var store = GetStore(pluginId);
            
            if (store.IsKeyDeleted(keyPath) || store.IsValueDeleted(keyPath, valueName))
            {
                result.Allowed = true;
                result.IsVirtualized = true;
                result.ExistsInVirtual = false;
                result.ExistsInReal = false;
                result.WasDeleted = true;
                return result;
            }

            if (store.TryGetValue(keyPath, valueName, out var virtualEntry))
            {
                result.Allowed = true;
                result.IsVirtualized = true;
                result.ExistsInVirtual = true;
                result.Value = virtualEntry.Value;
                result.ValueKind = virtualEntry.ValueKind;
                return result;
            }

            result.ExistsInReal = RealRegistryExists(keyPath, valueName);
            if (result.ExistsInReal)
            {
                result.Allowed = true;
                result.IsVirtualized = false;
                result.Value = GetRealRegistryValue(keyPath, valueName, out string kind);
                result.ValueKind = kind;
            }
            else
            {
                result.Allowed = true;
                result.ExistsInVirtual = false;
                result.ExistsInReal = false;
            }

            return result;
        }

        public RegistryAccessResult WriteValue(string pluginId, string keyPath, string valueName, object value, string valueKind)
        {
            var result = new RegistryAccessResult();

            if (!_config.EnableRegistryVirtualization)
            {
                result.Allowed = true;
                result.IsVirtualized = false;
                try
                {
                    SetRealRegistryValue(keyPath, valueName, value, valueKind);
                }
                catch (Exception ex)
                {
                    result.Allowed = false;
                    result.ErrorMessage = ex.Message;
                }
                return result;
            }

            var store = GetStore(pluginId);
            store.SetValue(keyPath, valueName, value, valueKind);

            result.Allowed = true;
            result.IsVirtualized = true;
            result.ExistsInVirtual = true;
            return result;
        }

        public RegistryAccessResult DeleteValue(string pluginId, string keyPath, string valueName)
        {
            var result = new RegistryAccessResult();

            if (!_config.EnableRegistryVirtualization)
            {
                result.Allowed = true;
                result.IsVirtualized = false;
                try
                {
                    using (var baseKey = GetBaseKey(keyPath, out string subPath))
                    {
                        if (baseKey == null) throw new InvalidOperationException("Invalid registry key path");
                        using (var subKey = baseKey.OpenSubKey(subPath, true))
                        {
                            if (subKey == null) throw new InvalidOperationException("Registry key not found");
                            subKey.DeleteValue(valueName, false);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Allowed = false;
                    result.ErrorMessage = ex.Message;
                }
                return result;
            }

            // 检查真实注册表中是否存在
            bool existsInReal = RealRegistryExists(keyPath, valueName);
            if (!existsInReal)
            {
                result.Allowed = false;
                result.IsVirtualized = true;
                result.ErrorMessage = "Registry value not found in real environment";
                return result;
            }

            var store = GetStore(pluginId);
            store.DeleteValue(keyPath, valueName);

            result.Allowed = true;
            result.IsVirtualized = true;
            return result;
        }

        public RegistryAccessResult DeleteKey(string pluginId, string keyPath)
        {
            var result = new RegistryAccessResult();

            if (!_config.EnableRegistryVirtualization)
            {
                result.Allowed = true;
                result.IsVirtualized = false;
                try
                {
                    using (var baseKey = GetBaseKey(keyPath, out string subPath))
                    {
                        if (baseKey == null) throw new InvalidOperationException("Invalid registry key path");
                        baseKey.DeleteSubKeyTree(subPath, false);
                    }
                }
                catch (Exception ex)
                {
                    result.Allowed = false;
                    result.ErrorMessage = ex.Message;
                }
                return result;
            }

            // 检查真实注册表中是否存在
            bool existsInReal = RealRegistryKeyExists(keyPath);
            if (!existsInReal)
            {
                result.Allowed = false;
                result.IsVirtualized = true;
                result.ErrorMessage = "Registry key not found in real environment";
                return result;
            }

            var store = GetStore(pluginId);
            store.DeleteKey(keyPath);

            result.Allowed = true;
            result.IsVirtualized = true;
            return result;
        }

        public List<VirtualRegistryEntry> GetVirtualEntries(string pluginId)
        {
            var store = GetStore(pluginId);
            return store.GetAllEntries();
        }

        public Dictionary<string, List<VirtualRegistryEntry>> GetAllVirtualEntries()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, List<VirtualRegistryEntry>>();
                foreach (var kvp in _pluginStores)
                {
                    var entries = kvp.Value.GetAllEntries();
                    if (entries.Count > 0)
                    {
                        result[kvp.Key] = entries;
                    }
                }
                return result;
            }
        }

        public void ClearPluginStore(string pluginId)
        {
            var store = GetStore(pluginId);
            store.Clear();
        }

        private bool RealRegistryExists(string keyPath, string valueName)
        {
            try
            {
                using (var baseKey = GetBaseKey(keyPath, out string subPath))
                {
                    if (baseKey == null) return false;
                    using (var subKey = baseKey.OpenSubKey(subPath))
                    {
                        if (subKey == null) return false;
                        if (string.IsNullOrEmpty(valueName) || valueName == "(default)")
                        {
                            return subKey.GetValue(null) != null;
                        }
                        return subKey.GetValue(valueName) != null;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private bool RealRegistryKeyExists(string keyPath)
        {
            try
            {
                using (var baseKey = GetBaseKey(keyPath, out string subPath))
                {
                    if (baseKey == null) return false;
                    using (var subKey = baseKey.OpenSubKey(subPath))
                    {
                        return subKey != null;
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private object GetRealRegistryValue(string keyPath, string valueName, out string valueKind)
        {
            valueKind = "String";
            try
            {
                using (var baseKey = GetBaseKey(keyPath, out string subPath))
                {
                    if (baseKey == null) return null;
                    using (var subKey = baseKey.OpenSubKey(subPath))
                    {
                        if (subKey == null) return null;
                        var value = string.IsNullOrEmpty(valueName) || valueName == "(default)"
                            ? subKey.GetValue(null)
                            : subKey.GetValue(valueName);
                        if (value != null)
                        {
                            valueKind = subKey.GetValueKind(valueName ?? "").ToString();
                        }
                        return value;
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private void SetRealRegistryValue(string keyPath, string valueName, object value, string valueKind)
        {
            using (var baseKey = GetBaseKey(keyPath, out string subPath))
            {
                if (baseKey == null) throw new InvalidOperationException("Invalid registry key path");
                using (var subKey = baseKey.CreateSubKey(subPath))
                {
                    if (subKey == null) throw new InvalidOperationException("Failed to create registry key");
                    RegistryValueKind kind = ParseValueKind(valueKind);
                    subKey.SetValue(valueName ?? "", value, kind);
                }
            }
        }

        private RegistryKey GetBaseKey(string keyPath, out string subPath)
        {
            subPath = "";
            if (string.IsNullOrEmpty(keyPath)) return null;

            string[] parts = keyPath.Split(new[] { '\\' }, 2);
            if (parts.Length == 0) return null;

            string rootName = parts[0];
            subPath = parts.Length > 1 ? parts[1] : "";

            switch (rootName.ToUpper())
            {
                case "HKEY_LOCAL_MACHINE":
                case "HKLM":
                    return Registry.LocalMachine;
                case "HKEY_CURRENT_USER":
                case "HKCU":
                    return Registry.CurrentUser;
                case "HKEY_CLASSES_ROOT":
                case "HKCR":
                    return Registry.ClassesRoot;
                case "HKEY_USERS":
                case "HKU":
                    return Registry.Users;
                case "HKEY_CURRENT_CONFIG":
                case "HKCC":
                    return Registry.CurrentConfig;
                default:
                    return null;
            }
        }

        private RegistryValueKind ParseValueKind(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return RegistryValueKind.String;
            switch (kind.ToLower())
            {
                case "string": return RegistryValueKind.String;
                case "expandstring": return RegistryValueKind.ExpandString;
                case "binary": return RegistryValueKind.Binary;
                case "dword": return RegistryValueKind.DWord;
                case "qword": return RegistryValueKind.QWord;
                case "multistring": return RegistryValueKind.MultiString;
                default: return RegistryValueKind.String;
            }
        }
    }
}
