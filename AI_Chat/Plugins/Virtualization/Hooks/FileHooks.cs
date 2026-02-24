using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using AI_Chat.Services;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class FileHooks
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

        [HarmonyPatch(typeof(File), "ReadAllBytes", new Type[] { typeof(string) })]
        public class File_ReadAllBytes_Patch
        {
            public static bool Prefix(string path, ref byte[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllBytes", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized && File.Exists(accessResult.RealPath))
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllBytes", true, false, "Success");
                        __result = File.ReadAllBytes(accessResult.RealPath);
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllBytes", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "ReadAllText", new Type[] { typeof(string) })]
        public class File_ReadAllText_Patch
        {
            public static bool Prefix(string path, ref string __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllText", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized && File.Exists(accessResult.RealPath))
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllText", true, false, "Success");
                        __result = File.ReadAllText(accessResult.RealPath);
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllText", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "ReadAllText", new Type[] { typeof(string), typeof(System.Text.Encoding) })]
        public class File_ReadAllText_Encoding_Patch
        {
            public static bool Prefix(string path, System.Text.Encoding encoding, ref string __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized && File.Exists(accessResult.RealPath))
                    {
                        __result = File.ReadAllText(accessResult.RealPath, encoding);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "WriteAllBytes", new Type[] { typeof(string), typeof(byte[]) })]
        public class File_WriteAllBytes_Patch
        {
            public static bool Prefix(string path, byte[] bytes)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Write", "File", path, "WriteAllBytes", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        
                        File.WriteAllBytes(accessResult.RealPath, bytes);
                        _virtualizationManager.RecordActivity(pluginId, "Write", "File", path, "WriteAllBytes", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Write", "File", path, "WriteAllBytes", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "WriteAllText", new Type[] { typeof(string), typeof(string) })]
        public class File_WriteAllText_Patch
        {
            public static bool Prefix(string path, string contents)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Write", "File", path, "WriteAllText", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        File.WriteAllText(accessResult.RealPath, contents);
                        _virtualizationManager.RecordActivity(pluginId, "Write", "File", path, "WriteAllText", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Write", "File", path, "WriteAllText", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "WriteAllText", new Type[] { typeof(string), typeof(string), typeof(System.Text.Encoding) })]
        public class File_WriteAllText_Encoding_Patch
        {
            public static bool Prefix(string path, string contents, System.Text.Encoding encoding)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.WriteAllText(accessResult.RealPath, contents, encoding);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "Delete", new Type[] { typeof(string) })]
        public class File_Delete_Patch
        {
            public static bool Prefix(string path)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        _virtualizationManager.RecordActivity(pluginId, "Delete", "File", path, "Delete", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Delete", "File", path, "Delete", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "Open", new Type[] { typeof(string), typeof(FileMode) })]
        public class File_Open_Patch
        {
            public static bool Prefix(string path, FileMode mode, ref FileStream __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    if (mode == FileMode.Open || mode == FileMode.OpenOrCreate)
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
                                _virtualizationManager.RecordActivity(pluginId, "Open", "File", path, $"Open({mode})", true, false, "Success");
                                return false;
                            }
                            else if (mode == FileMode.OpenOrCreate)
                            {
                                string directory = Path.GetDirectoryName(accessResult.RealPath);
                                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                                {
                                    Directory.CreateDirectory(directory);
                                }
                                __result = File.Open(accessResult.RealPath, mode);
                                _virtualizationManager.RecordActivity(pluginId, "Open", "File", path, $"Open({mode})", true, false, "Success");
                                return false;
                            }
                            else
                            {
                                throw new FileNotFoundException($"File not found in virtual environment: {path}");
                            }
                        }
                    }
                    else if (mode == FileMode.Create || mode == FileMode.CreateNew || mode == FileMode.Truncate)
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
                            _virtualizationManager.RecordActivity(pluginId, "Open", "File", path, $"Open({mode})", true, false, "Success");
                            return false;
                        }
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Open", "File", path, $"Open({mode})", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "OpenRead", new Type[] { typeof(string) })]
        public class File_OpenRead_Patch
        {
            public static bool Prefix(string path, ref FileStream __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                            _virtualizationManager.RecordActivity(pluginId, "OpenRead", "File", path, "OpenRead", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "OpenRead", "File", path, "OpenRead", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "OpenWrite", new Type[] { typeof(string) })]
        public class File_OpenWrite_Patch
        {
            public static bool Prefix(string path, ref FileStream __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        _virtualizationManager.RecordActivity(pluginId, "OpenWrite", "File", path, "OpenWrite", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "OpenWrite", "File", path, "OpenWrite", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "ReadAllLines", new Type[] { typeof(string) })]
        public class File_ReadAllLines_Patch
        {
            public static bool Prefix(string path, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized && File.Exists(accessResult.RealPath))
                    {
                        __result = File.ReadAllLines(accessResult.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllLines", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllLines", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "ReadAllLines", new Type[] { typeof(string), typeof(System.Text.Encoding) })]
        public class File_ReadAllLines_Encoding_Patch
        {
            public static bool Prefix(string path, System.Text.Encoding encoding, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized && File.Exists(accessResult.RealPath))
                    {
                        __result = File.ReadAllLines(accessResult.RealPath, encoding);
                        _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllLines(Encoding)", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "File", path, "ReadAllLines(Encoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "AppendAllText", new Type[] { typeof(string), typeof(string) })]
        public class File_AppendAllText_Patch
        {
            public static bool Prefix(string path, string contents)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.AppendAllText(accessResult.RealPath, contents);
                        _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllText", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllText", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "AppendAllText", new Type[] { typeof(string), typeof(string), typeof(System.Text.Encoding) })]
        public class File_AppendAllText_Encoding_Patch
        {
            public static bool Prefix(string path, string contents, System.Text.Encoding encoding)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.AppendAllText(accessResult.RealPath, contents, encoding);
                        _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllText(Encoding)", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllText(Encoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "AppendAllLines", new Type[] { typeof(string), typeof(string[]) })]
        public class File_AppendAllLines_Patch
        {
            public static bool Prefix(string path, string[] contents)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.AppendAllLines(accessResult.RealPath, contents);
                        _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllLines", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllLines", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "AppendAllLines", new Type[] { typeof(string), typeof(string[]), typeof(System.Text.Encoding) })]
        public class File_AppendAllLines_Encoding_Patch
        {
            public static bool Prefix(string path, string[] contents, System.Text.Encoding encoding)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.AppendAllLines(accessResult.RealPath, contents, encoding);
                        _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllLines(Encoding)", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Append", "File", path, "AppendAllLines(Encoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "Copy", new Type[] { typeof(string), typeof(string), typeof(bool) })]
        public class File_Copy_Patch
        {
            public static bool Prefix(string sourceFileName, string destFileName, bool overwrite)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.Copy(sourceFileName, destAccess.RealPath, overwrite);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(File), "Move", new Type[] { typeof(string), typeof(string) })]
        public class File_Move_Patch
        {
            public static bool Prefix(string sourceFileName, string destFileName)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

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
                        File.Move(sourceFileName, destAccess.RealPath);
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileStream), "BeginWrite", new Type[] { typeof(byte[]), typeof(int), typeof(int), typeof(AsyncCallback), typeof(object) })]
        public class FileStream_BeginWrite_Patch
        {
            public static bool Prefix(FileStream __instance, byte[] array, int offset, int numBytes, AsyncCallback userCallback, object stateObject)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string path = GetFileStreamPath(__instance);
                if (string.IsNullOrEmpty(path)) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                    
                    if (!accessResult.Allowed)
                    {
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    return !accessResult.IsVirtualized;
                }
            }
        }

        [HarmonyPatch(typeof(File), "Exists", new Type[] { typeof(string) })]
        public class File_Exists_Patch
        {
            public static bool Prefix(string path, ref bool __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                    
                    if (accessResult.IsVirtualized)
                    {
                        if (!accessResult.Allowed)
                        {
                            __result = false;
                            return false;
                        }
                        __result = File.Exists(accessResult.RealPath);
                        return false;
                    }
                }

                return true;
            }
        }

        private static string GetFileStreamPath(FileStream stream)
        {
            try
            {
                return stream?.Name;
            }
            catch
            {
                return null;
            }
        }
    }

    public static class FileStreamHooks
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

        [HarmonyPatch(typeof(FileStream), MethodType.Constructor, new Type[] { typeof(string), typeof(FileMode) })]
        public class FileStream_ctor_String_FileMode_Patch
        {
            public static bool Prefix(ref FileStream __instance, string path, FileMode mode)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    if (mode == FileMode.Open)
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
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
                        }
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileStream), MethodType.Constructor, new Type[] { typeof(string), typeof(FileMode), typeof(FileAccess) })]
        public class FileStream_ctor_String_FileMode_FileAccess_Patch
        {
            public static bool Prefix(ref FileStream __instance, string path, FileMode mode, FileAccess access)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    if (access == FileAccess.Read)
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                    }
                    else if (access == FileAccess.Write || access == FileAccess.ReadWrite)
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
                        }
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(FileStream), MethodType.Constructor, new Type[] { typeof(string), typeof(FileMode), typeof(FileAccess), typeof(FileShare) })]
        public class FileStream_ctor_String_FileMode_FileAccess_FileShare_Patch
        {
            public static bool Prefix(ref FileStream __instance, string path, FileMode mode, FileAccess access, FileShare share)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    if (access == FileAccess.Read)
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                    }
                    else if (access == FileAccess.Write || access == FileAccess.ReadWrite)
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
                        }
                    }
                }

                return true;
            }
        }
    }

    public static class DirectoryHooks
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

        [HarmonyPatch(typeof(Directory), "CreateDirectory", new Type[] { typeof(string) })]
        public class Directory_CreateDirectory_Patch
        {
            public static bool Prefix(string path, ref DirectoryInfo __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckDirectoryCreate(pluginId, path);
                    
                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        __result = Directory.CreateDirectory(accessResult.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "Create", "Directory", path, "CreateDirectory", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Create", "Directory", path, "CreateDirectory", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "Delete", new Type[] { typeof(string) })]
        public class Directory_Delete_Patch
        {
            public static bool Prefix(string path)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckDirectoryDelete(pluginId, path);
                    
                    if (accessResult.IsVirtualized)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Delete", "Directory", path, "Delete", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Delete", "Directory", path, "Delete", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "Delete", new Type[] { typeof(string), typeof(bool) })]
        public class Directory_Delete_Recursive_Patch
        {
            public static bool Prefix(string path, bool recursive)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var accessResult = _virtualizationManager.CheckDirectoryDelete(pluginId, path);
                    
                    if (accessResult.IsVirtualized)
                    {
                        _virtualizationManager.RecordActivity(pluginId, "Delete", "Directory", path, "Delete (recursive)", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Delete", "Directory", path, "Delete (recursive)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "Exists", new Type[] { typeof(string) })]
        public class Directory_Exists_Patch
        {
            public static bool Prefix(string path, ref bool __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    
                    if (store.IsPathDeleted(path) || store.IsUnderDeletedDirectory(path))
                    {
                        __result = false;
                        return false;
                    }
                    
                    var entries = store.GetEntriesUnderPath(path);
                    
                    if (entries.Any(e => e.IsDirectory && !e.IsDeleted))
                    {
                        __result = true;
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "GetFiles", new Type[] { typeof(string) })]
        public class Directory_GetFiles_Patch
        {
            public static bool Prefix(string path, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualFiles = entries
                        .Where(e => !e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .ToArray();
                    
                    if (virtualFiles.Length > 0)
                    {
                        __result = virtualFiles;
                        return false;
                    }
                    
                    if (!Directory.Exists(path))
                    {
                        __result = new string[0];
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "GetFiles", new Type[] { typeof(string), typeof(string) })]
        public class Directory_GetFiles_Pattern_Patch
        {
            public static bool Prefix(string path, string searchPattern, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualFiles = entries
                        .Where(e => !e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Where(f => MatchesPattern(Path.GetFileName(f), searchPattern))
                        .ToArray();
                    
                    if (virtualFiles.Length > 0)
                    {
                        __result = virtualFiles;
                        return false;
                    }
                    
                    if (!Directory.Exists(path))
                    {
                        __result = new string[0];
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "GetDirectories", new Type[] { typeof(string) })]
        public class Directory_GetDirectories_Patch
        {
            public static bool Prefix(string path, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualDirs = entries
                        .Where(e => e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Distinct()
                        .ToArray();
                    
                    if (virtualDirs.Length > 0)
                    {
                        __result = virtualDirs;
                        return false;
                    }
                    
                    if (!Directory.Exists(path))
                    {
                        __result = new string[0];
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "GetFileSystemEntries", new Type[] { typeof(string) })]
        public class Directory_GetFileSystemEntries_Patch
        {
            public static bool Prefix(string path, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualEntries = entries
                        .Where(e => !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Distinct()
                        .ToArray();
                    
                    if (virtualEntries.Length > 0)
                    {
                        __result = virtualEntries;
                        return false;
                    }
                    
                    if (!Directory.Exists(path))
                    {
                        __result = new string[0];
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "GetFileSystemEntries", new Type[] { typeof(string), typeof(string) })]
        public class Directory_GetFileSystemEntries_Pattern_Patch
        {
            public static bool Prefix(string path, string searchPattern, ref string[] __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualEntries = entries
                        .Where(e => !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Where(f => MatchesPattern(Path.GetFileName(f), searchPattern))
                        .Distinct()
                        .ToArray();
                    
                    if (virtualEntries.Length > 0)
                    {
                        __result = virtualEntries;
                        return false;
                    }
                    
                    if (!Directory.Exists(path))
                    {
                        __result = new string[0];
                        return false;
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Directory), "EnumerateFiles", new Type[] { typeof(string) })]
        public class Directory_EnumerateFiles_Patch
        {
            public static bool Prefix(string path, ref IEnumerable<string> __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualFiles = entries
                        .Where(e => !e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath);
                    
                    __result = virtualFiles;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Directory), "EnumerateFiles", new Type[] { typeof(string), typeof(string) })]
        public class Directory_EnumerateFiles_Pattern_Patch
        {
            public static bool Prefix(string path, string searchPattern, ref IEnumerable<string> __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualFiles = entries
                        .Where(e => !e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Where(f => MatchesPattern(Path.GetFileName(f), searchPattern));
                    
                    __result = virtualFiles;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Directory), "EnumerateDirectories", new Type[] { typeof(string) })]
        public class Directory_EnumerateDirectories_Patch
        {
            public static bool Prefix(string path, ref IEnumerable<string> __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualDirs = entries
                        .Where(e => e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Distinct();
                    
                    __result = virtualDirs;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Directory), "EnumerateDirectories", new Type[] { typeof(string), typeof(string) })]
        public class Directory_EnumerateDirectories_Pattern_Patch
        {
            public static bool Prefix(string path, string searchPattern, ref IEnumerable<string> __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualDirs = entries
                        .Where(e => e.IsDirectory && !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Where(f => MatchesPattern(Path.GetFileName(f), searchPattern))
                        .Distinct();
                    
                    __result = virtualDirs;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Directory), "EnumerateFileSystemEntries", new Type[] { typeof(string) })]
        public class Directory_EnumerateFileSystemEntries_Patch
        {
            public static bool Prefix(string path, ref IEnumerable<string> __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualEntries = entries
                        .Where(e => !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Distinct();
                    
                    __result = virtualEntries;
                    return false;
                }
            }
        }

        [HarmonyPatch(typeof(Directory), "EnumerateFileSystemEntries", new Type[] { typeof(string), typeof(string) })]
        public class Directory_EnumerateFileSystemEntries_Pattern_Patch
        {
            public static bool Prefix(string path, string searchPattern, ref IEnumerable<string> __result)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                using (PluginExecutionContext.BeginPluginScope(null))
                {
                    var store = _virtualizationManager.FileSystem.GetStore(pluginId);
                    var entries = store.GetEntriesUnderPath(path);
                    
                    var virtualEntries = entries
                        .Where(e => !e.IsDeleted)
                        .Select(e => e.VirtualPath)
                        .Where(f => MatchesPattern(Path.GetFileName(f), searchPattern))
                        .Distinct();
                    
                    __result = virtualEntries;
                    return false;
                }
            }
        }

        private static bool MatchesPattern(string fileName, string pattern)
        {
            if (string.IsNullOrEmpty(pattern) || pattern == "*")
                return true;
            
            try
            {
                string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                    .Replace("\\*", ".*")
                    .Replace("\\?", ".") + "$";
                return System.Text.RegularExpressions.Regex.IsMatch(fileName, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                return fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }
    }
}
