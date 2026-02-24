using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AI_Chat.Services;
using AI_Chat.Plugins;
using AI_Chat.Plugins.Virtualization;
using AI_Chat.Plugins.Virtualization.Hooks;
using AI_Chat.Managers;

namespace AI_Chat
{
    internal class Program
    {
        private static CancellationTokenSource _globalCts = new CancellationTokenSource();
        private static ConfigManager _configManager;
        private static UserConfigManager _userConfigManager;
        private static LLMService _llmService;
        private static WebSocketClient _webSocketClient;
        private static MessageHandler _messageHandler;
        private static ControlPanelServer _controlPanelServer;
        private static PluginManager _pluginManager;
        private static PluginApi _pluginApi;
        private static PluginVirtualizationManager _virtualizationManager;
        private static VersionCheckService _versionCheckService;
        private static System.Threading.Timer _activeChatTimer;
        private static System.Threading.Timer _eventCheckTimer;
        private static UserSessionManager _sessionManager;

        static void Main(string[] args)
        {
            Console.Clear();

            if (!IsRunningAsAdmin())
            {
                MessageBox.Show(
                    "Software running without administrator privileges; some functions may not work properly.",
                    "Warning",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            InitializeServices();


            Logger.LogInfo("SYSTEM", "==================== APPLICATION STARTUP ====================");
            Logger.LogInfo("SYSTEM", $"Allowed users: {string.Join(", ", _configManager.Config.AllowedUserIds)}");

            _activeChatTimer = new System.Threading.Timer(_messageHandler.CheckActiveChat, null, 60000, 60000);
            _eventCheckTimer = new System.Threading.Timer(_messageHandler.CheckScheduledEvents, null, 10000, 10000);

            Task.Run(() => _controlPanelServer.StartAsync());
            
            Task.Run(async () => await PerformVersionCheckAsync());
            
            Task.Run(() => StartBotAsync()).Wait();
        }
        
        private static async Task PerformVersionCheckAsync()
        {
            try
            {
                await Task.Delay(3000);
                await _versionCheckService.PerformVersionCheckAndNotifyAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("VERSION_CHECK", $"ERROR: {ex.Message}");
            }
        }

        private static void InitializeServices()
        {
            _configManager = new ConfigManager();
            _configManager.LoadConfig();

            _userConfigManager = new UserConfigManager();

            _llmService = new LLMService(_configManager);

            _sessionManager = new UserSessionManager(_configManager, _llmService, _userConfigManager);

            _webSocketClient = new WebSocketClient(_configManager, _globalCts);

            _messageHandler = new MessageHandler(
                _configManager,
                _userConfigManager,
                _llmService,
                _webSocketClient,
                _globalCts,
                _sessionManager
            );

            _pluginManager = new PluginManager(_configManager, null);
            _pluginManager.Initialize();
            
            string baseDataPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PluginData");
            string appBasePath = AppDomain.CurrentDomain.BaseDirectory;
            
            var virtualizationConfig = new VirtualizationConfig();
            virtualizationConfig.ExcludedPaths.Add(System.IO.Path.Combine(appBasePath, "PluginConfigs"));
            virtualizationConfig.ExcludedPaths.Add(System.IO.Path.Combine(appBasePath, "PluginData"));
            virtualizationConfig.ExcludedPaths.Add(System.IO.Path.Combine(appBasePath, "Plugins"));
            virtualizationConfig.ExcludedPaths.Add(System.IO.Path.Combine(appBasePath, "UserData"));
            virtualizationConfig.ExcludedPaths.Add(System.IO.Path.Combine(appBasePath, "BotLogs"));
            
            _virtualizationManager = new PluginVirtualizationManager(baseDataPath, virtualizationConfig);
            
            // 设置插件管理器到虚拟化管理器
            _virtualizationManager.SetPluginManager(_pluginManager);
            
            VirtualizationHookManager.Instance.Initialize(_virtualizationManager);
            VirtualizationHookManager.Instance.ApplyHooks();
            
            // 设置虚拟化管理器到插件管理器
            _pluginManager.SetVirtualizationManager(_virtualizationManager);
            
            _pluginApi = new PluginApi(_configManager, _sessionManager, _llmService, _webSocketClient, _pluginManager);
            _pluginManager.SetPluginApi(_pluginApi);

            _messageHandler.SetPluginManager(_pluginManager);
            _messageHandler.SetPluginApi(_pluginApi);

            _llmService.SetPluginApi(_pluginApi);

            // 先创建 ControlPanelServer，这样插件初始化时可以发送消息给前端
            _controlPanelServer = new ControlPanelServer(
                _configManager,
                _sessionManager,
                _userConfigManager,
                _llmService,
                _messageHandler,
                _globalCts,
                _pluginManager,
                _virtualizationManager
            );

            // 设置控制面板服务器到插件管理器
            _pluginManager.SetControlPanelServer(_controlPanelServer);

            _controlPanelServer.InitializeBroadcastCallbacks();
            
            // Initialize version check service
            _versionCheckService = new VersionCheckService(_controlPanelServer);
            _controlPanelServer.SetVersionCheckService(_versionCheckService);
            
            // 最后加载、初始化并启动插件（此时 ControlPanelServer 已准备好接收消息）
            _pluginManager.LoadAllPlugins();
            _pluginManager.InitializeAllPlugins();
            _pluginManager.StartAllPlugins();
            _webSocketClient.SetMessageHandler(_messageHandler.HandleMessageAsync);
            _messageHandler.InitializeBroadcastCallback(_controlPanelServer.BroadcastMessageToClients);
        }

        private static async Task StartBotAsync()
        {
            await _webSocketClient.StartAsync();
        }

        private static bool IsRunningAsAdmin()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

    }
}
