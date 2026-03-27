using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using LLama;
using LLama.Common;

namespace AI_Chat.Services
{
    public class LocalEmbeddingService
    {
        private static readonly object _initLock = new object();
        private static LocalEmbeddingService _instance;
        private LLamaWeights _model;
        private LLamaEmbedder _embedder;
        private bool _isInitialized = false;
        private string _modelPath;

        public static LocalEmbeddingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_initLock)
                    {
                        if (_instance == null)
                        {
                            _instance = new LocalEmbeddingService();
                        }
                    }
                }
                return _instance;
            }
        }

        private LocalEmbeddingService()
        {
        }

        public bool IsInitialized => _isInitialized;

        public string ModelPath => _modelPath ?? GetDefaultModelPath();

        public static string GetDefaultModelPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = Path.Combine(baseDir, "Models");
            return Path.Combine(modelsDir, "bge-m3-q8_0.gguf");
        }

        public static string GetModelsDirectory()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "Models");
        }

        public static bool IsModelExists()
        {
            string modelPath = GetDefaultModelPath();
            bool exists = File.Exists(modelPath);
            Logger.LogInfo("LOCAL_EMBEDDING", $"Checking model existence at: {modelPath}, exists: {exists}");
            return exists;
        }

        public static void EnsureModelsDirectoryExists()
        {
            string modelsDir = GetModelsDirectory();
            if (!Directory.Exists(modelsDir))
            {
                Directory.CreateDirectory(modelsDir);
                Logger.LogInfo("LOCAL_EMBEDDING", $"Created models directory: {modelsDir}");
            }
        }

        public bool Initialize()
        {
            if (_isInitialized)
            {
                return true;
            }

            try
            {
                string modelPath = GetDefaultModelPath();
                if (!File.Exists(modelPath))
                {
                    Logger.LogError("LOCAL_EMBEDDING", $"Model file not found: {modelPath}");
                    return false;
                }

                Logger.LogInfo("LOCAL_EMBEDDING", $"Initializing local embedding model from: {modelPath}");

                // 获取 CPU 核心数，使用所有核心
                int cpuCores = Environment.ProcessorCount;
                Logger.LogInfo("LOCAL_EMBEDDING", $"Configuring model to use {cpuCores} CPU cores");

                var parameters = new ModelParams(modelPath)
                {
                    ContextSize = 8192,
                    GpuLayerCount = 0,
                    Embeddings = true,
                    Threads = cpuCores,           // 推理线程数
                    BatchThreads = cpuCores,      // 批处理线程数
                    BatchSize = 512,              // 批处理大小
                    UBatchSize = 512,             // 微批处理大小
                    UseMemorymap = true,          // 使用内存映射加速加载
                    UseMemoryLock = false         // 不使用内存锁定（避免权限问题）
                };

                _model = LLamaWeights.LoadFromFile(parameters);
                _embedder = new LLamaEmbedder(_model, parameters);
                _modelPath = modelPath;
                _isInitialized = true;

                Logger.LogInfo("LOCAL_EMBEDDING", "Local embedding model initialized successfully");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError("LOCAL_EMBEDDING", $"Failed to initialize local embedding model: {ex.Message}", ex);
                return false;
            }
        }

        public void Uninitialize()
        {
            if (_embedder != null)
            {
                _embedder.Dispose();
                _embedder = null;
            }
            if (_model != null)
            {
                _model.Dispose();
                _model = null;
            }
            _isInitialized = false;
            Logger.LogInfo("LOCAL_EMBEDDING", "Local embedding model uninitialized");
        }

        public float[] GenerateEmbedding(string text)
        {
            if (!_isInitialized || _embedder == null)
            {
                Logger.LogError("LOCAL_EMBEDDING", "Local embedding model is not initialized");
                return null;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return new float[1024];
                }

                // Get embeddings - returns Task<IReadOnlyList<float[]>>
                var embeddingsTask = _embedder.GetEmbeddings(text);
                var embeddings = embeddingsTask.GetAwaiter().GetResult();
                
                if (embeddings == null || embeddings.Count == 0)
                {
                    Logger.LogError("LOCAL_EMBEDDING", "Failed to generate embedding: empty result");
                    return null;
                }

                // Get the first embedding (for single text input)
                var firstEmbedding = embeddings[0];
                float[] result = new float[firstEmbedding.Length];
                firstEmbedding.CopyTo(result, 0);

                Logger.LogInfo("LOCAL_EMBEDDING", $"Successfully generated embedding for text, dimension: {result.Length}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.LogError("LOCAL_EMBEDDING", $"Error generating embedding: {ex.Message}", ex);
                return null;
            }
        }

        public static async Task<bool> DownloadModelAsync(
            string url = "https://www.modelscope.cn/models/ggml-org/bge-m3-Q8_0-GGUF/resolve/master/bge-m3-q8_0.gguf",
            IProgress<DownloadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                EnsureModelsDirectoryExists();
                string modelPath = GetDefaultModelPath();
                string tempPath = modelPath + ".tmp";

                Logger.LogInfo("LOCAL_EMBEDDING", $"Starting model download from: {url}");

                // 使用多线程下载
                const int chunkCount = 4; // 分4个线程下载
                long totalBytes = await GetFileSizeAsync(url);
                
                if (totalBytes <= 0)
                {
                    Logger.LogWarning("LOCAL_EMBEDDING", "Could not determine file size, falling back to single-threaded download");
                    return await DownloadModelSingleThreadAsync(url, tempPath, totalBytes, progress, cancellationToken);
                }

                long chunkSize = totalBytes / chunkCount;
                var chunkFiles = new string[chunkCount];
                var downloadTasks = new Task[chunkCount];
                var chunkProgress = new long[chunkCount];

                // 创建进度报告定时器
                using var progressTimer = new System.Timers.Timer(500);
                progressTimer.Elapsed += (s, e) =>
                {
                    long totalDownloaded = 0;
                    for (int i = 0; i < chunkCount; i++)
                    {
                        totalDownloaded += chunkProgress[i];
                    }
                    int percentage = (int)((totalDownloaded * 100) / totalBytes);
                    progress?.Report(new DownloadProgress
                    {
                        Status = DownloadStatus.Downloading,
                        Progress = percentage,
                        DownloadedBytes = totalDownloaded,
                        TotalBytes = totalBytes,
                        DownloadedMB = totalDownloaded / (1024.0 * 1024.0),
                        TotalMB = totalBytes / (1024.0 * 1024.0)
                    });
                };
                progressTimer.Start();

                // 启动多线程下载
                for (int i = 0; i < chunkCount; i++)
                {
                    int chunkIndex = i;
                    long startByte = chunkIndex * chunkSize;
                    long endByte = (chunkIndex == chunkCount - 1) ? totalBytes - 1 : (startByte + chunkSize - 1);
                    chunkFiles[chunkIndex] = tempPath + $".part{chunkIndex}";

                    downloadTasks[chunkIndex] = Task.Run(async () =>
                    {
                        await DownloadChunkAsync(url, chunkFiles[chunkIndex], startByte, endByte, 
                            bytes => chunkProgress[chunkIndex] = bytes, cancellationToken);
                    }, cancellationToken);
                }

                // 等待所有分块下载完成
                await Task.WhenAll(downloadTasks);
                progressTimer.Stop();

                // 合并分块文件
                Logger.LogInfo("LOCAL_EMBEDDING", "Merging downloaded chunks...");
                await MergeChunksAsync(chunkFiles, tempPath, cancellationToken);

                // 清理分块文件
                foreach (var chunkFile in chunkFiles)
                {
                    if (File.Exists(chunkFile))
                    {
                        File.Delete(chunkFile);
                    }
                }

                // 移动最终文件
                if (File.Exists(modelPath))
                {
                    File.Delete(modelPath);
                }
                File.Move(tempPath, modelPath);

                Logger.LogInfo("LOCAL_EMBEDDING", $"Model downloaded successfully to: {modelPath}");

                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Completed,
                    Progress = 100,
                    DownloadedBytes = totalBytes,
                    TotalBytes = totalBytes,
                    DownloadedMB = totalBytes / (1024.0 * 1024.0),
                    TotalMB = totalBytes / (1024.0 * 1024.0)
                });

                return true;
            }
            catch (OperationCanceledException)
            {
                Logger.LogWarning("LOCAL_EMBEDDING", "Model download was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError("LOCAL_EMBEDDING", $"Failed to download model: {ex.Message}", ex);
                progress?.Report(new DownloadProgress
                {
                    Status = DownloadStatus.Error,
                    Message = ex.Message
                });
                return false;
            }
        }

        private static async Task<long> GetFileSizeAsync(string url)
        {
            try
            {
                using var httpClient = CreateHttpClient();
                // 尝试使用 HEAD 请求
                var headRequest = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResponse = await httpClient.SendAsync(headRequest);
                if (headResponse.IsSuccessStatusCode && headResponse.Content.Headers.ContentLength.HasValue)
                {
                    return headResponse.Content.Headers.ContentLength.Value;
                }

                // 如果 HEAD 请求失败，尝试使用 GET 请求并读取 Content-Length
                var getRequest = new HttpRequestMessage(HttpMethod.Get, url);
                using var getResponse = await httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead);
                if (getResponse.IsSuccessStatusCode && getResponse.Content.Headers.ContentLength.HasValue)
                {
                    return getResponse.Content.Headers.ContentLength.Value;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning("LOCAL_EMBEDDING", $"Failed to get file size: {ex.Message}");
            }
            return -1;
        }

        private static HttpClient CreateHttpClient()
        {
            var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(30);
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept", "*/*");
            httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            return httpClient;
        }

        private static async Task DownloadChunkAsync(string url, string outputPath, long startByte, long endByte, 
            Action<long> onProgress, CancellationToken cancellationToken)
        {
            using var httpClient = CreateHttpClient();
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(startByte, endByte);

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[65536]; // 64KB buffer
            int bytesRead;
            long totalRead = 0;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                totalRead += bytesRead;
                onProgress?.Invoke(totalRead);
            }
        }

        private static async Task MergeChunksAsync(string[] chunkFiles, string outputPath, CancellationToken cancellationToken)
        {
            using var outputStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            foreach (var chunkFile in chunkFiles)
            {
                using var inputStream = File.OpenRead(chunkFile);
                await inputStream.CopyToAsync(outputStream, cancellationToken);
            }
        }

        private static async Task<bool> DownloadModelSingleThreadAsync(string url, string tempPath, long totalBytes,
            IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
        {
            using var httpClient = CreateHttpClient();
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (totalBytes <= 0)
            {
                totalBytes = response.Content.Headers.ContentLength ?? -1;
            }

            long downloadedBytes = 0;
            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[65536];
            int bytesRead;
            DateTime lastProgressUpdate = DateTime.MinValue;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
                downloadedBytes += bytesRead;

                if (progress != null && DateTime.Now - lastProgressUpdate > TimeSpan.FromMilliseconds(500))
                {
                    int percentage = totalBytes > 0 ? (int)((downloadedBytes * 100) / totalBytes) : 0;
                    progress.Report(new DownloadProgress
                    {
                        Status = DownloadStatus.Downloading,
                        Progress = percentage,
                        DownloadedBytes = downloadedBytes,
                        TotalBytes = totalBytes,
                        DownloadedMB = downloadedBytes / (1024.0 * 1024.0),
                        TotalMB = totalBytes / (1024.0 * 1024.0)
                    });
                    lastProgressUpdate = DateTime.Now;
                }
            }

            return true;
        }
    }

    public class DownloadProgress
    {
        public DownloadStatus Status { get; set; }
        public int Progress { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public double DownloadedMB { get; set; }
        public double TotalMB { get; set; }
        public string Message { get; set; }
    }

    public enum DownloadStatus
    {
        Downloading,
        Completed,
        Error
    }
}
