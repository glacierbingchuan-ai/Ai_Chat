using System;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using AI_Chat.Services;
using AI_Chat.Plugins;

namespace AI_Chat
{
    internal class Program
    {
        private static CancellationTokenSource _globalCts = new CancellationTokenSource();
        private static ConfigManager _configManager;
        private static LLMService _llmService;
        private static ContextManager _contextManager;
        private static ChatHistoryManager _chatHistoryManager;
        private static WebSocketClient _webSocketClient;
        private static MessageHandler _messageHandler;
        private static ControlPanelServer _controlPanelServer;
        private static PluginManager _pluginManager;
        private static PluginApi _pluginApi;
        private static System.Threading.Timer _activeChatTimer;
        private static System.Threading.Timer _eventCheckTimer;

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
            Logger.LogInfo("SYSTEM", "Mode: Message Fusion | Interruption Cleanup | Persistent Dual-Logging | Self-Correction");

            _activeChatTimer = new System.Threading.Timer(_messageHandler.CheckActiveChat, null, 60000, 60000);
            _eventCheckTimer = new System.Threading.Timer(_messageHandler.CheckScheduledEvents, null, 10000, 10000);

            Task.Run(() => _controlPanelServer.StartAsync());
            Task.Run(() => StartBotAsync()).Wait();
        }

        private static void InitializeServices()
        {
            _configManager = new ConfigManager();
            _configManager.LoadConfig();

            _llmService = new LLMService(_configManager);

            _contextManager = new ContextManager(_configManager, _llmService);
            _contextManager.LoadContextFromDisk();
            _contextManager.LoadEventsFromDisk();

            _chatHistoryManager = new ChatHistoryManager();
            _chatHistoryManager.LoadChatHistoryFromDisk();

            _webSocketClient = new WebSocketClient(_configManager, _globalCts);

            _messageHandler = new MessageHandler(
                _configManager,
                _contextManager,
                _llmService,
                _webSocketClient,
                _chatHistoryManager,
                _globalCts
            );

            // 先创建 PluginManager（不需要 PluginApi）
            _pluginManager = new PluginManager(_configManager, null);
            _pluginManager.Initialize();
            
            // 创建 PluginApi 并注册到服务容器（在加载插件之前）
            _pluginApi = new PluginApi(_configManager, _contextManager, _llmService, _webSocketClient, _chatHistoryManager, _pluginManager);
            _pluginManager.SetPluginApi(_pluginApi);
            
            // 加载并启动插件（现在 IPluginApi 已经可用）
            _pluginManager.LoadAllPlugins();
            _pluginManager.StartAllPlugins();

            // 设置 MessageHandler 的 PluginManager 和 PluginApi
            _messageHandler.SetPluginManager(_pluginManager);
            _messageHandler.SetPluginApi(_pluginApi);

            _controlPanelServer = new ControlPanelServer(
                _configManager,
                _contextManager,
                _llmService,
                _chatHistoryManager,
                _messageHandler,
                _globalCts,
                _pluginManager
            );

            _controlPanelServer.InitializeBroadcastCallbacks();
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
