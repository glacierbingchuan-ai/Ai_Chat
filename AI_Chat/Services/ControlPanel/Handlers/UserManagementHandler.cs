using System;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using AI_Chat.Managers;
using AI_Chat.Models;

namespace AI_Chat.Services.ControlPanel.Handlers
{
    public class UserManagementHandler
    {
        private readonly ConfigManager _configManager;
        private readonly UserSessionManager _sessionManager;
        private readonly UserConfigManager _userConfigManager;

        public UserManagementHandler(
            ConfigManager configManager,
            UserSessionManager sessionManager,
            UserConfigManager userConfigManager)
        {
            _configManager = configManager;
            _sessionManager = sessionManager;
            _userConfigManager = userConfigManager;
        }

        public async Task HandleSelectUserAsync(WebSocket webSocket, dynamic data, string replyTo, WebSocketHandler handler)
        {
            try
            {
                long userId = (long)data.userId;
                if (_configManager.Config.AllowedUserIds.Contains(userId))
                {
                    await handler.SendResponseAsync(webSocket, "user_selecting", new { userId = userId }, replyTo);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            Logger.LogInfo("ControlPanel", $"Selected user: {userId}");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("ControlPanel", $"Error sending initial data for user {userId}: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error selecting user: {ex.Message}");
            }
        }

        public async Task SendUsersListAsync(WebSocket webSocket, string replyTo, WebSocketHandler handler)
        {
            try
            {
                var users = _configManager.Config.AllowedUserIds.Select(id => new
                {
                    userId = id,
                    stats = _sessionManager.GetSession(id)?.GetStats()
                }).ToList();

                var groups = _configManager.Config.AllowedGroupIds.Select(id => new
                {
                    groupId = id
                }).ToList();

                await handler.SendResponseAsync(webSocket, "users_list", new { users = users, groups = groups }, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error sending users list: {ex.Message}");
            }
        }

        public async Task HandleAddAllowedUserAsync(dynamic data, string replyTo, WebSocketHandler handler)
        {
            try
            {
                long userId = (long)data.userId;

                string userIdStr = userId.ToString();
                if (userIdStr.Length < 5)
                {
                    Logger.LogWarning("ControlPanel", $"Rejected user ID {userId}: must be at least 5 digits");
                    return;
                }

                _configManager.AddAllowedUser(userId);
                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error adding allowed user: {ex.Message}");
            }
        }

        public async Task HandleRemoveAllowedUserAsync(dynamic data, string replyTo, WebSocketHandler handler)
        {
            try
            {
                long userId = (long)data.userId;
                _configManager.RemoveAllowedUser(userId);
                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error removing allowed user: {ex.Message}");
            }
        }

        public async Task HandleAddAllowedGroupAsync(dynamic data, string replyTo, WebSocketHandler handler)
        {
            try
            {
                long groupId = (long)data.groupId;

                string groupIdStr = groupId.ToString();
                if (groupIdStr.Length < 5)
                {
                    Logger.LogWarning("ControlPanel", $"Rejected group ID {groupId}: must be at least 5 digits");
                    return;
                }

                _configManager.AddAllowedGroup(groupId);
                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error adding allowed group: {ex.Message}");
            }
        }

        public async Task HandleRemoveAllowedGroupAsync(dynamic data, string replyTo, WebSocketHandler handler)
        {
            try
            {
                long groupId = (long)data.groupId;
                _configManager.RemoveAllowedGroup(groupId);
                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error removing allowed group: {ex.Message}");
            }
        }
    }
}
