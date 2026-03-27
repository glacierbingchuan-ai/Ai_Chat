using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AI_Chat.Services
{
    /// <summary>
    /// 图片服务 - 处理CQ:image格式解析、下载和本地存储
    /// </summary>
    public class ImageService
    {
        private readonly HttpClient _httpClient;

        // CQ:image 格式正则表达式
        // 匹配 [CQ:image,file=xxx,sub_type=0,url=xxx,file_size=xxx]
        private static readonly Regex CqImageRegex = new Regex(
            @"\[CQ:image,([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 软件自己的图片格式 [IMG:文件名]
        private static readonly Regex ImgTagRegex = new Regex(
            @"\[IMG:([^\]]+)\]",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // 用于存储等待响应的请求（静态，所有实例共享）
        private static readonly Dictionary<string, TaskCompletionSource<JObject>> _pendingRequests = new Dictionary<string, TaskCompletionSource<JObject>>();
        private static readonly object _lock = new object();

        public ImageService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        /// <summary>
        /// 检查消息是否包含CQ:image格式
        /// </summary>
        public static bool ContainsCqImage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;
            return CqImageRegex.IsMatch(message);
        }

        /// <summary>
        /// 检查消息是否包含软件自己的图片格式
        /// </summary>
        public static bool ContainsImgTag(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;
            return ImgTagRegex.IsMatch(message);
        }

        /// <summary>
        /// 从CQ:image格式中提取file参数
        /// </summary>
        public static string ExtractFileId(string cqImageTag)
        {
            if (string.IsNullOrEmpty(cqImageTag))
                return null;

            var match = Regex.Match(cqImageTag, @"file=([^,\]]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return null;
        }

        /// <summary>
        /// 从CQ:image格式中提取url参数
        /// </summary>
        public static string ExtractUrl(string cqImageTag)
        {
            if (string.IsNullOrEmpty(cqImageTag))
                return null;

            var match = Regex.Match(cqImageTag, @"url=([^,\]]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return null;
        }

        /// <summary>
        /// 从CQ:image格式中提取file_size参数
        /// </summary>
        public static string ExtractFileSize(string cqImageTag)
        {
            if (string.IsNullOrEmpty(cqImageTag))
                return null;

            var match = Regex.Match(cqImageTag, @"file_size=([^,\]]+)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return null;
        }

        /// <summary>
        /// 构建 get_image 请求的 JSON
        /// </summary>
        public string BuildGetImageRequest(string fileId, string echo)
        {
            var payload = new
            {
                action = "get_image",
                @params = new { file = fileId },
                echo = echo
            };
            return JsonConvert.SerializeObject(payload);
        }

        /// <summary>
        /// 发送请求并等待响应
        /// </summary>
        public async Task<ImageInfoResponse> GetImageInfoAsync(string fileId, Func<string, Task> sendMessageFunc)
        {
            string echo = Guid.NewGuid().ToString("N");

            try
            {
                string requestJson = BuildGetImageRequest(fileId, echo);

                var tcs = new TaskCompletionSource<JObject>();
                lock (_lock)
                {
                    _pendingRequests[echo] = tcs;
                }

                Logger.LogInfo("ImageService", $"Sending get_image request via WebSocket for file: {fileId}, echo: {echo}");

                await sendMessageFunc(requestJson);

                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    cts.Token.Register(() => tcs.TrySetCanceled(), useSynchronizationContext: false);

                    JObject response = await tcs.Task;
                    return ParseImageResponse(response);
                }
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("ImageService", "Request timed out waiting for response");
                return new ImageInfoResponse
                {
                    Success = false,
                    ErrorMessage = "Request timeout"
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("ImageService", $"Failed to get image info: {ex.Message}");
                return new ImageInfoResponse
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
            finally
            {
                lock (_lock)
                {
                    _pendingRequests.Remove(echo);
                }
            }
        }

        /// <summary>
        /// 处理协议端返回的响应（静态方法，所有实例共享）
        /// </summary>
        public static void HandleResponse(JObject response)
        {
            try
            {
                if (response["echo"] == null)
                    return;

                string echo = response["echo"].ToString();

                lock (_lock)
                {
                    if (_pendingRequests.TryGetValue(echo, out var tcs))
                    {
                        tcs.TrySetResult(response);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("ImageService", $"Error handling response: {ex.Message}");
            }
        }

        /// <summary>
        /// 解析图片响应
        /// </summary>
        private ImageInfoResponse ParseImageResponse(JObject response)
        {
            try
            {
                string status = response["status"]?.ToString();
                int retcode = response["retcode"]?.Value<int>() ?? -1;

                if (status == "ok" && retcode == 0)
                {
                    var data = response["data"] as JObject;
                    if (data != null)
                    {
                        string url = data["url"]?.ToString();
                        Logger.LogInfo("ImageService", $"Successfully got image info. URL: {url}");

                        return new ImageInfoResponse
                        {
                            Success = true,
                            File = data["file"]?.ToString(),
                            Url = url,
                            FileSize = data["file_size"]?.ToString(),
                            FileName = data["file_name"]?.ToString(),
                            Base64 = data["base64"]?.ToString()
                        };
                    }
                }

                string message = response["message"]?.ToString() ?? "Unknown error";
                Logger.LogWarning("ImageService", $"API returned error status: {status}, retcode: {retcode}, message: {message}");

                return new ImageInfoResponse
                {
                    Success = false,
                    ErrorMessage = message
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("ImageService", $"Failed to parse response: {ex.Message}");
                return new ImageInfoResponse
                {
                    Success = false,
                    ErrorMessage = $"Parse error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 下载图片到指定目录
        /// </summary>
        public async Task<DownloadResult> DownloadImageAsync(string imageUrl, string saveDirectory, string fileName = null)
        {
            try
            {
                if (string.IsNullOrEmpty(imageUrl))
                {
                    return new DownloadResult { Success = false, ErrorMessage = "Image URL is empty" };
                }

                // 确保目录存在
                Directory.CreateDirectory(saveDirectory);

                // 如果没有指定文件名，从URL生成
                if (string.IsNullOrEmpty(fileName))
                {
                    string extension = GetImageExtensionFromUrl(imageUrl);
                    fileName = $"{Guid.NewGuid():N}{extension}";
                }

                string filePath = Path.Combine(saveDirectory, fileName);

                Logger.LogInfo("ImageService", $"Downloading image from {imageUrl} to {filePath}");

                // 下载图片
                byte[] imageData = await _httpClient.GetByteArrayAsync(imageUrl);

                // 保存到文件
                await File.WriteAllBytesAsync(filePath, imageData);

                Logger.LogInfo("ImageService", $"Successfully downloaded image: {fileName}, size: {imageData.Length} bytes");

                return new DownloadResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = filePath,
                    FileSize = imageData.Length
                };
            }
            catch (Exception ex)
            {
                Logger.LogError("ImageService", $"Failed to download image: {ex.Message}");
                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        /// <summary>
        /// 从URL获取图片扩展名
        /// </summary>
        private string GetImageExtensionFromUrl(string url)
        {
            try
            {
                // 尝试从URL路径获取扩展名
                var uri = new Uri(url);
                string path = uri.AbsolutePath;
                string extension = Path.GetExtension(path);

                if (!string.IsNullOrEmpty(extension))
                {
                    return extension;
                }
            }
            catch { }

            // 默认返回 .jpg
            return ".jpg";
        }

        /// <summary>
        /// 处理消息中的CQ:image，下载图片并转换为软件自己的格式
        /// </summary>
        public async Task<ProcessImageResult> ProcessCqImagesAsync(string message, string userDataDirectory, Func<string, Task> sendMessageFunc)
        {
            var result = new ProcessImageResult
            {
                OriginalMessage = message,
                ProcessedImages = new List<ImageInfo>()
            };

            if (string.IsNullOrEmpty(message) || !ContainsCqImage(message))
            {
                result.ConvertedMessage = message;
                return result;
            }

            string convertedMessage = message;
            var matches = CqImageRegex.Matches(message);

            foreach (Match match in matches)
            {
                var cqImageTag = match.Value;
                var fileId = ExtractFileId(cqImageTag);

                if (!string.IsNullOrEmpty(fileId))
                {
                    // 获取图片信息
                    var imageInfo = await GetImageInfoAsync(fileId, sendMessageFunc);

                    if (imageInfo.Success && !string.IsNullOrEmpty(imageInfo.Url))
                    {
                        // 下载图片到用户目录
                        var downloadResult = await DownloadImageAsync(imageInfo.Url, userDataDirectory, imageInfo.FileName);

                        if (downloadResult.Success)
                        {
                            // 替换为软件自己的格式
                            string imgTag = $"[IMG:{downloadResult.FileName}]";
                            convertedMessage = convertedMessage.Replace(cqImageTag, imgTag);

                            result.ProcessedImages.Add(new ImageInfo
                            {
                                OriginalFileId = fileId,
                                FileName = downloadResult.FileName,
                                FilePath = downloadResult.FilePath,
                                Url = imageInfo.Url
                            });

                            Logger.LogInfo("ImageService", $"Converted CQ:image to {imgTag}");
                        }
                        else
                        {
                            Logger.LogWarning("ImageService", $"Failed to download image for fileId {fileId}: {downloadResult.ErrorMessage}");
                        }
                    }
                    else
                    {
                        Logger.LogWarning("ImageService", $"Failed to get image info for fileId {fileId}: {imageInfo.ErrorMessage}");
                    }
                }
            }

            result.ConvertedMessage = convertedMessage;
            return result;
        }

        /// <summary>
        /// 读取本地图片并编码为base64
        /// </summary>
        public async Task<string> ReadImageAsBase64Async(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Logger.LogWarning("ImageService", $"Image file not found: {filePath}");
                    return null;
                }

                byte[] imageData = await File.ReadAllBytesAsync(filePath);
                return Convert.ToBase64String(imageData);
            }
            catch (Exception ex)
            {
                Logger.LogError("ImageService", $"Failed to read image as base64: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取图片的MIME类型
        /// </summary>
        public string GetImageMimeType(string fileName)
        {
            string extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".bmp" => "image/bmp",
                _ => "image/jpeg"
            };
        }

        /// <summary>
        /// 处理包含[IMG:]标签的消息，读取本地图片构建多模态内容
        /// </summary>
        public async Task<List<ContentPart>> BuildMultimodalContentAsync(string message, string userDataDirectory)
        {
            var result = new List<ContentPart>();

            if (string.IsNullOrEmpty(message))
                return result;

            if (!ContainsImgTag(message))
            {
                result.Add(new ContentPart { Type = "text", Text = message });
                return result;
            }

            int lastIndex = 0;
            var matches = ImgTagRegex.Matches(message);

            foreach (Match match in matches)
            {
                // 添加图片前的文本
                if (match.Index > lastIndex)
                {
                    var textBefore = message.Substring(lastIndex, match.Index - lastIndex).Trim();
                    if (!string.IsNullOrEmpty(textBefore))
                    {
                        result.Add(new ContentPart { Type = "text", Text = textBefore });
                    }
                }

                // 处理图片
                string fileName = match.Groups[1].Value.Trim();
                string filePath = Path.Combine(userDataDirectory, fileName);

                if (File.Exists(filePath))
                {
                    string base64 = await ReadImageAsBase64Async(filePath);
                    if (!string.IsNullOrEmpty(base64))
                    {
                        string mimeType = GetImageMimeType(fileName);
                        result.Add(new ContentPart
                        {
                            Type = "image_base64",
                            FileName = fileName,
                            MimeType = mimeType,
                            Base64Data = base64
                        });
                    }
                }
                else
                {
                    Logger.LogWarning("ImageService", $"Image file not found: {filePath}");
                }

                lastIndex = match.Index + match.Length;
            }

            // 添加最后一段文本
            if (lastIndex < message.Length)
            {
                var textAfter = message.Substring(lastIndex).Trim();
                if (!string.IsNullOrEmpty(textAfter))
                {
                    result.Add(new ContentPart { Type = "text", Text = textAfter });
                }
            }

            return result;
        }

        /// <summary>
        /// 过滤消息中的CQ:image和[IMG:]标签，只保留纯文本
        /// </summary>
        public static string FilterImageTags(string message)
        {
            if (string.IsNullOrEmpty(message))
                return message;

            // 移除CQ:image标签
            var result = CqImageRegex.Replace(message, "");
            // 移除[IMG:]标签
            result = ImgTagRegex.Replace(result, "");

            // 清理多余的空格
            result = Regex.Replace(result.Trim(), @"\s+", " ");

            return result;
        }
    }

    /// <summary>
    /// 图片信息响应
    /// </summary>
    public class ImageInfoResponse
    {
        public bool Success { get; set; }
        public string File { get; set; }
        public string Url { get; set; }
        public string FileSize { get; set; }
        public string FileName { get; set; }
        public string Base64 { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 下载结果
    /// </summary>
    public class DownloadResult
    {
        public bool Success { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// 处理图片结果
    /// </summary>
    public class ProcessImageResult
    {
        public string OriginalMessage { get; set; }
        public string ConvertedMessage { get; set; }
        public List<ImageInfo> ProcessedImages { get; set; }
    }

    /// <summary>
    /// 图片信息
    /// </summary>
    public class ImageInfo
    {
        public string OriginalFileId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string Url { get; set; }
    }

    /// <summary>
    /// 内容部分（用于多模态）
    /// </summary>
    public class ContentPart
    {
        public string Type { get; set; } // "text" or "image_base64"
        public string Text { get; set; }
        public string FileName { get; set; }
        public string MimeType { get; set; }
        public string Base64Data { get; set; }
    }
}
