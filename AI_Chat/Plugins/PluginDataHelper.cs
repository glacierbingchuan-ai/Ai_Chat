using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件数据帮助类 - 提供配置和数据文件操作
    /// </summary>
    public class PluginDataHelper
    {
        private readonly string _pluginId;
        private readonly string _dataDirectory;
        private readonly string _configDirectory;
        private readonly Dictionary<string, object> _configCache;
        private readonly string _configFilePath;

        public PluginDataHelper(string pluginId, string dataDirectory, string configDirectory)
        {
            _pluginId = pluginId ?? throw new ArgumentNullException(nameof(pluginId));
            _dataDirectory = dataDirectory ?? throw new ArgumentNullException(nameof(dataDirectory));
            _configDirectory = configDirectory ?? throw new ArgumentNullException(nameof(configDirectory));
            _configCache = new Dictionary<string, object>();
            _configFilePath = Path.Combine(_configDirectory, $"{pluginId}.json");

            EnsureDirectory(_dataDirectory);
            EnsureDirectory(_configDirectory);
            LoadConfig();
        }

        #region 路径

        /// <summary>
        /// 获取数据目录路径
        /// </summary>
        public string DataPath => _dataDirectory;

        /// <summary>
        /// 获取配置目录路径
        /// </summary>
        public string ConfigPath => _configDirectory;

        /// <summary>
        /// 获取配置文件路径
        /// </summary>
        public string ConfigFile => _configFilePath;

        /// <summary>
        /// 获取数据文件完整路径
        /// </summary>
        public string GetPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                throw new ArgumentException("路径不能为空", nameof(relativePath));

            string fullPath = Path.Combine(_dataDirectory, relativePath);
            ValidatePath(fullPath);
            return fullPath;
        }

        #endregion

        #region 配置操作

        /// <summary>
        /// 获取配置值
        /// </summary>
        public T Get<T>(string key, T defaultValue = default)
        {
            if (_configCache.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is T typedValue) return typedValue;
                    if (typeof(T) == typeof(string)) return (T)(object)value?.ToString();
                    if (typeof(T) == typeof(bool) && value is bool b) return (T)(object)b;
                    if (typeof(T) == typeof(int)) return (T)(object)Convert.ToInt32(value);
                    if (typeof(T) == typeof(double)) return (T)(object)Convert.ToDouble(value);
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PluginDataHelper] Failed to convert config value for key '{key}': {ex.Message}");
                }
            }
            return defaultValue;
        }

        /// <summary>
        /// 设置配置值
        /// </summary>
        public void Set<T>(string key, T value)
        {
            _configCache[key] = value;
        }

        /// <summary>
        /// 检查配置是否存在
        /// </summary>
        public bool Has(string key) => _configCache.ContainsKey(key);

        /// <summary>
        /// 移除配置
        /// </summary>
        public bool Remove(string key) => _configCache.Remove(key);

        /// <summary>
        /// 清除所有配置
        /// </summary>
        public void Clear() => _configCache.Clear();

        /// <summary>
        /// 获取所有配置
        /// </summary>
        public Dictionary<string, object> GetAll() => new Dictionary<string, object>(_configCache);

        /// <summary>
        /// 设置所有配置
        /// </summary>
        public void SetAll(Dictionary<string, object> config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            _configCache.Clear();
            foreach (var item in config) _configCache[item.Key] = item.Value;
        }

        /// <summary>
        /// 保存配置到文件
        /// </summary>
        public void SaveConfig()
        {
            var json = JsonConvert.SerializeObject(_configCache, Formatting.Indented);
            File.WriteAllText(_configFilePath, json);
        }

        /// <summary>
        /// 从文件加载配置
        /// </summary>
        public void LoadConfig()
        {
            if (!File.Exists(_configFilePath)) return;
            try
            {
                var json = File.ReadAllText(_configFilePath);
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);
                if (loaded != null)
                {
                    _configCache.Clear();
                    foreach (var item in loaded) _configCache[item.Key] = item.Value;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PluginDataHelper] Failed to load config: {ex.Message}");
            }
        }

        #endregion

        #region JSON数据

        /// <summary>
        /// 保存对象到JSON文件
        /// </summary>
        public void SaveJson<T>(string path, T data, Formatting formatting = Formatting.Indented)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.WriteAllText(fullPath, JsonConvert.SerializeObject(data, formatting));
        }

        /// <summary>
        /// 从JSON文件加载对象
        /// </summary>
        public T LoadJson<T>(string path, T defaultValue = default)
        {
            string fullPath = GetPath(path);
            if (!File.Exists(fullPath)) return defaultValue;
            return JsonConvert.DeserializeObject<T>(File.ReadAllText(fullPath));
        }

        #endregion

        #region 文本文件

        /// <summary>
        /// 读取文本文件
        /// </summary>
        public string ReadText(string path)
        {
            string fullPath = GetPath(path);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
        }

        /// <summary>
        /// 读取文本文件（指定编码）
        /// </summary>
        public string ReadText(string path, Encoding encoding)
        {
            string fullPath = GetPath(path);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath, encoding) : null;
        }

        /// <summary>
        /// 写入文本文件
        /// </summary>
        public void WriteText(string path, string content)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.WriteAllText(fullPath, content);
        }

        /// <summary>
        /// 写入文本文件（指定编码）
        /// </summary>
        public void WriteText(string path, string content, Encoding encoding)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.WriteAllText(fullPath, content, encoding);
        }

        /// <summary>
        /// 追加文本
        /// </summary>
        public void AppendText(string path, string content)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.AppendAllText(fullPath, content);
        }

        /// <summary>
        /// 追加文本（指定编码）
        /// </summary>
        public void AppendText(string path, string content, Encoding encoding)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.AppendAllText(fullPath, content, encoding);
        }

        /// <summary>
        /// 读取所有行
        /// </summary>
        public string[] ReadLines(string path)
        {
            string fullPath = GetPath(path);
            return File.Exists(fullPath) ? File.ReadAllLines(fullPath) : null;
        }

        /// <summary>
        /// 写入多行
        /// </summary>
        public void WriteLines(string path, string[] lines)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.WriteAllLines(fullPath, lines);
        }

        #endregion

        #region 二进制文件

        /// <summary>
        /// 读取字节
        /// </summary>
        public byte[] ReadBytes(string path)
        {
            string fullPath = GetPath(path);
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : null;
        }

        /// <summary>
        /// 写入字节
        /// </summary>
        public void WriteBytes(string path, byte[] bytes)
        {
            string fullPath = GetPath(path);
            EnsureParentDir(fullPath);
            File.WriteAllBytes(fullPath, bytes);
        }

        #endregion

        #region 文件操作

        /// <summary>
        /// 检查文件是否存在
        /// </summary>
        public bool Exists(string path) => File.Exists(GetPath(path));

        /// <summary>
        /// 删除文件
        /// </summary>
        public bool Delete(string path)
        {
            string fullPath = GetPath(path);
            if (!File.Exists(fullPath)) return false;
            File.Delete(fullPath);
            return true;
        }

        /// <summary>
        /// 复制文件
        /// </summary>
        public void Copy(string source, string dest, bool overwrite = false)
        {
            string sourcePath = GetPath(source);
            string destPath = GetPath(dest);
            EnsureParentDir(destPath);
            File.Copy(sourcePath, destPath, overwrite);
        }

        /// <summary>
        /// 移动文件
        /// </summary>
        public void Move(string source, string dest)
        {
            string sourcePath = GetPath(source);
            string destPath = GetPath(dest);
            EnsureParentDir(destPath);
            File.Move(sourcePath, destPath);
        }

        /// <summary>
        /// 获取文件信息
        /// </summary>
        public FileInfo Info(string path)
        {
            string fullPath = GetPath(path);
            return File.Exists(fullPath) ? new FileInfo(fullPath) : null;
        }

        #endregion

        #region 目录操作

        /// <summary>
        /// 创建目录
        /// </summary>
        public void CreateDir(string path)
        {
            string fullPath = GetPath(path);
            Directory.CreateDirectory(fullPath);
        }

        /// <summary>
        /// 检查目录是否存在
        /// </summary>
        public bool DirExists(string path) => Directory.Exists(GetPath(path));

        /// <summary>
        /// 删除目录
        /// </summary>
        public bool DeleteDir(string path, bool recursive = false)
        {
            string fullPath = GetPath(path);
            if (!Directory.Exists(fullPath)) return false;
            Directory.Delete(fullPath, recursive);
            return true;
        }

        /// <summary>
        /// 获取文件列表
        /// </summary>
        public string[] Files(string path = "", string pattern = "*.*")
        {
            string fullPath = string.IsNullOrEmpty(path) ? _dataDirectory : GetPath(path);
            return Directory.Exists(fullPath) ? Directory.GetFiles(fullPath, pattern) : new string[0];
        }

        /// <summary>
        /// 获取目录列表
        /// </summary>
        public string[] Dirs(string path = "")
        {
            string fullPath = string.IsNullOrEmpty(path) ? _dataDirectory : GetPath(path);
            return Directory.Exists(fullPath) ? Directory.GetDirectories(fullPath) : new string[0];
        }

        /// <summary>
        /// 获取所有条目
        /// </summary>
        public string[] Entries(string path = "", string pattern = "*.*")
        {
            string fullPath = string.IsNullOrEmpty(path) ? _dataDirectory : GetPath(path);
            return Directory.Exists(fullPath) ? Directory.GetFileSystemEntries(fullPath, pattern) : new string[0];
        }

        #endregion

        #region 辅助

        private void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }

        private void EnsureParentDir(string filePath)
        {
            string parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);
        }

        private void ValidatePath(string fullPath)
        {
            string normalized = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
            string baseDir = Path.GetFullPath(_dataDirectory).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();

            if (!normalized.StartsWith(baseDir + Path.DirectorySeparatorChar) && !normalized.Equals(baseDir))
                throw new SecurityException($"路径 '{fullPath}' 超出允许范围");
        }

        #endregion
    }

    public class SecurityException : Exception
    {
        public SecurityException(string message) : base(message) { }
    }
}
