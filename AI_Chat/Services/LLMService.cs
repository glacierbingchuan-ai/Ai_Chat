using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AI_Chat.Models;
using AI_Chat.Constants;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class LLMService
    {
        private readonly HttpClient _httpClient;
        private readonly ConfigManager _configManager;
        private bool _lastLlmStatus = false;
        private DateTime _lastLlmCheckTime = DateTime.MinValue;

        public LLMService(ConfigManager configManager)
        {
            _configManager = configManager;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add(AppConstants.LLM_AUTH_HEADER, $"{AppConstants.LLM_AUTH_SCHEME} {_configManager.Config.LlmApiKey}");
            _httpClient.Timeout = TimeSpan.FromSeconds(100);
        }

        public void UpdateApiKey(string apiKey)
        {
            _httpClient.DefaultRequestHeaders.Remove(AppConstants.LLM_AUTH_HEADER);
            _httpClient.DefaultRequestHeaders.Add(AppConstants.LLM_AUTH_HEADER, $"{AppConstants.LLM_AUTH_SCHEME} {apiKey}");
        }

        public async Task<string> GetRawLLMResponseAsync(List<Message> context, CancellationToken token)
        {
            var body = new
            {
                model = _configManager.Config.LlmModelName,
                messages = context.Select(m => new { role = m.Role, content = m.Content }),
                max_tokens = _configManager.Config.LlmMaxTokens,
                temperature = _configManager.Config.LlmTemperature,
                top_p = _configManager.Config.LlmTopP
            };

            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(40));
                    var res = await _httpClient.PostAsync(_configManager.Config.LlmApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), cts.Token);
                    string rawJson = await res.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<dynamic>(rawJson)?.choices?[0]?.message?.content?.ToString();
                }
            }
            catch { return null; }
        }

        public async Task<CompletenessLevel> IsUserMessageCompleteAsync(string message, string hid)
        {
            var body = new
            {
                model = _configManager.Config.LlmModelName,
                messages = new[]
                {
                    new { role = "system", content = _configManager.Config.IncompleteInputPrompt },
                    new { role = "user", content = message }
                },
                max_tokens = 15,
                temperature = 0.0
            };

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    var res = await _httpClient.PostAsync(_configManager.Config.LlmApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), cts.Token);
                    string result = JsonConvert.DeserializeObject<dynamic>(await res.Content.ReadAsStringAsync())?.choices?[0]?.message?.content?.ToString().ToUpper() ?? "";
                    if (result.Contains("INCOMPLETE")) return CompletenessLevel.Incomplete;
                    if (result.Contains("UNCERTAIN")) return CompletenessLevel.Uncertain;
                    return CompletenessLevel.Complete;
                }
            }
            catch { return CompletenessLevel.Complete; }
        }

        public async Task<string> SummarizeContextAsync(List<Message> messagesToSummarize)
        {
            string history = string.Join("\n", messagesToSummarize
                .Where(m => m.Role != "system"  // 跳过所有 system 消息
                         && !m.Content.Contains(AppConstants.TAG_PROACTIVE)
                         && !m.Content.Contains(AppConstants.TAG_REMINDER)
                         && !m.Content.Contains(AppConstants.TAG_FORMAT_ERROR))
                .Select(m =>
                {
                    string displayContent = m.Content;
                    if (m.Role == "assistant" && displayContent.Trim().StartsWith("{"))
                    {
                        try
                        {
                            var parsed = JsonConvert.DeserializeObject<AIReplyModel>(displayContent);
                            if (parsed != null && parsed.Messages != null)
                            {
                                var items = parsed.Messages.Select(item =>
                                {
                                    if (item.content != null) return item.content.ToString();
                                    if (item.meme != null) return $"[表情包:{item.meme}]";
                                    return "";
                                });
                                displayContent = string.Join(" ", items);
                            }
                        }
                        catch { }
                    }
                    return $"{m.Role}: {displayContent}";
                }));

            if (string.IsNullOrWhiteSpace(history)) return null;

            var body = new
            {
                model = _configManager.Config.LlmModelName,
                messages = new[]
                {
                    new { role = "system", content = "请基于【历史对话总结】和【新增对话内容】，生成一份完整、详细的最新对话总结。\n要求：\n1. 必须包含所有核心信息：人物、核心话题、关键观点、时间信息、约定事件、补充细节\n2. 合并历史总结和新增内容，避免重复，保持逻辑连贯\n3. 语言精炼，去除冗余话术\n4. 总结开头必须以\"对话总结：\"开头\n5. 注意分清人物 assistant是助手，user是用户\n6. 注意包含历史对话总结的详细信息，不要遗漏任何关键信息7. 只能使用纯文本输出" },
                    new { role = "user", content = history }
                }
            };

            var res = await _httpClient.PostAsync(_configManager.Config.LlmApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"));
            string summary = JsonConvert.DeserializeObject<dynamic>(await res.Content.ReadAsStringAsync())?.choices?[0]?.message?.content?.ToString();

            if (summary != null && summary.StartsWith("对话总结："))
                summary = summary.Substring(5).Trim();

            return summary;
        }

        public bool TryParseAndValidateReply(string raw, out AIReplyModel model)
        {
            model = null;
            try
            {
                string content = Regex.Replace(raw, @"```json\s*", "");
                content = Regex.Replace(content, @"```\s*", "").Trim();

                var settings = new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Ignore };
                model = JsonConvert.DeserializeObject<AIReplyModel>(content, settings);

                if (model == null || model.Messages == null)
                {
                    model = null;
                    return false;
                }

                foreach (var m in model.Messages)
                {
                    string mStr = m.ToString();
                    bool hasContent = mStr.Contains("\"content\":");
                    bool hasMeme = mStr.Contains("\"meme\":");

                    if (hasContent && hasMeme)
                    {
                        model = null;
                        return false;
                    }

                    if (hasContent)
                    {
                        string text = "";
                        try { text = m.content?.ToString() ?? ""; } catch { }
                        if (text.IndexOf("MEME", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.IndexOf(".jpg", StringComparison.OrdinalIgnoreCase) >= 0
                            || text.Contains("_"))
                        {
                            model = null;
                            return false;
                        }
                    }

                    if (!hasContent && !hasMeme)
                    {
                        model = null;
                        return false;
                    }
                }

                return true;
            }
            catch
            {
                model = null;
                return false;
            }
        }

        public async Task<Dictionary<string, object>> CheckLlmApiStatusAsync(string modelName = null, string apiBaseUrl = null, string apiKey = null)
        {
            try
            {
                string actualModelName = modelName ?? _configManager.Config.LlmModelName;
                string actualApiBaseUrl = apiBaseUrl ?? _configManager.Config.LlmApiBaseUrl;
                string actualApiKey = apiKey ?? _configManager.Config.LlmApiKey;

                using (var testHttpClient = new HttpClient())
                {
                    testHttpClient.DefaultRequestHeaders.Add(AppConstants.LLM_AUTH_HEADER, $"{AppConstants.LLM_AUTH_SCHEME} {actualApiKey}");
                    testHttpClient.Timeout = TimeSpan.FromSeconds(10);
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    {
                        var body = new
                        {
                            model = actualModelName,
                            messages = new[]
                            {
                                new { role = "system", content = "Ping" },
                                new { role = "user", content = "Ping" }
                            },
                            max_tokens = 1,
                            temperature = 0.0
                        };
                        var res = await testHttpClient.PostAsync(actualApiBaseUrl, new StringContent(JsonConvert.SerializeObject(body), Encoding.UTF8, "application/json"), cts.Token);
                        if (res.IsSuccessStatusCode)
                            return new Dictionary<string, object> { { "success", true }, { "message", "Success: LLM service is available" } };
                        else
                            return new Dictionary<string, object> { { "success", false }, { "message", $"Failed: {res.StatusCode}" } };
                    }
                }
            }
            catch (Exception ex)
            {
                return new Dictionary<string, object> { { "success", false }, { "message", $"Failed: {ex.Message}" } };
            }
        }

        public async Task<string> GetLlmStatusAsync()
        {
            bool llmApiAvailable = _lastLlmStatus;
            if (!_lastLlmStatus || (DateTime.Now - _lastLlmCheckTime).TotalMilliseconds >= AppConstants.LLM_STATUS_CHECK_INTERVAL)
            {
                var result = await CheckLlmApiStatusAsync();
                llmApiAvailable = (bool)result["success"];
                _lastLlmStatus = llmApiAvailable;
                _lastLlmCheckTime = DateTime.Now;
            }
            return llmApiAvailable ? "Online" : "Offline";
        }

        /// <summary>
        /// 发送原始LLM请求（插件使用）
        /// </summary>
        /// <param name="requestJson">请求JSON字符串</param>
        /// <returns>原始响应JSON</returns>
        public async Task<string> SendRequestRawAsync(string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                return JsonConvert.SerializeObject(new { error = "请求JSON不能为空" });
            }

            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                    var res = await _httpClient.PostAsync(_configManager.Config.LlmApiBaseUrl, content, cts.Token);
                    string rawJson = await res.Content.ReadAsStringAsync();
                    return rawJson;
                }
            }
            catch (Exception ex)
            {
                return JsonConvert.SerializeObject(new { error = ex.Message });
            }
        }
    }
}
