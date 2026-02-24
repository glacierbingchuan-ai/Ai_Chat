using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using AI_Chat.Services;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class ProcessHooks
    {
        private static PluginVirtualizationManager _virtualizationManager;

        public static void Initialize(PluginVirtualizationManager manager)
        {
            _virtualizationManager = manager;
        }

        private static string GetCurrentPluginId()
        {
            return PluginExecutionContext.CurrentPluginId;
        }

        private static bool IsPluginCall()
        {
            return PluginExecutionContext.IsInPluginContext;
        }

        private static bool IsVirtualizationEnabledForPlugin(string pluginId)
        {
            return _virtualizationManager != null && _virtualizationManager.IsVirtualizationEnabled(pluginId);
        }

        [HarmonyPatch(typeof(Process), "Start", new Type[] { typeof(ProcessStartInfo) })]
        public class Process_Start_Patch
        {
            public static bool Prefix(ProcessStartInfo startInfo, ref Process __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                var result = _virtualizationManager.CheckProcessStart(pluginId, startInfo.FileName, startInfo.Arguments);
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    Logger.LogInfo("Virtualization", $"Process.Start blocked: fileName={startInfo.FileName}, pluginId={pluginId}");
                }
                
                if (!result.Allowed)
                {
                    _virtualizationManager.RecordActivity(pluginId, "Start", "Process", startInfo.FileName, startInfo.Arguments, false, true, "Blocked: " + result.ErrorMessage);
                    throw new UnauthorizedAccessException(result.ErrorMessage);
                }

                _virtualizationManager.RecordActivity(pluginId, "Start", "Process", startInfo.FileName, startInfo.Arguments, true, false, "Blocked by sandbox");
                return false;
            }
        }

        [HarmonyPatch(typeof(Process), "Start", new Type[] { typeof(string) })]
        public class Process_Start_String_Patch
        {
            public static bool Prefix(string fileName, ref Process __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                var result = _virtualizationManager.CheckProcessStart(pluginId, fileName, "");
                
                if (!result.Allowed)
                {
                    _virtualizationManager.RecordActivity(pluginId, "Start", "Process", fileName, "", false, true, "Blocked: " + result.ErrorMessage);
                    throw new UnauthorizedAccessException(result.ErrorMessage);
                }

                _virtualizationManager.RecordActivity(pluginId, "Start", "Process", fileName, "", true, false, "Blocked by sandbox");
                return false;
            }
        }

        [HarmonyPatch(typeof(Process), "Start", new Type[] { typeof(string), typeof(string) })]
        public class Process_Start_TwoStrings_Patch
        {
            public static bool Prefix(string fileName, string arguments, ref Process __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                var result = _virtualizationManager.CheckProcessStart(pluginId, fileName, arguments);
                
                if (!result.Allowed)
                {
                    _virtualizationManager.RecordActivity(pluginId, "Start", "Process", fileName, arguments, false, true, "Blocked: " + result.ErrorMessage);
                    throw new UnauthorizedAccessException(result.ErrorMessage);
                }

                _virtualizationManager.RecordActivity(pluginId, "Start", "Process", fileName, arguments, true, false, "Blocked by sandbox");
                return false;
            }
        }

        [HarmonyPatch(typeof(Process), "Kill")]
        public class Process_Kill_Patch
        {
            public static bool Prefix(Process __instance)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                var result = _virtualizationManager.CheckProcessKill(pluginId, __instance.Id);
                
                if (!result.Allowed)
                {
                    _virtualizationManager.RecordActivity(pluginId, "Kill", "Process", $"PID: {__instance.Id}", __instance.ProcessName, false, true, "Blocked: " + result.ErrorMessage);
                    throw new UnauthorizedAccessException(result.ErrorMessage);
                }

                _virtualizationManager.RecordActivity(pluginId, "Kill", "Process", $"PID: {__instance.Id}", __instance.ProcessName, true, false, "Blocked by sandbox");
                return false;
            }
        }

        [HarmonyPatch(typeof(Process), "GetProcessById", new Type[] { typeof(int) })]
        public class Process_GetProcessById_Patch
        {
            public static void Postfix(int processId, Process __result)
            {
                if (_virtualizationManager == null || __result == null) return;
                if (!IsPluginCall()) return;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return;

                _virtualizationManager.Process.CheckProcessAccess(pluginId, processId, "GetProcessById");
                _virtualizationManager.RecordActivity(pluginId, "Access", "Process", $"PID: {processId}", "GetProcessById", false, false, "Success");
            }
        }

        [HarmonyPatch(typeof(Process), "GetProcesses", new Type[] { })]
        public class Process_GetProcesses_Patch
        {
            public static void Postfix(Process[] __result)
            {
                if (_virtualizationManager == null || __result == null) return;
                if (!IsPluginCall()) return;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return;

                _virtualizationManager.Process.CheckProcessAccess(pluginId, 0, "GetProcesses");
                _virtualizationManager.RecordActivity(pluginId, "Access", "Process", "All processes", "GetProcesses", false, false, $"Found {__result.Length} processes");
            }
        }

        [HarmonyPatch(typeof(Process), "GetProcessesByName", new Type[] { typeof(string) })]
        public class Process_GetProcessesByName_Patch
        {
            public static void Postfix(string processName, Process[] __result)
            {
                if (_virtualizationManager == null || __result == null) return;
                if (!IsPluginCall()) return;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return;

                _virtualizationManager.Process.CheckProcessAccess(pluginId, 0, "GetProcessesByName");
                _virtualizationManager.RecordActivity(pluginId, "Access", "Process", processName, "GetProcessesByName", false, false, $"Found {__result.Length} processes");
            }
        }
    }
}
