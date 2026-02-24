using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace AI_Chat.Plugins.Virtualization
{
    public class VirtualFileStore
    {
        private readonly string _storePath;
        private readonly string _pluginId;
        private readonly Dictionary<string, VirtualFileEntry> _entries;
        private readonly object _lock = new object();

        public VirtualFileStore(string baseDataPath, string pluginId)
        {
            _pluginId = pluginId;
            _storePath = Path.Combine(baseDataPath, "VirtualFileSystem", pluginId);
            _entries = new Dictionary<string, VirtualFileEntry>(StringComparer.OrdinalIgnoreCase);
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

        public bool TryGetEntry(string virtualPath, out VirtualFileEntry entry)
        {
            lock (_lock)
            {
                string normalizedPath = NormalizePath(virtualPath);
                if (_entries.TryGetValue(normalizedPath, out entry))
                {
                    if (entry.IsDeleted)
                    {
                        entry = null;
                        return false;
                    }
                    return true;
                }
                return false;
            }
        }

        public bool IsPathDeleted(string virtualPath)
        {
            lock (_lock)
            {
                string normalizedPath = NormalizePath(virtualPath);
                if (_entries.TryGetValue(normalizedPath, out var entry))
                {
                    return entry.IsDeleted;
                }
                return false;
            }
        }

        public bool IsUnderDeletedDirectory(string virtualPath)
        {
            lock (_lock)
            {
                string normalizedPath = NormalizePath(virtualPath);
                foreach (var entry in _entries.Values)
                {
                    if (entry.IsDirectory && entry.IsDeleted)
                    {
                        string deletedDir = NormalizePath(entry.VirtualPath);
                        if (normalizedPath.StartsWith(deletedDir + "\\", StringComparison.OrdinalIgnoreCase) ||
                            normalizedPath.Equals(deletedDir, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
                return false;
            }
        }

        public void AddEntry(VirtualFileEntry entry)
        {
            lock (_lock)
            {
                string normalizedPath = NormalizePath(entry.VirtualPath);
                entry.VirtualPath = normalizedPath;
                _entries[normalizedPath] = entry;
                SaveToDisk();
            }
        }

        public bool RemoveEntry(string virtualPath)
        {
            lock (_lock)
            {
                string normalizedPath = NormalizePath(virtualPath);
                bool removed = _entries.Remove(normalizedPath);
                if (removed)
                {
                    SaveToDisk();
                }
                return removed;
            }
        }

        public void MarkAsDeleted(string virtualPath)
        {
            lock (_lock)
            {
                string normalizedPath = NormalizePath(virtualPath);
                if (_entries.TryGetValue(normalizedPath, out var entry))
                {
                    entry.IsDeleted = true;
                    SaveToDisk();
                }
            }
        }

        public List<VirtualFileEntry> GetAllEntries()
        {
            lock (_lock)
            {
                var entries = _entries.Values.ToList();
                bool sizeUpdated = false;
                // 更新每个条目的大小（用于序列化到前端）
                foreach (var entry in entries)
                {
                    long oldSize = entry.SerializedSize;
                    entry.UpdateSerializedSize();
                    if (entry.SerializedSize != oldSize)
                    {
                        sizeUpdated = true;
                    }
                }
                // 如果有大小变化，保存到磁盘
                if (sizeUpdated)
                {
                    SaveToDisk();
                }
                return entries;
            }
        }

        public List<VirtualFileEntry> GetEntriesUnderPath(string parentPath)
        {
            lock (_lock)
            {
                string normalizedParent = NormalizePath(parentPath);
                return _entries.Values
                    .Where(e => e.VirtualPath.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase))
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

        private string NormalizePath(string path)
        {
            return path?.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
        }

        private void LoadFromDisk()
        {
            string filePath = Path.Combine(_storePath, "files.json");
            
            using (PluginExecutionContext.BeginPluginScope(null))
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        string json = File.ReadAllText(filePath);
                        var loadedEntries = JsonConvert.DeserializeObject<List<VirtualFileEntry>>(json);
                        if (loadedEntries != null)
                        {
                            foreach (var entry in loadedEntries)
                            {
                                string normalizedPath = NormalizePath(entry.VirtualPath);
                                _entries[normalizedPath] = entry;
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
            string filePath = Path.Combine(_storePath, "files.json");
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

    public class VirtualFileSystemService
    {
        private readonly string _baseDataPath;
        private readonly Dictionary<string, VirtualFileStore> _pluginStores;
        private readonly VirtualizationConfig _config;
        private readonly object _lock = new object();

        public VirtualFileSystemService(string baseDataPath, VirtualizationConfig config)
        {
            _baseDataPath = baseDataPath;
            _config = config;
            _pluginStores = new Dictionary<string, VirtualFileStore>(StringComparer.OrdinalIgnoreCase);
        }

        public VirtualFileStore GetStore(string pluginId)
        {
            lock (_lock)
            {
                if (!_pluginStores.TryGetValue(pluginId, out var store))
                {
                    store = new VirtualFileStore(_baseDataPath, pluginId);
                    _pluginStores[pluginId] = store;
                }
                return store;
            }
        }

        public FileAccessResult CheckReadAccess(string pluginId, string filePath)
        {
            var result = new FileAccessResult
            {
                Allowed = true,
                VirtualPath = filePath,
                RealPath = filePath
            };

            if (!_config.EnableFileVirtualization)
            {
                result.IsVirtualized = false;
                return result;
            }

            if (IsExcludedPath(filePath))
            {
                result.IsVirtualized = false;
                return result;
            }

            var store = GetStore(pluginId);
            
            if (store.IsUnderDeletedDirectory(filePath))
            {
                result.Allowed = false;
                result.ErrorMessage = "Path is under a deleted directory in virtual environment";
                result.IsVirtualized = true;
                return result;
            }
            
            if (store.IsPathDeleted(filePath))
            {
                result.Allowed = false;
                result.ErrorMessage = "File has been deleted in virtual environment";
                result.IsVirtualized = true;
                return result;
            }

            if (store.TryGetEntry(filePath, out var entry))
            {
                result.IsVirtualized = true;
                result.RealPath = entry.RealPath;
            }
            else
            {
                result.IsVirtualized = false;
            }

            return result;
        }

        public FileAccessResult CheckWriteAccess(string pluginId, string filePath)
        {
            var result = new FileAccessResult
            {
                VirtualPath = filePath
            };

            string extension = Path.GetExtension(filePath)?.ToLowerInvariant();
            if (_config.BlockExeWrites && _config.BlockedFileExtensions.Contains(extension))
            {
                result.Allowed = false;
                result.ErrorMessage = $"Writing {extension} files is blocked for security reasons";
                return result;
            }

            if (IsExcludedPath(filePath))
            {
                result.Allowed = true;
                result.RealPath = filePath;
                result.IsVirtualized = false;
                return result;
            }

            if (!_config.EnableFileVirtualization)
            {
                result.Allowed = true;
                result.RealPath = filePath;
                result.IsVirtualized = false;
                return result;
            }

            var store = GetStore(pluginId);
            string virtualStorePath = GetVirtualStorePath(pluginId, filePath);

            var entry = new VirtualFileEntry
            {
                VirtualPath = filePath,
                RealPath = virtualStorePath,
                PluginId = pluginId,
                CreatedTime = DateTime.Now,
                LastModified = DateTime.Now,
                IsDirectory = false,
                IsDeleted = false
            };

            store.AddEntry(entry);

            ClearParentDirectoryDeletedMark(store, pluginId, filePath);

            result.Allowed = true;
            result.RealPath = virtualStorePath;
            result.IsVirtualized = true;

            return result;
        }

        private void ClearParentDirectoryDeletedMark(VirtualFileStore store, string pluginId, string filePath)
        {
            string parentDir = Path.GetDirectoryName(filePath);
            while (!string.IsNullOrEmpty(parentDir))
            {
                if (store.IsPathDeleted(parentDir))
                {
                    var clearEntry = new VirtualFileEntry
                    {
                        VirtualPath = parentDir,
                        RealPath = GetVirtualStorePath(pluginId, parentDir),
                        PluginId = pluginId,
                        CreatedTime = DateTime.Now,
                        LastModified = DateTime.Now,
                        IsDirectory = true,
                        IsDeleted = false
                    };
                    store.AddEntry(clearEntry);
                }
                parentDir = Path.GetDirectoryName(parentDir);
            }
        }

        public FileAccessResult CheckDeleteAccess(string pluginId, string filePath)
        {
            var result = new FileAccessResult
            {
                VirtualPath = filePath
            };

            if (!_config.EnableFileVirtualization)
            {
                result.Allowed = true;
                result.RealPath = filePath;
                result.IsVirtualized = false;
                return result;
            }

            if (IsExcludedPath(filePath))
            {
                result.Allowed = true;
                result.RealPath = filePath;
                result.IsVirtualized = false;
                return result;
            }

            // 检查真实环境中是否存在
            if (!File.Exists(filePath))
            {
                result.Allowed = false;
                result.IsVirtualized = true;
                result.ErrorMessage = "File not found in real environment";
                return result;
            }

            var store = GetStore(pluginId);
            
            var entry = new VirtualFileEntry
            {
                VirtualPath = filePath,
                RealPath = null,
                PluginId = pluginId,
                CreatedTime = DateTime.Now,
                LastModified = DateTime.Now,
                IsDirectory = false,
                IsDeleted = true
            };
            
            store.AddEntry(entry);
            
            result.Allowed = true;
            result.IsVirtualized = true;
            result.RealPath = filePath;

            return result;
        }

        public FileAccessResult CheckDirectoryCreateAccess(string pluginId, string dirPath)
        {
            var result = new FileAccessResult
            {
                VirtualPath = dirPath
            };

            if (!_config.EnableFileVirtualization)
            {
                result.Allowed = true;
                result.RealPath = dirPath;
                result.IsVirtualized = false;
                return result;
            }

            if (IsExcludedPath(dirPath))
            {
                result.Allowed = true;
                result.RealPath = dirPath;
                result.IsVirtualized = false;
                return result;
            }

            var store = GetStore(pluginId);
            string virtualStorePath = GetVirtualStorePath(pluginId, dirPath);

            var entry = new VirtualFileEntry
            {
                VirtualPath = dirPath,
                RealPath = virtualStorePath,
                PluginId = pluginId,
                CreatedTime = DateTime.Now,
                LastModified = DateTime.Now,
                IsDirectory = true,
                IsDeleted = false
            };

            store.AddEntry(entry);

            result.Allowed = true;
            result.RealPath = virtualStorePath;
            result.IsVirtualized = true;

            return result;
        }

        public FileAccessResult CheckDirectoryDeleteAccess(string pluginId, string dirPath)
        {
            var result = new FileAccessResult
            {
                VirtualPath = dirPath
            };

            if (!_config.EnableFileVirtualization)
            {
                result.Allowed = true;
                result.RealPath = dirPath;
                result.IsVirtualized = false;
                return result;
            }

            if (IsExcludedPath(dirPath))
            {
                result.Allowed = true;
                result.RealPath = dirPath;
                result.IsVirtualized = false;
                return result;
            }

            // 检查真实环境中是否存在
            if (!Directory.Exists(dirPath))
            {
                result.Allowed = false;
                result.IsVirtualized = true;
                result.ErrorMessage = "Directory not found in real environment";
                return result;
            }

            var store = GetStore(pluginId);

            var entry = new VirtualFileEntry
            {
                VirtualPath = dirPath,
                RealPath = null,
                PluginId = pluginId,
                CreatedTime = DateTime.Now,
                LastModified = DateTime.Now,
                IsDirectory = true,
                IsDeleted = true
            };

            store.AddEntry(entry);

            result.Allowed = true;
            result.IsVirtualized = true;
            result.RealPath = dirPath;

            return result;
        }

        private bool IsExcludedPath(string path)
        {
            if (_config.ExcludedPaths == null || _config.ExcludedPaths.Count == 0)
                return false;

            string normalizedPath = path?.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
            if (string.IsNullOrEmpty(normalizedPath))
                return false;

            foreach (var excludedPath in _config.ExcludedPaths)
            {
                string normalizedExcluded = excludedPath?.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();
                if (string.IsNullOrEmpty(normalizedExcluded))
                    continue;

                if (normalizedPath.StartsWith(normalizedExcluded + "\\") || 
                    normalizedPath.Equals(normalizedExcluded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        public List<VirtualFileEntry> GetVirtualEntries(string pluginId)
        {
            var store = GetStore(pluginId);
            return store.GetAllEntries();
        }

        public Dictionary<string, List<VirtualFileEntry>> GetAllVirtualEntries()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, List<VirtualFileEntry>>();
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

        private int _pathCounter = 0;
        private readonly Dictionary<string, string> _pathMapping = new Dictionary<string, string>();

        private string GetVirtualStorePath(string pluginId, string originalPath)
        {
            // 使用路径映射来生成短文件名
            string mappingKey = $"{pluginId}:{originalPath}";
            if (_pathMapping.TryGetValue(mappingKey, out string existingPath))
            {
                return existingPath;
            }

            // 生成新的短文件名
            string ext = Path.GetExtension(originalPath);
            string shortFileName = $"f{++_pathCounter}{ext}";
            string virtualPath = Path.Combine(_baseDataPath, "VirtualFileSystem", pluginId, "files", shortFileName);
            
            _pathMapping[mappingKey] = virtualPath;
            return virtualPath;
        }
    }
}
