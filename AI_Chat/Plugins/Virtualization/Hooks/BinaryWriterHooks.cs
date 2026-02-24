using System;
using System.IO;
using System.Text;
using HarmonyLib;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public static class BinaryWriterHooks
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

        [HarmonyPatch(typeof(BinaryWriter), MethodType.Constructor, new Type[] { typeof(Stream) })]
        public class BinaryWriter_ctor_Stream_Patch
        {
            public static bool Prefix(Stream output)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (output is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            _virtualizationManager.RecordActivity(pluginId, "Write", "BinaryWriter", path, "Constructor(Stream)", accessResult.IsVirtualized, true, "Blocked: " + accessResult.ErrorMessage);
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "BinaryWriter", path, "Constructor(Stream)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(BinaryWriter), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding) })]
        public class BinaryWriter_ctor_Stream_Encoding_Patch
        {
            public static bool Prefix(Stream output, Encoding encoding)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (output is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "BinaryWriter", path, "Constructor(Stream, Encoding)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(BinaryWriter), MethodType.Constructor, new Type[] { typeof(Stream), typeof(Encoding), typeof(bool) })]
        public class BinaryWriter_ctor_Stream_Encoding_Bool_Patch
        {
            public static bool Prefix(Stream output, Encoding encoding, bool leaveOpen)
            {
                if (_virtualizationManager == null) return true;
                if (!IsPluginCall()) return true;

                string pluginId = GetCurrentPluginId();
                if (!IsVirtualizationEnabledForPlugin(pluginId)) return true;

                if (output is FileStream fileStream)
                {
                    string path = fileStream.Name;
                    
                    using (PluginExecutionContext.BeginPluginScope(null))
                    {
                        var accessResult = _virtualizationManager.CheckFileWrite(pluginId, path);
                        
                        if (!accessResult.Allowed)
                        {
                            throw new UnauthorizedAccessException(accessResult.ErrorMessage);
                        }
                        
                        _virtualizationManager.RecordActivity(pluginId, "Write", "BinaryWriter", path, "Constructor(Stream, Encoding, leaveOpen)", accessResult.IsVirtualized, false, "Success");
                    }
                }

                return true;
            }
        }
    }
}
