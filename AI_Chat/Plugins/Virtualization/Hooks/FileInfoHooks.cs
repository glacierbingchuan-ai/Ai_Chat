using System;
using System.IO;
using HarmonyLib;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class FileInfoHooks
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

        [HarmonyPatch(typeof(FileInfo), MethodType.Constructor, new Type[] { typeof(string) })]
        public class FileInfo_ctor_Patch
        {
            public static void Postfix(FileInfo __instance, string fileName)
            {
                if (_virtualizationManager == null) return;
                if (!IsPluginCall()) return;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, fileName);
                    
                    if (!accessResult.Allowed)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Info", "FileInfo", fileName, "Constructor", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                    }
                    else
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Info", "FileInfo", fileName, "Constructor", accessResult.IsVirtualized, false, "Success");
                    }
                }
            }
        }

        [HarmonyPatch(typeof(FileInfo), "Exists", MethodType.Getter)]
        public class FileInfo_Exists_Getter_Patch
        {
            public static bool Prefix(FileInfo __instance, ref bool __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        __result = false;
                        return false;
                    }

                    if (accessResult.IsVirtualized)
                    {
                        __result = File.Exists(accessResult.RealPath);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "Length", MethodType.Getter)]
        public class FileInfo_Length_Getter_Patch
        {
            public static bool Prefix(FileInfo __instance, ref long __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized && File.Exists(accessResult.RealPath))
                    {
                        __result = new FileInfo(accessResult.RealPath).Length;
                        return false;
                    }
                }

                return true;
            }
        }



        [HarmonyPatch(typeof(FileInfo), "Delete")]
        public class FileInfo_Delete_Patch
        {
            public static bool Prefix(FileInfo __instance)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileDelete(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        if (File.Exists(accessResult.RealPath))
                        {
                            File.Delete(accessResult.RealPath);
                        }
                        _virtualizationManager.RecordActivity(pluginId, "Delete", "FileInfo", path, "Delete", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Delete", "FileInfo", path, "Delete", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "MoveTo", new Type[] { typeof(string) })]
        public class FileInfo_MoveTo_Patch
        {
            public static bool Prefix(FileInfo __instance, string destFileName)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string sourcePath = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var destAccess = _virtualizationManager.CheckFileWrite(pluginId, destFileName);
                    
                    if (!destAccess.Allowed)
                    {
                        throw new UnauthorizedAccessException(destAccess.ErrorMessage);
                    }

                    if (destAccess.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(destAccess.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.Move(sourcePath, destAccess.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "Move", "FileInfo", sourcePath, $"MoveTo({destFileName})", true, false, "Success");
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "CopyTo", new Type[] { typeof(string) })]
        public class FileInfo_CopyTo_Patch
        {
            public static bool Prefix(FileInfo __instance, string destFileName, ref FileInfo __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string sourcePath = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var destAccess = _virtualizationManager.CheckFileWrite(pluginId, destFileName);
                    
                    if (!destAccess.Allowed)
                    {
                        throw new UnauthorizedAccessException(destAccess.ErrorMessage);
                    }

                    if (destAccess.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(destAccess.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.Copy(sourcePath, destAccess.RealPath);
                        __result = new FileInfo(destAccess.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "Copy", "FileInfo", sourcePath, $"CopyTo({destFileName})", true, false, "Success");
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "CopyTo", new Type[] { typeof(string), typeof(bool) })]
        public class FileInfo_CopyTo_Overwrite_Patch
        {
            public static bool Prefix(FileInfo __instance, string destFileName, bool overwrite, ref FileInfo __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string sourcePath = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var destAccess = _virtualizationManager.CheckFileWrite(pluginId, destFileName);
                    
                    if (!destAccess.Allowed)
                    {
                        throw new UnauthorizedAccessException(destAccess.ErrorMessage);
                    }

                    if (destAccess.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(destAccess.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.Copy(sourcePath, destAccess.RealPath, overwrite);
                        __result = new FileInfo(destAccess.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "Copy", "FileInfo", sourcePath, $"CopyTo({destFileName}, {overwrite})", true, false, "Success");
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "Open", new Type[] { typeof(FileMode) })]
        public class FileInfo_Open_Patch
        {
            public static bool Prefix(FileInfo __instance, FileMode mode, ref FileStream __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    if (mode == FileMode.Open)
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }

                        if (accessResult.IsVirtualized)
                        {
                            if (File.Exists(accessResult.RealPath))
                            {
                                __result = File.Open(accessResult.RealPath, mode);
                                _virtualizationManager.RecordActivity(pluginId, "Open", "FileInfo", path, $"Open({mode})", true, false, "Success");
                                return false;
                            }
                            throw new FileNotFoundException($"File not found in virtual environment: {path}");
                        }
                    }
                    else
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }

                        if (accessResult.IsVirtualized)
                        {
                            string directory = Path.GetDirectoryName(accessResult.RealPath);
                            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                            {
                                Directory.CreateDirectory(directory);
                            }
                            __result = File.Open(accessResult.RealPath, mode);
                            _virtualizationManager.RecordActivity(pluginId, "Open", "FileInfo", path, $"Open({mode})", true, false, "Success");
                            return false;
                        }
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Open", "FileInfo", path, $"Open({mode})", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "OpenRead")]
        public class FileInfo_OpenRead_Patch
        {
            public static bool Prefix(FileInfo __instance, ref FileStream __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        if (File.Exists(accessResult.RealPath))
                        {
                            __result = File.OpenRead(accessResult.RealPath);
                            _virtualizationManager.RecordActivity(pluginId, "OpenRead", "FileInfo", path, "OpenRead", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "OpenRead", "FileInfo", path, "OpenRead", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "OpenWrite")]
        public class FileInfo_OpenWrite_Patch
        {
            public static bool Prefix(FileInfo __instance, ref FileStream __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        __result = File.OpenWrite(accessResult.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "OpenWrite", "FileInfo", path, "OpenWrite", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "OpenWrite", "FileInfo", path, "OpenWrite", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "CreateText")]
        public class FileInfo_CreateText_Patch
        {
            public static bool Prefix(FileInfo __instance, ref StreamWriter __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        __result = File.CreateText(accessResult.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "CreateText", "FileInfo", path, "CreateText", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "CreateText", "FileInfo", path, "CreateText", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "AppendText")]
        public class FileInfo_AppendText_Patch
        {
            public static bool Prefix(FileInfo __instance, ref StreamWriter __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        __result = File.AppendText(accessResult.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "AppendText", "FileInfo", path, "AppendText", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "AppendText", "FileInfo", path, "AppendText", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileInfo), "OpenText")]
        public class FileInfo_OpenText_Patch
        {
            public static bool Prefix(FileInfo __instance, ref StreamReader __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                string path = __instance.FullName;
                
                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        if (File.Exists(accessResult.RealPath))
                        {
                            __result = File.OpenText(accessResult.RealPath);
                            _virtualizationManager.RecordActivity(pluginId, "OpenText", "FileInfo", path, "OpenText", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "OpenText", "FileInfo", path, "OpenText", false, false, "Success");
                }

                return true;
            }
        }
    }
}
