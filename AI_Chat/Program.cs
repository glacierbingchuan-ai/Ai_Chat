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
            try
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

                // 启动时尝试一次连接协议端（不自动重连）
                _ = Task.Run(async () => await TryInitialProtocolConnectionAsync());

                // 启动机器人（使用 await 而非 .Wait() 避免阻塞）
                await StartBotAsync();
            }
            finally
            {
                // 确保 Serilog 在应用退出时刷新所有日志
                Logger.CloseAndFlush();
            }
        }

        private static async Task PerformVersionCheckAsync()
        {
            try
            {
                await _versionCheckService.PerformVersionCheckAndNotifyAsync();
            }
            catch (Exception ex)
            {
                Logger.LogError("VERSION_CHECK", $"ERROR: {ex.Message}");
            }
        }

        /// <summary>
        /// 启动时尝试一次连接协议端
        /// </summary>
        private static async Task TryInitialProtocolConnectionAsync()
        {
            try
            {
                Logger.LogInfo("SYSTEM", "Trying initial protocol connection...");
                var connected = await _webSocketClient.TryConnectAsync();

                if (connected)
                {
                    Logger.LogInfo("SYSTEM", "Initial protocol connection successful");
                }
                else
                {
                    Logger.LogWarning("SYSTEM", "Initial protocol connection failed. Please connect manually via control panel.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("SYSTEM", $"Error during initial protocol connection: {ex.Message}");
            }
        }

        private static void InitializeServices()
        {
            // 1. 首先初始化 Logger（不依赖 broadcastCallback，使用 null）
            Logger.Initialize(null);

            // 2. 然后加载配置（ConfigManager 会使用 Logger）
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
            _llmService.SetPluginManager(_pluginManager);

            // 设置LLMService的WebSocket发送函数和响应处理
            _llmService.SetWebSocketSendFunc(_webSocketClient.SendMessageAsync);
            _webSocketClient.SetLLMWebSocketHandler(json =>
            {
                // 处理LLM相关的响应
                _llmService.HandleWebSocketResponse(json);

                // 处理图片服务相关的响应（全局共享）
                try
                {
                    var response = Newtonsoft.Json.Linq.JObject.Parse(json);
                    ImageService.HandleResponse(response);
                }
                catch { }

                return Task.CompletedTask;
            });

            // 3. 创建 ControlPanelServer
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

            // 设置 WebSocketClient 到 ControlPanelServer，用于协议端连接管理
            _controlPanelServer.SetWebSocketClient(_webSocketClient);

            // 4. 初始化广播回调（这会重新设置 Logger 的 broadcastCallback）
            _controlPanelServer.InitializeBroadcastCallbacks();

            // Initialize version check service
            _versionCheckService = new VersionCheckService(_controlPanelServer);
            _controlPanelServer.SetVersionCheckService(_versionCheckService);

            // 5. 最后加载、初始化并启动插件
            _pluginManager.LoadAllPlugins();
            _pluginManager.InitializeAllPlugins();
            _pluginManager.StartAllPlugins();
            _webSocketClient.SetMessageHandler(_messageHandler.HandleMessageAsync);
            _messageHandler.InitializeBroadcastCallback(_controlPanelServer.BroadcastMessageToClients);
        }

        private static async Task StartBotAsync()
        {
            // 注意：现在 WebSocketClient.StartAsync() 不再自动重连
            // 而是在启动时尝试一次连接，失败则由前端控制手动连接
            // 这里我们保持运行，等待前端触发连接
            Logger.LogInfo("SYSTEM", "Bot is ready. Waiting for protocol connection...");

            // 使用一个 TaskCompletionSource 来保持程序运行
            var tcs = new TaskCompletionSource<object>();
            _globalCts.Token.Register(() => tcs.TrySetResult(null));
            await tcs.Task;
        }
    }
}
