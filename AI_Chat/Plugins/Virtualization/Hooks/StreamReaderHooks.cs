using System;
using System.IO;
using System.Text;
using HarmonyLib;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class StreamReaderHooks
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

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(string) })]
        public class StreamReader_ctor_String_Patch
        {
            public static bool Prefix(ref StreamReader __instance, string path)
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
                        _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                        throw new FileNotFoundException(accessResult.ErrorMessage);
                    }

                    if (accessResult.IsVirtualized)
                    {
                        if (File.Exists(accessResult.RealPath))
                        {
                            __instance = new StreamReader(accessResult.RealPath);
                            _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(string), typeof(bool) })]
        public class StreamReader_ctor_String_Bool_Patch
        {
            public static bool Prefix(ref StreamReader __instance, string path, bool detectEncodingFromByteOrderMarks)
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
                            __instance = new StreamReader(accessResult.RealPath, detectEncodingFromByteOrderMarks);
                            _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(detectEncoding)", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(detectEncoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(string), typeof(Encoding) })]
        public class StreamReader_ctor_String_Encoding_Patch
        {
            public static bool Prefix(ref StreamReader __instance, string path, Encoding encoding)
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
                            __instance = new StreamReader(accessResult.RealPath, encoding);
                            _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Encoding)", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Encoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(string), typeof(Encoding), typeof(bool) })]
        public class StreamReader_ctor_String_Encoding_Bool_Patch
        {
            public static bool Prefix(ref StreamReader __instance, string path, Encoding encoding, bool detectEncodingFromByteOrderMarks)
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
                            __instance = new StreamReader(accessResult.RealPath, encoding, detectEncodingFromByteOrderMarks);
                            _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Encoding, detectEncoding)", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Encoding, detectEncoding)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(string), typeof(Encoding), typeof(bool), typeof(int) })]
        public class StreamReader_ctor_String_Encoding_Bool_Int_Patch
        {
            public static bool Prefix(ref StreamReader __instance, string path, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
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
                            __instance = new StreamReader(accessResult.RealPath, encoding, detectEncodingFromByteOrderMarks, bufferSize);
                            _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Encoding, detectEncoding, bufferSize)", true, false, "Success");
                            return false;
                        }
                        throw new FileNotFoundException($"File not found in virtual environment: {path}");
                    }
                    
                    _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Encoding, detectEncoding, bufferSize)", false, false, "Success");
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(Stream) })]
        public class StreamReader_ctor_Stream_Patch
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
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Stream)", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Stream)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(Stream), typeof(bool) })]
        public class StreamReader_ctor_Stream_Bool_Patch
        {
            public static bool Prefix(Stream stream, bool detectEncodingFromByteOrderMarks)
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
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Stream, detectEncoding)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding) })]
        public class StreamReader_ctor_Stream_Encoding_Patch
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
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Stream, Encoding)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding), typeof(bool) })]
        public class StreamReader_ctor_Stream_Encoding_Bool_Patch
        {
            public static bool Prefix(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks)
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
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Stream, Encoding, detectEncoding)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(StreamReader), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding), typeof(bool), typeof(int) })]
        public class StreamReader_ctor_Stream_Encoding_Bool_Int_Patch
        {
            public static bool Prefix(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize)
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
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "StreamReader", path, "Constructor(Stream, Encoding, detectEncoding, bufferSize)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }
    }
}
