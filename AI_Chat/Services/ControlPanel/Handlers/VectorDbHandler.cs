using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading.Tasks;
using AI_Chat.Managers;
using AI_Chat.Models;

namespace AI_Chat.Services.ControlPanel.Handlers
{
    public class VectorDbHandler
    {
        private readonly UserSessionManager _sessionManager;
        private readonly ConfigManager _configManager;

        public VectorDbHandler(UserSessionManager sessionManager, ConfigManager configManager)
        {
            _sessionManager = sessionManager;
            _configManager = configManager;
        }

        public async Task HandleGetVectorEntriesAsync(WebSocket webSocket, dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                int page = data?.page != null ? (int)data.page : 1;
                int pageSize = data?.pageSize != null ? (int)data.pageSize : 20;

                if (userId == 0)
                {
                    await handler.SendResponseAsync(webSocket, "vector_entries",
                        new { entries = new List<VectorEntry>(), totalCount = 0, page = page, pageSize = pageSize },
                        replyTo);
                    return;
                }

                var contextManager = _sessionManager.GetVectorContextManager(userId);
                if (contextManager == null)
                {
                    await handler.SendResponseAsync(webSocket, "vector_entries",
                        new { entries = new List<VectorEntry>(), totalCount = 0, page = page, pageSize = pageSize },
                        replyTo);
                    return;
                }

                var (entries, totalCount) = contextManager.GetVectorEntriesPaged(page, pageSize);

                var entriesWithoutVector = entries.Select(e => new
                {
                    id = e.Id,
                    content = e.Content,
                    role = e.Role,
                    userId = e.UserId,
                    timestamp = e.Timestamp,
                    metadata = e.Metadata
                }).ToList();

                await handler.SendResponseAsync(webSocket, "vector_entries",
                    new { entries = entriesWithoutVector, totalCount = totalCount, page = page, pageSize = pageSize },
                    replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error getting vector entries: {ex.Message}");
            }
        }

        public async Task HandleSearchVectorsAsync(WebSocket webSocket, dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = data?.userId != null ? (long)data.userId : selectedUserId;
                string query = data?.query?.ToString() ?? "";
                int topK = data?.topK != null ? (int)data.topK : 5;
                float threshold = data?.threshold != null ? (float)data.threshold : 0.2f;

                Logger.LogInfo("CONTROL_PANEL", $"Searching vectors for user {userId}, query: {query}, topK: {topK}, threshold: {threshold:F4}");

                if (userId == 0 || string.IsNullOrEmpty(query))
                {
                    await handler.SendResponseAsync(webSocket, "vector_search_results", new List<VectorEntry>(), replyTo);
                    return;
                }

                var contextManager = _sessionManager.GetVectorContextManager(userId);
                if (contextManager == null)
                {
                    await handler.SendResponseAsync(webSocket, "vector_search_results", new List<VectorEntry>(), replyTo);
                    return;
                }
                var results = contextManager.SearchSimilar(query, topK, threshold);

                Logger.LogInfo("CONTROL_PANEL", $"Found {results.Count} results for user {userId}");

                await handler.SendResponseAsync(webSocket, "vector_search_results", results, replyTo);
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error searching vectors: {ex.Message}", ex);
            }
        }

        public async Task HandleDeleteVectorEntryAsync(WebSocket webSocket, dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                string entryId = data?.id?.ToString() ?? "";

                if (userId == 0 || string.IsNullOrEmpty(entryId))
                {
                    return;
                }

                var contextManager = _sessionManager.GetVectorContextManager(userId);
                if (contextManager == null)
                {
                    await handler.SendResponseAsync(webSocket, "error", new { message = "Vector context not available" }, replyTo);
                    return;
                }
                contextManager.DeleteVectorEntry(entryId);

                await handler.SendResponseAsync(webSocket, "vector_entry_deleted", new { id = entryId }, replyTo);

                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "vector_entries_updated", ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error deleting vector entry: {ex.Message}");
            }
        }

        public async Task HandleClearVectorsAsync(WebSocket webSocket, dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                if (userId == 0)
                {
                    return;
                }

                var contextManager = _sessionManager.GetVectorContextManager(userId);
                if (contextManager == null)
                {
                    await handler.SendResponseAsync(webSocket, "error", new { message = "Vector context not available" }, replyTo);
                    return;
                }
                contextManager.ClearVectors();

                await handler.SendResponseAsync(webSocket, "vectors_cleared", new { userId = userId }, replyTo);

                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "vector_entries_updated", ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error clearing vectors: {ex.Message}");
            }
        }

        public async Task HandleRegenerateVectorsAsync(WebSocket webSocket, dynamic data, string replyTo, long selectedUserId, WebSocketHandler handler)
        {
            try
            {
                long userId = selectedUserId;
                if (data?.userId != null)
                {
                    userId = (long)data.userId;
                }

                if (userId == 0)
                {
                    return;
                }

                var contextManager = _sessionManager.GetVectorContextManager(userId);
                if (contextManager == null)
                {
                    await handler.SendResponseAsync(webSocket, "error", new { message = "Vector context not available" }, replyTo);
                    return;
                }
                await contextManager.RegenerateAllVectorsAsync();

                await handler.SendResponseAsync(webSocket, "vectors_regenerated", new { userId = userId }, replyTo);

                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "vector_entries_updated", ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error regenerating vectors: {ex.Message}", ex);
            }
        }

        public async Task HandleSaveVectorDbSettingsAsync(dynamic data, string replyTo, WebSocketHandler handler)
        {
            try
            {
                float threshold = data?.similarityThreshold != null ? (float)data.similarityThreshold : 0.2f;
                int topK = data?.topK != null ? (int)data.topK : 10;

                Logger.LogInfo("CONTROL_PANEL", $"Saving vector DB settings: threshold={threshold:F4}, topK={topK}");

                _configManager.Config.VectorDbSimilarityThreshold = threshold;
                _configManager.Config.VectorDbTopK = topK;
                _configManager.SaveConfig();

                handler.BroadcastMessageToClients(new WebSocketMessage { Type = "config_updated", Data = _configManager.Config, ReplyTo = replyTo });
            }
            catch (Exception ex)
            {
                Logger.LogError("CONTROL_PANEL", $"Error saving vector DB settings: {ex.Message}", ex);
            }
        }
    }
}
