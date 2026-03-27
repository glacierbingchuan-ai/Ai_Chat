using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using AI_Chat.Managers;
using AI_Chat.Models;

namespace AI_Chat.Services.ControlPanel.Handlers
{
    public class UserConfigHandler
    {
        private readonly UserConfigManager _userConfigManager;

        public UserConfigHandler(UserConfigManager userConfigManager)
        {
            _userConfigManager = userConfigManager;
        }

        public async Task HandleGetUserConfigAsync(WebSocket webSocket, dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                var userConfig = _userConfigManager.GetOrCreateUserConfig(userId);
                await handler.SendResponseAsync(webSocket, "user_config", userConfig, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error getting user config: {ex.Message}");
            }
        }

        public async Task HandleUpdateUserConfigAsync(dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                _userConfigManager.UpdateUserConfig(userId, data);
                var userConfig = _userConfigManager.GetUserConfig(userId);
                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "user_config_updated", Data = userConfig, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error updating user config: {ex.Message}");
            }
        }

        public async Task HandleResetUserConfigAsync(dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                _userConfigManager.ResetUserConfig(userId, data);
                var userConfig = _userConfigManager.GetUserConfig(userId);
                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "user_config_updated", Data = userConfig, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("ControlPanel", $"Error resetting user config: {ex.Message}");
            }
        }
    }
}
