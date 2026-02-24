using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Win32;
using AI_Chat.Services;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class RegistryHooks
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

        [HarmonyPatch(typeof(RegistryKey), "OpenSubKey", new Type[] { typeof(string), typeof(bool) })]
        public class RegistryKey_OpenSubKey_Patch
        {
            public static void Postfix(RegistryKey __instance, string name, bool writable, RegistryKey __result)
            {
                if (_virtualizationManager == null || __result == null) return;
                if (!IsPluginCall()) return;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    _virtualizationManager.Process.RecordRegistryRead(pluginId);
                }
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "CreateSubKey", new Type[] { typeof(string), typeof(RegistryKeyPermissionCheck) })]
        public class RegistryKey_CreateSubKey_Patch
        {
            public static void Postfix(RegistryKey __instance, string subkey, RegistryKeyPermissionCheck permissionCheck, RegistryKey __result)
            {
                if (_virtualizationManager == null || __result == null) return;
                if (!IsPluginCall()) return;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    _virtualizationManager.Process.RecordRegistryWrite(pluginId, true);
                }
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "GetValue", new Type[] { typeof(string), typeof(object), typeof(RegistryValueOptions) })]
        public class RegistryKey_GetValue_Patch
        {
            public static bool Prefix(RegistryKey __instance, string name, object defaultValue, RegistryValueOptions options, ref object __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string keyPath;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    keyPath = GetKeyPathSafe(__instance, "");
                    var result = _virtualizationManager.ReadRegistryValue(pluginId, keyPath, name);
                    
                    if (result.WasDeleted)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Read", "Registry", $"{keyPath}\\{name}", "GetValue", true, false, "Value deleted");
                        __result = defaultValue;
                        return false;
                    }
                    
                    if (result.IsVirtualized && result.ExistsInVirtual)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Read", "Registry", $"{keyPath}\\{name}", "GetValue", true, false, "Success");
                        __result = result.Value;
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "Registry", $"{keyPath}\\{name}", "GetValue", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "SetValue", new Type[] { typeof(string), typeof(object) })]
        public class RegistryKey_SetValue_NoKind_Patch
        {
            public static bool Prefix(RegistryKey __instance, string name, object value)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string keyPath;
                bool isVirtualized = false;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    keyPath = GetKeyPathSafe(__instance, "");
                    string valueKind = InferValueKind(value);
                    var result = _virtualizationManager.WriteRegistryValue(pluginId, keyPath, name, value, valueKind);
                    isVirtualized = result.IsVirtualized;
                    _virtualizationManager.RecordActivity(pluginId, "Write", "Registry", $"{keyPath}\\{name}", "SetValue", isVirtualized, false, $"Type: {valueKind}");
                }

                return !isVirtualized;
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "SetValue", new Type[] { typeof(string), typeof(object), typeof(RegistryValueKind) })]
        public class RegistryKey_SetValue_Patch
        {
            public static bool Prefix(RegistryKey __instance, string name, object value, RegistryValueKind valueKind)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string keyPath;
                bool isVirtualized = false;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    keyPath = GetKeyPathSafe(__instance, "");
                    var result = _virtualizationManager.WriteRegistryValue(pluginId, keyPath, name, value, valueKind.ToString());
                    isVirtualized = result.IsVirtualized;
                    _virtualizationManager.RecordActivity(pluginId, "Write", "Registry", $"{keyPath}\\{name}", "SetValue", isVirtualized, false, $"Type: {valueKind}");
                }

                return !isVirtualized;
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "DeleteValue", new Type[] { typeof(string), typeof(bool) })]
        public class RegistryKey_DeleteValue_Patch
        {
            public static bool Prefix(RegistryKey __instance, string name, bool throwOnMissingValue)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    string keyPath = GetKeyPathSafe(__instance, "");
                    var result = _virtualizationManager.DeleteRegistryValue(pluginId, keyPath, name);
                    
                    if (!result.Allowed)
                    {
                        throw new InvalidOperationException(result.ErrorMessage);
                    }
                }
                
                return false;
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "DeleteSubKey", new Type[] { typeof(string), typeof(bool) })]
        public class RegistryKey_DeleteSubKey_Patch
        {
            public static bool Prefix(RegistryKey __instance, string subkey, bool throwOnMissingSubKey)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    string keyPath = GetKeyPathSafe(__instance, subkey);
                    var result = _virtualizationManager.DeleteRegistryKey(pluginId, keyPath);
                    
                    if (!result.Allowed)
                    {
                        throw new InvalidOperationException(result.ErrorMessage);
                    }
                }
                
                return false;
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "GetSubKeyNames")]
        public class RegistryKey_GetSubKeyNames_Patch
        {
            public static bool Prefix(RegistryKey __instance, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    string keyPath = GetKeyPathSafe(__instance, "");
                    var store = _virtualizationManager.Registry.GetStore(pluginId);
                    
                    // 检查键是否被删除
                    if (store.IsKeyDeleted(keyPath))
                    {
                        __result = new string[0];
                        return false;
                    }
                    
                    // 获取虚拟子键（ValueName为空的条目表示键）
                    var entries = store.GetSubKeys(keyPath);
                    var virtualSubKeys = entries
                        .Where(e => string.IsNullOrEmpty(e.ValueName) && !e.IsDeleted && !e.IsKeyDeleted)
                        .Select(e => e.KeyPath.Substring(keyPath.Length).TrimStart('\\'))
                        .Where(k => !string.IsNullOrEmpty(k) && !k.Contains("\\"))
                        .Distinct()
                        .ToArray();
                    
                    if (virtualSubKeys.Length > 0)
                    {
                        __result = virtualSubKeys;
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(RegistryKey), "GetValueNames")]
        public class RegistryKey_GetValueNames_Patch
        {
            public static bool Prefix(RegistryKey __instance, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    string keyPath = GetKeyPathSafe(__instance, "");
                    var store = _virtualizationManager.Registry.GetStore(pluginId);
                    
                    // 检查键是否被删除
                    if (store.IsKeyDeleted(keyPath))
                    {
                        __result = new string[0];
                        return false;
                    }
                    
                    // 获取虚拟值名称（ValueName不为空的条目表示值）
                    var entries = store.GetSubKeys(keyPath);
                    var virtualValueNames = entries
                        .Where(e => !string.IsNullOrEmpty(e.ValueName) && !e.IsDeleted && !e.IsKeyDeleted)
                        .Select(e => e.ValueName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Distinct()
                        .ToArray();
                    
                    if (virtualValueNames.Length > 0)
                    {
                        __result = virtualValueNames;
                        return false;
                    }
                }

                return true;
            }
        }

        private static string GetKeyPathSafe(RegistryKey key, string subKey)
        {
            try
            {
                string basePath = key?.Name ?? "";
                if (string.IsNullOrEmpty(subKey))
                    return basePath;
                return $"{basePath}\\{subKey}";
            }
            catch
            {
                return subKey ?? "";
            }
        }

        private static string InferValueKind(object value)
        {
            if (value == null) return "String";
            if (value is int) return "DWord";
            if (value is long) return "QWord";
            if (value is byte[]) return "Binary";
            if (value is string[]) return "MultiString";
            return "String";
        }
    }
}
