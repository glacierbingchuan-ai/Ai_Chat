using System;
using System.Threading;

namespace AI_Chat.Plugins.Virtualization
{
    public static class PluginExecutionContext
    {
        private static readonly AsyncLocal<string> _currentPluginId = new AsyncLocal<string>();

        public static string CurrentPluginId
        {
            get => _currentPluginId.Value;
            set => _currentPluginId.Value = value;
        }

        public static IDisposable BeginPluginScope(string pluginId)
        {
            var previousId = _currentPluginId.Value;
            _currentPluginId.Value = pluginId;
            
            return new PluginScopeDisposable(previousId);
        }

        private class PluginScopeDisposable : IDisposable
        {
            private readonly string _previousId;
            private bool _disposed;

            public PluginScopeDisposable(string previousId)
            {
                _previousId = previousId;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _currentPluginId.Value = _previousId;
                    _disposed = true;
                }
            }
        }

        public static bool IsInPluginContext => !string.IsNullOrEmpty(CurrentPluginId);
    }
}
