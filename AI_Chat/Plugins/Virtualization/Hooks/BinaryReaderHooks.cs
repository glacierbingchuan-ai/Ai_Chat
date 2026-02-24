using System;
using System.IO;
using System.Text;
using HarmonyLib;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class BinaryReaderHooks
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

        [HarmonyPatch(typeof(BinaryReader), MethodType.Constructor, new Type[] { typeof(Stream) })]
        public class BinaryReader_ctor_Stream_Patch
        {
            public static bool Prefix(Stream input)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (input is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            _virtualizationManager.RecordActivity(pluginId, "Read", "BinaryReader", path, "Constructor(Stream)", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "BinaryReader", path, "Constructor(Stream)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(BinaryReader), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding) })]
        public class BinaryReader_ctor_Stream_Encoding_Patch
        {
            public static bool Prefix(Stream input, Encoding encoding)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (input is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "BinaryReader", path, "Constructor(Stream, Encoding)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(BinaryReader), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding), typeof(bool) })]
        public class BinaryReader_ctor_Stream_Encoding_Bool_Patch
        {
            public static bool Prefix(Stream input, Encoding encoding, bool leaveOpen)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (input is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileRead(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new FileNotFoundException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Read", "BinaryReader", path, "Constructor(Stream, Encoding, leaveOpen)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }
    }
}
