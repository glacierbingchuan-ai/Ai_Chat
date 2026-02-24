using System;
using System.Reflection;
using HarmonyLib;
using AI_Chat.Services;

namespace AI_Chat.Plugins.Virtualization.Hooks
{
    public class VirtualizationHookManager
    {
        private static VirtualizationHookManager _instance;
        private static readonly object _lock = new object();

        private Harmony _harmony;
        private PluginVirtualizationManager _virtualizationManager;
        private bool _isInitialized;
        private bool _hooksApplied;

        public static VirtualizationHookManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new VirtualizationHookManager();
                        }
                    }
                }
                return _instance;
            }
        }

        private VirtualizationHookManager()
        {
            _harmony = new Harmony("com.aichat.virtualization");
        }

        public void Initialize(PluginVirtualizationManager virtualizationManager)
        {
            if (_isInitialized) return;

            _virtualizationManager = virtualizationManager;

            RegistryHooks.Initialize(_virtualizationManager);
            FileHooks.Initialize(_virtualizationManager);
            DirectoryHooks.Initialize(_virtualizationManager);
            FileStreamHooks.Initialize(_virtualizationManager);
            ProcessHooks.Initialize(_virtualizationManager);
            FileInfoHooks.Initialize(_virtualizationManager);
            StreamReaderHooks.Initialize(_virtualizationManager);
            StreamWriterHooks.Initialize(_virtualizationManager);
            BinaryReaderHooks.Initialize(_virtualizationManager);
            BinaryWriterHooks.Initialize(_virtualizationManager);

            _isInitialized = true;
            Logger.LogInfo("Virtualization", "Hook manager initialized");
        }

        public void ApplyHooks()
        {
            if (_hooksApplied) return;
            if (!_isInitialized)
            {
                Logger.LogWarning("Virtualization", "Cannot apply hooks: manager not initialized");
                return;
            }

            try
            {
                ApplyRegistryHooks();
                ApplyFileHooks();
                ApplyProcessHooks();
                ApplyFileInfoHooks();
                ApplyStreamReaderHooks();
                ApplyStreamWriterHooks();
                ApplyBinaryReaderHooks();
                ApplyBinaryWriterHooks();

                _hooksApplied = true;
                Logger.LogInfo("Virtualization", "All virtualization hooks applied successfully");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply hooks: {ex.Message}", ex);
                throw;
            }
        }

        private void ApplyRegistryHooks()
        {
            try
            {
                var registryHookTypes = new Type[]
                {
                    typeof(RegistryHooks.RegistryKey_OpenSubKey_Patch),
                    typeof(RegistryHooks.RegistryKey_CreateSubKey_Patch),
                    typeof(RegistryHooks.RegistryKey_GetValue_Patch),
                    typeof(RegistryHooks.RegistryKey_SetValue_NoKind_Patch),
                    typeof(RegistryHooks.RegistryKey_SetValue_Patch),
                    typeof(RegistryHooks.RegistryKey_DeleteValue_Patch),
                    typeof(RegistryHooks.RegistryKey_DeleteSubKey_Patch),
                    typeof(RegistryHooks.RegistryKey_GetSubKeyNames_Patch),
                    typeof(RegistryHooks.RegistryKey_GetValueNames_Patch)
                };

                foreach (var patchType in registryHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply registry hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "Registry hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply registry hooks: {ex.Message}", ex);
            }
        }

        private void ApplyFileHooks()
        {
            try
            {
                var fileHookTypes = new Type[]
                {
                    typeof(FileHooks.File_ReadAllBytes_Patch),
                    typeof(FileHooks.File_ReadAllText_Patch),
                    typeof(FileHooks.File_ReadAllText_Encoding_Patch),
                    typeof(FileHooks.File_ReadAllLines_Patch),
                    typeof(FileHooks.File_ReadAllLines_Encoding_Patch),
                    typeof(FileHooks.File_WriteAllBytes_Patch),
                    typeof(FileHooks.File_WriteAllText_Patch),
                    typeof(FileHooks.File_WriteAllText_Encoding_Patch),
                    typeof(FileHooks.File_AppendAllText_Patch),
                    typeof(FileHooks.File_AppendAllText_Encoding_Patch),
                    typeof(FileHooks.File_AppendAllLines_Patch),
                    typeof(FileHooks.File_AppendAllLines_Encoding_Patch),
                    typeof(FileHooks.File_Delete_Patch),
                    typeof(FileHooks.File_Open_Patch),
                    typeof(FileHooks.File_OpenRead_Patch),
                    typeof(FileHooks.File_OpenWrite_Patch),
                    typeof(FileHooks.File_Copy_Patch),
                    typeof(FileHooks.File_Move_Patch),
                    typeof(FileHooks.FileStream_BeginWrite_Patch),
                    typeof(FileHooks.File_Exists_Patch),
                    typeof(FileStreamHooks.FileStream_ctor_String_FileMode_Patch),
                    typeof(FileStreamHooks.FileStream_ctor_String_FileMode_FileAccess_Patch),
                    typeof(FileStreamHooks.FileStream_ctor_String_FileMode_FileAccess_FileShare_Patch),
                    typeof(DirectoryHooks.Directory_CreateDirectory_Patch),
                    typeof(DirectoryHooks.Directory_Delete_Patch),
                    typeof(DirectoryHooks.Directory_Delete_Recursive_Patch),
                    typeof(DirectoryHooks.Directory_Exists_Patch),
                    typeof(DirectoryHooks.Directory_GetFiles_Patch),
                    typeof(DirectoryHooks.Directory_GetFiles_Pattern_Patch),
                    typeof(DirectoryHooks.Directory_GetDirectories_Patch),
                    typeof(DirectoryHooks.Directory_GetFileSystemEntries_Patch),
                    typeof(DirectoryHooks.Directory_GetFileSystemEntries_Pattern_Patch),
                    typeof(DirectoryHooks.Directory_EnumerateFiles_Patch),
                    typeof(DirectoryHooks.Directory_EnumerateFiles_Pattern_Patch),
                    typeof(DirectoryHooks.Directory_EnumerateDirectories_Patch),
                    typeof(DirectoryHooks.Directory_EnumerateDirectories_Pattern_Patch),
                    typeof(DirectoryHooks.Directory_EnumerateFileSystemEntries_Patch),
                    typeof(DirectoryHooks.Directory_EnumerateFileSystemEntries_Pattern_Patch)
                };

                foreach (var patchType in fileHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply file hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "File hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply file hooks: {ex.Message}", ex);
            }
        }

        private void ApplyProcessHooks()
        {
            try
            {
                var processHookTypes = new Type[]
                {
                    typeof(ProcessHooks.Process_Start_Patch),
                    typeof(ProcessHooks.Process_Start_String_Patch),
                    typeof(ProcessHooks.Process_Start_TwoStrings_Patch),
                    typeof(ProcessHooks.Process_Kill_Patch),
                    typeof(ProcessHooks.Process_GetProcessById_Patch),
                    typeof(ProcessHooks.Process_GetProcesses_Patch),
                    typeof(ProcessHooks.Process_GetProcessesByName_Patch)
                };

                foreach (var patchType in processHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply process hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "Process hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply process hooks: {ex.Message}", ex);
            }
        }

        private void ApplyFileInfoHooks()
        {
            try
            {
                var fileInfoHookTypes = new Type[]
                {
                    typeof(FileInfoHooks.FileInfo_ctor_Patch),
                    typeof(FileInfoHooks.FileInfo_Exists_Getter_Patch),
                    typeof(FileInfoHooks.FileInfo_Length_Getter_Patch),
                    typeof(FileInfoHooks.FileInfo_Delete_Patch),
                    typeof(FileInfoHooks.FileInfo_MoveTo_Patch),
                    typeof(FileInfoHooks.FileInfo_CopyTo_Patch),
                    typeof(FileInfoHooks.FileInfo_CopyTo_Overwrite_Patch),
                    typeof(FileInfoHooks.FileInfo_Open_Patch),
                    typeof(FileInfoHooks.FileInfo_OpenRead_Patch),
                    typeof(FileInfoHooks.FileInfo_OpenWrite_Patch),
                    typeof(FileInfoHooks.FileInfo_CreateText_Patch),
                    typeof(FileInfoHooks.FileInfo_AppendText_Patch),
                    typeof(FileInfoHooks.FileInfo_OpenText_Patch)
                };

                foreach (var patchType in fileInfoHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply FileInfo hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "FileInfo hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply FileInfo hooks: {ex.Message}", ex);
            }
        }

        private void ApplyStreamReaderHooks()
        {
            try
            {
                var streamReaderHookTypes = new Type[]
                {
                    typeof(StreamReaderHooks.StreamReader_ctor_String_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_String_Bool_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_String_Encoding_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_String_Encoding_Bool_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_String_Encoding_Bool_Int_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_Stream_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_Stream_Bool_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_Stream_Encoding_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_Stream_Encoding_Bool_Patch),
                    typeof(StreamReaderHooks.StreamReader_ctor_Stream_Encoding_Bool_Int_Patch)
                };

                foreach (var patchType in streamReaderHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply StreamReader hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "StreamReader hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply StreamReader hooks: {ex.Message}", ex);
            }
        }

        private void ApplyStreamWriterHooks()
        {
            try
            {
                var streamWriterHookTypes = new Type[]
                {
                    typeof(StreamWriterHooks.StreamWriter_ctor_String_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_String_Bool_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_String_Bool_Encoding_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_String_Bool_Encoding_Int_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_Stream_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_Stream_Encoding_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_Stream_Encoding_Int_Patch),
                    typeof(StreamWriterHooks.StreamWriter_ctor_Stream_Encoding_Int_Bool_Patch)
                };

                foreach (var patchType in streamWriterHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply StreamWriter hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "StreamWriter hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply StreamWriter hooks: {ex.Message}", ex);
            }
        }

        private void ApplyBinaryReaderHooks()
        {
            try
            {
                var binaryReaderHookTypes = new Type[]
                {
                    typeof(BinaryReaderHooks.BinaryReader_ctor_Stream_Patch),
                    typeof(BinaryReaderHooks.BinaryReader_ctor_Stream_Encoding_Patch),
                    typeof(BinaryReaderHooks.BinaryReader_ctor_Stream_Encoding_Bool_Patch)
                };

                foreach (var patchType in binaryReaderHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply BinaryReader hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "BinaryReader hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply BinaryReader hooks: {ex.Message}", ex);
            }
        }

        private void ApplyBinaryWriterHooks()
        {
            try
            {
                var binaryWriterHookTypes = new Type[]
                {
                    typeof(BinaryWriterHooks.BinaryWriter_ctor_Stream_Patch),
                    typeof(BinaryWriterHooks.BinaryWriter_ctor_Stream_Encoding_Patch),
                    typeof(BinaryWriterHooks.BinaryWriter_ctor_Stream_Encoding_Bool_Patch)
                };

                foreach (var patchType in binaryWriterHookTypes)
                {
                    try
                    {
                        _harmony.CreateClassProcessor(patchType).Patch();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning("Virtualization", $"Failed to apply BinaryWriter hook {patchType.Name}: {ex.Message}");
                    }
                }

                Logger.LogInfo("Virtualization", "BinaryWriter hooks applied");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to apply BinaryWriter hooks: {ex.Message}", ex);
            }
        }

        public void RemoveHooks()
        {
            if (!_hooksApplied) return;

            try
            {
                _harmony.UnpatchAll("com.aichat.virtualization");
                _hooksApplied = false;
                Logger.LogInfo("Virtualization", "All virtualization hooks removed");
            }
            catch (Exception ex)
            {
                Logger.LogError("Virtualization", $"Failed to remove hooks: {ex.Message}", ex);
            }
        }

        public bool IsInitialized => _isInitialized;
        public bool HooksApplied => _hooksApplied;
    }
}
