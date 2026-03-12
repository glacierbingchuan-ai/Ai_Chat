using System;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Services;
using AI_Chat.Plugins;
using AI_Chat.Managers;
using AI_Chat.Utils;

namespace AI_Chat
{
    internal class Program
    {
        private static CancellationTokenSource _globalCts = new CancellationTokenSource();
        private static ConfigManager _configManager;
        private static UserConfigManager _userConfigManager;
        private static RequestRateLimiter _requestRateLimiter;
        private static LLMService _llmService;
        private static WebSocketClient _webSocketClient;
        private static MessageHandler _messageHandler;
        private static ControlPanelServer _controlPanelServer;
        private static PluginManager _pluginManager;
        private static PluginApi _pluginApi;
        private static VersionCheckService _versionCheckService;
        private static System.Threading.Timer _activeChatTimer;
        private static System.Threading.Timer _eventCheckTimer;
        private static UserSessionManager _sessionManager;

        static async Task Main(string[] args)
        {
            Console.Clear();

            if (!PlatformHelper.IsRunningAsAdmin())
            {
                PlatformHelper.ShowAdminWarning();
            }

            InitializeServices();

            Logger.LogInfo("SYSTEM", "==================== APPLICATION STARTUP ====================");
            Logger.LogInfo("SYSTEM", $"Platform: {(PlatformHelper.IsWindows ? "Windows" : PlatformHelper.IsLinux ? "Linux" : "Other")}");
            Logger.LogInfo("SYSTEM", $"Allowed users: {string.Join(", ", _configManager.Config.AllowedUserIds)}");

            _activeChatTimer = new System.Threading.Timer(_messageHandler.CheckActiveChat, null, 60000, 60000);
            _eventCheckTimer = new System.Threading.Timer(_messageHandler.CheckScheduledEvents, null, 10000, 10000);

            // 启动控制面板服务器
            _ = Task.Run(async () => await _controlPanelServer.StartAsync());
            
            // 执行版本检查
            _ = Task.Run(async () => await PerformVersionCheckAsync());
            
            // 启动机器人（使用 await 而非 .Wait() 避免阻塞）
            await StartBotAsync();
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

            _requestRateLimiter = new RequestRateLimiter(_configManager);

            _llmService = new LLMService(_configManager, _requestRateLimiter);

            _sessionManager = new UserSessionManager(_configManager, _llmService, _userConfigManager, _requestRateLimiter);

            _webSocketClient = new WebSocketClient(_configManager, _globalCts);

            _messageHandler = new MessageHandler(
                _configManager,
                _userConfigManager,
                _llmService,
                _webSocketClient,
                _globalCts,
                _sessionManager
            );

            _pluginManager = new PluginManager(_configManager);
            _pluginManager.Initialize();
            
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
                _requestRateLimiter
            );

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
    }
}
