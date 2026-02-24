using System;
using System.IO;
using System.Text;
using HarmonyLib;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class StreamWriterHooks
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

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(string) })]
        public class StreamWriter_ctor_String_Patch
        {
            public static bool Prefix(ref StreamWriter __instance, string path)
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
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                        throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        string directory = Path.GetDirectoryName(accessResult.RealPath);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }
                        __instance = new StreamWriter(accessResult.RealPath);
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(string), typeof(bool) })]
        public class StreamWriter_ctor_String_Bool_Patch
        {
            public static bool Prefix(ref StreamWriter __instance, string path, bool append)
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
                        __instance = new StreamWriter(accessResult.RealPath, append);
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, $"Constructor(append={append})", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, $"Constructor(append={append})", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(string), typeof(bool), typeof(Encoding) })]
        public class StreamWriter_ctor_String_Bool_Encoding_Patch
        {
            public static bool Prefix(ref StreamWriter __instance, string path, bool append, Encoding encoding)
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
                        __instance = new StreamWriter(accessResult.RealPath, append, encoding);
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, $"Constructor(append={append}, Encoding)", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, $"Constructor(append={append}, Encoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(string), typeof(bool), typeof(Encoding), typeof(int) })]
        public class StreamWriter_ctor_String_Bool_Encoding_Int_Patch
        {
            public static bool Prefix(ref StreamWriter __instance, string path, bool append, Encoding encoding, int bufferSize)
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
                        __instance = new StreamWriter(accessResult.RealPath, append, encoding, bufferSize);
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, $"Constructor(append={append}, Encoding, bufferSize)", true, false, "Success");
                        return false;
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, $"Constructor(append={append}, Encoding, bufferSize)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(Stream) })]
        public class StreamWriter_ctor_Stream_Patch
        {
            public static bool Prefix(Stream stream)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (stream is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor(Stream)", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor(Stream)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding) })]
        public class StreamWriter_ctor_Stream_Encoding_Patch
        {
            public static bool Prefix(Stream stream, Encoding encoding)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (stream is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor(Stream, Encoding)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding), typeof(int) })]
        public class StreamWriter_ctor_Stream_Encoding_Int_Patch
        {
            public static bool Prefix(Stream stream, Encoding encoding, int bufferSize)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (stream is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor(Stream, Encoding, bufferSize)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamWriter), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding), typeof(int), typeof(bool) })]
        public class StreamWriter_ctor_Stream_Encoding_Int_Bool_Patch
        {
            public static bool Prefix(Stream stream, Encoding encoding, int bufferSize, bool leaveOpen)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (stream is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "StreamWriter", path, "Constructor(Stream, Encoding, bufferSize, leaveOpen)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }
    }
}
