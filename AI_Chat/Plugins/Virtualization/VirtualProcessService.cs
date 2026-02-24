using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace AI_Chat.Plugins.Virtualization
{
    public class VirtualProcessService
    {
        private readonly VirtualizationConfig _config;
        private readonly Dictionary<string, VirtualizationStatistics> _statistics;
        private readonly Dictionary<string, List<VirtualProcessEntry>> _virtualProcesses;
        private readonly Dictionary<string, List<PluginActivityRecord>> _activityRecords;
        private readonly object _lock = new object();
        private int _nextVirtualProcessId = 10000;
        private const int MaxRecordsPerPlugin = 500;

        public VirtualProcessService(VirtualizationConfig config)
        {
            _config = config;
            _statistics = new Dictionary<string, VirtualizationStatistics>(StringComparer.OrdinalIgnoreCase);
            _virtualProcesses = new Dictionary<string, List<VirtualProcessEntry>>(StringComparer.OrdinalIgnoreCase);
            _activityRecords = new Dictionary<string, List<PluginActivityRecord>>(StringComparer.OrdinalIgnoreCase);
        }

        public void RecordActivity(string pluginId, string activityType, string category, string target, string detail, bool isVirtualized, bool isBlocked, string result)
        {
            lock (_lock)
            {
                if (!_activityRecords.TryGetValue(pluginId, out var records))
                {
                    records = new List<PluginActivityRecord>();
                    _activityRecords[pluginId] = records;
                }

                records.Insert(0, new PluginActivityRecord
                {
                    PluginId = pluginId,
                    Timestamp = DateTime.Now,
                    ActivityType = activityType,
                    Category = category,
                    Target = target,
                    Detail = detail,
                    IsVirtualized = isVirtualized,
                    IsBlocked = isBlocked,
                    Result = result
                });

                if (records.Count > MaxRecordsPerPlugin)
                {
                    records.RemoveRange(MaxRecordsPerPlugin, records.Count - MaxRecordsPerPlugin);
                }
            }
        }

        public List<PluginActivityRecord> GetActivityRecords(string pluginId)
        {
            lock (_lock)
            {
                if (_activityRecords.TryGetValue(pluginId, out var records))
                {
                    return records.ToList();
                }
                return new List<PluginActivityRecord>();
            }
        }

        public Dictionary<string, List<PluginActivityRecord>> GetAllActivityRecords()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, List<PluginActivityRecord>>();
                foreach (var kvp in _activityRecords)
                {
                    if (kvp.Value.Count > 0)
                    {
                        result[kvp.Key] = kvp.Value.ToList();
                    }
                }
                return result;
            }
        }

        public void ClearActivityRecords(string pluginId)
        {
            lock (_lock)
            {
                _activityRecords.Remove(pluginId);
            }
        }

        public ProcessAccessResult CheckProcessAccess(string pluginId, int processId, string accessType)
        {
            var result = new ProcessAccessResult();

            if (!_config.EnableProcessInterception)
            {
                result.Allowed = true;
                return result;
            }

            lock (_lock)
            {
                UpdateStatistics(pluginId, s => s.ProcessAccessAttempts++);

                result.Allowed = false;
                result.ErrorMessage = "Process memory access is blocked for security reasons. Plugins cannot access other processes' memory.";

                UpdateStatistics(pluginId, s => s.ProcessAccessBlocked++);
            }

            return result;
        }

        public ProcessAccessResult CheckProcessStart(string pluginId, string processName, string arguments)
        {
            var result = new ProcessAccessResult();

            if (!_config.EnableProcessInterception)
            {
                result.Allowed = true;
                return result;
            }

            lock (_lock)
            {
                UpdateStatistics(pluginId, s => s.ProcessAccessAttempts++);

                result.Allowed = false;
                result.ErrorMessage = "Starting new processes is blocked in the virtualized environment.";

                UpdateStatistics(pluginId, s => s.ProcessAccessBlocked++);
            }

            return result;
        }

        public ProcessAccessResult CheckProcessKill(string pluginId, int processId)
        {
            var result = new ProcessAccessResult();

            if (!_config.EnableProcessInterception)
            {
                result.Allowed = true;
                return result;
            }

            lock (_lock)
            {
                UpdateStatistics(pluginId, s => s.ProcessAccessAttempts++);

                result.Allowed = false;
                result.ErrorMessage = "Killing processes is blocked in the virtualized environment.";

                UpdateStatistics(pluginId, s => s.ProcessAccessBlocked++);
            }

            return result;
        }

        public ProcessAccessResult CreateVirtualProcess(string pluginId, string processName)
        {
            var result = new ProcessAccessResult();

            lock (_lock)
            {
                int virtualPid = _nextVirtualProcessId++;
                var entry = new VirtualProcessEntry
                {
                    VirtualProcessId = virtualPid,
                    ProcessName = processName,
                    PluginId = pluginId,
                    CreatedTime = DateTime.Now,
                    IsActive = true
                };

                if (!_virtualProcesses.ContainsKey(pluginId))
                {
                    _virtualProcesses[pluginId] = new List<VirtualProcessEntry>();
                }
                _virtualProcesses[pluginId].Add(entry);

                result.Allowed = true;
                result.VirtualProcessId = virtualPid;
            }

            return result;
        }

        public bool TerminateVirtualProcess(string pluginId, int virtualProcessId)
        {
            lock (_lock)
            {
                if (_virtualProcesses.TryGetValue(pluginId, out var processes))
                {
                    var process = processes.FirstOrDefault(p => p.VirtualProcessId == virtualProcessId);
                    if (process != null)
                    {
                        process.IsActive = false;
                        return true;
                    }
                }
                return false;
            }
        }

        public List<VirtualProcessEntry> GetVirtualProcesses(string pluginId)
        {
            lock (_lock)
            {
                if (_virtualProcesses.TryGetValue(pluginId, out var processes))
                {
                    return processes.ToList();
                }
                return new List<VirtualProcessEntry>();
            }
        }

        public Dictionary<string, List<VirtualProcessEntry>> GetAllVirtualProcesses()
        {
            lock (_lock)
            {
                return _virtualProcesses.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.ToList()
                );
            }
        }

        public VirtualizationStatistics GetStatistics(string pluginId)
        {
            lock (_lock)
            {
                if (_statistics.TryGetValue(pluginId, out var stats))
                {
                    return stats;
                }
                return new VirtualizationStatistics { PluginId = pluginId };
            }
        }

        public Dictionary<string, VirtualizationStatistics> GetAllStatistics()
        {
            lock (_lock)
            {
                var result = new Dictionary<string, VirtualizationStatistics>();
                foreach (var kvp in _statistics)
                {
                    var stats = kvp.Value;
                    if (stats.RegistryReads > 0 || stats.RegistryWrites > 0 || 
                        stats.FileReads > 0 || stats.FileWrites > 0 ||
                        stats.ProcessAccessAttempts > 0 || stats.LastActivity != DateTime.MinValue)
                    {
                        result[kvp.Key] = stats;
                    }
                }
                return result;
            }
        }

        public void RecordRegistryRead(string pluginId)
        {
            UpdateStatistics(pluginId, s => s.RegistryReads++);
        }

        public void RecordRegistryWrite(string pluginId, bool isVirtual)
        {
            UpdateStatistics(pluginId, s =>
            {
                s.RegistryWrites++;
                if (isVirtual) s.RegistryVirtualWrites++;
            });
        }

        public void RecordFileRead(string pluginId)
        {
            UpdateStatistics(pluginId, s => s.FileReads++);
        }

        public void RecordFileWrite(string pluginId, bool isVirtual, bool isBlocked)
        {
            UpdateStatistics(pluginId, s =>
            {
                s.FileWrites++;
                if (isVirtual) s.FileVirtualWrites++;
                if (isBlocked) s.FileBlockedWrites++;
            });
        }

        public void ClearPluginData(string pluginId)
        {
            lock (_lock)
            {
                _statistics.Remove(pluginId);
                _virtualProcesses.Remove(pluginId);
                _activityRecords.Remove(pluginId);
            }
        }

        private void UpdateStatistics(string pluginId, Action<VirtualizationStatistics> update)
        {
            if (!_statistics.TryGetValue(pluginId, out var stats))
            {
                stats = new VirtualizationStatistics { PluginId = pluginId };
                _statistics[pluginId] = stats;
            }
            update(stats);
            stats.LastActivity = DateTime.Now;
        }
    }
}
