using System;
using System.Threading.Tasks;
using Betalgo.Ranul.OpenAI;
using Betalgo.Ranul.OpenAI.Managers;
using Betalgo.Ranul.OpenAI.ObjectModels;
using Betalgo.Ranul.OpenAI.ObjectModels.RequestModels;

namespace AI_Chat.Services
{
    public class EmbeddingService
    {
        private ConfigManager _configManager;
        private readonly RequestRateLimiter _requestRateLimiter;
        private OpenAIService _openAIService;

        public EmbeddingService(ConfigManager configManager = null, RequestRateLimiter requestRateLimiter = null)
        {
            _configManager = configManager;
            _requestRateLimiter = requestRateLimiter;
        }

        private string GetEmbeddingApiKey()
        {
            if (_configManager != null && !string.IsNullOrEmpty(_configManager.Config.EmbeddingApiKey))
            {
                return _configManager.Config.EmbeddingApiKey;
            }
            return _configManager?.Config.LlmApiKey ?? "";
        }

        private string GetEmbeddingApiBaseUrl()
        {
            if (_configManager != null && !string.IsNullOrEmpty(_configManager.Config.EmbeddingApiBaseUrl))
            {
                return _configManager.Config.EmbeddingApiBaseUrl;
            }
            return _configManager?.Config.LlmApiBaseUrl ?? "";
        }

        private string GetEmbeddingModelName()
        {
            if (_configManager != null && !string.IsNullOrEmpty(_configManager.Config.EmbeddingModelName))
            {
                return _configManager.Config.EmbeddingModelName;
            }
            return "text-embedding-3-small";
        }

        private string GetBaseDomain(string apiUrl)
        {
            if (string.IsNullOrEmpty(apiUrl))
                return null;

            try
            {
                var uri = new Uri(apiUrl);
                string baseUrl = $"{uri.Scheme}://{uri.Host}:{uri.Port}";
                
                if (!string.IsNullOrEmpty(uri.PathAndQuery))
                {
                    string path = uri.PathAndQuery;
                    
                    int chatCompletionsIndex = path.IndexOf("/chat/completions", StringComparison.OrdinalIgnoreCase);
                    if (chatCompletionsIndex >= 0)
                    {
                        path = path.Substring(0, chatCompletionsIndex);
                    }
                    
                    if (!string.IsNullOrEmpty(path))
                    {
                        baseUrl += path;
                    }
                }
                
                return baseUrl;
            }
            catch
            {
                return null;
            }
        }

        private void InitializeOpenAIServiceIfNeeded(string apiKey, string baseUrl)
        {
            var newBaseUrl = GetBaseDomain(baseUrl);
            var options = new OpenAIOptions
            {
                ApiKey = apiKey,
                BaseDomain = newBaseUrl
            };
            _openAIService = new OpenAIService(options);
        }

        public void UpdateConfig(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public float[] GenerateEmbedding(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new float[1536];
            }

            try
            {
                var apiKey = GetEmbeddingApiKey();
                var baseUrl = GetEmbeddingApiBaseUrl();
                var modelName = GetEmbeddingModelName();

                if (!string.IsNullOrEmpty(apiKey))
                {
                    InitializeOpenAIServiceIfNeeded(apiKey, baseUrl);
                    
                    if (_openAIService != null)
                    {
                        var request = new EmbeddingCreateRequest
                        {
                            Model = modelName,
                            Input = text
                        };

                        var result = _openAIService.Embeddings.CreateEmbedding(request).GetAwaiter().GetResult();
                        if (result.Successful)
                        {
                            var embedding = result.Data[0].Embedding;
                            if (embedding != null && embedding.Count > 0)
                            {
                                var floatArray = new float[embedding.Count];
                                for (int i = 0; i < embedding.Count; i++)
                                {
                                    floatArray[i] = (float)embedding[i];
                                }
                                Logger.LogInfo("EMBEDDING", $"Successfully generated embedding for text, dimension: {floatArray.Length}");
                                return floatArray;
                            }
                        }
                        else
                        {
                            Logger.LogError("EMBEDDING", $"Failed to generate embedding: {result.Error?.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("EMBEDDING", $"Error generating embedding: {ex.Message}", ex);
            }

            Logger.LogWarning("EMBEDDING", "Falling back to pseudo-random embedding");
            return GenerateFallbackEmbedding(text);
        }

        private float[] GenerateFallbackEmbedding(string text)
        {
            var vector = new float[1536];
            
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                var hash = sha256.ComputeHash(bytes);
                
                var random = new Random(BitConverter.ToInt32(hash, 0));
                
                for (int i = 0; i < 1536; i++)
                {
                    vector[i] = (float)(random.NextDouble() * 2 - 1);
                }
                
                Normalize(vector);
            }
            
            return vector;
        }

        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            if (_requestRateLimiter != null)
            {
                return await _requestRateLimiter.EnqueueRequest(async () =>
                {
                    return await DoGenerateEmbeddingAsync(text);
                });
            }
            return await DoGenerateEmbeddingAsync(text);
        }

        private async Task<float[]> DoGenerateEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new float[1536];
            }

            try
            {
                var apiKey = GetEmbeddingApiKey();
                var baseUrl = GetEmbeddingApiBaseUrl();
                var modelName = GetEmbeddingModelName();

                if (!string.IsNullOrEmpty(apiKey))
                {
                    InitializeOpenAIServiceIfNeeded(apiKey, baseUrl);
                    
                    if (_openAIService != null)
                    {
                        var request = new EmbeddingCreateRequest
                        {
                            Model = modelName,
                            Input = text
                        };

                        var result = await _openAIService.Embeddings.CreateEmbedding(request);
                        if (result.Successful)
                        {
                            var embedding = result.Data[0].Embedding;
                            if (embedding != null && embedding.Count > 0)
                            {
                                var floatArray = new float[embedding.Count];
                                for (int i = 0; i < embedding.Count; i++)
                                {
                                    floatArray[i] = (float)embedding[i];
                                }
                                Logger.LogInfo("EMBEDDING", $"Successfully generated embedding for text, dimension: {floatArray.Length}");
                                return floatArray;
                            }
                        }
                        else
                        {
                            Logger.LogError("EMBEDDING", $"Failed to generate embedding: {result.Error?.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("EMBEDDING", $"Error generating embedding: {ex.Message}", ex);
            }

            Logger.LogWarning("EMBEDDING", "Falling back to pseudo-random embedding");
            return GenerateFallbackEmbedding(text);
        }

        private void Normalize(float[] vector)
        {
            float sum = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }
            
            float norm = (float)Math.Sqrt(sum);
            if (norm > 0)
            {
                for (int i = 0; i < vector.Length; i++)
                {
                    vector[i] /= norm;
                }
            }
        }

        public float CosineSimilarity(float[] a, float[] b)
        {
            if (a == null || b == null)
            {
                Logger.LogWarning("EMBEDDING", "CosineSimilarity: One or both vectors are null");
                return 0;
            }

            if (a.Length != b.Length)
            {
                Logger.LogWarning("EMBEDDING", $"CosineSimilarity: Vector dimension mismatch! Query vector: {a.Length}, Stored vector: {b.Length}");
                return 0;
            }

            float dotProduct = 0;
            float normA = 0;
            float normB = 0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0 || normB == 0)
            {
                Logger.LogWarning("EMBEDDING", "CosineSimilarity: One or both vectors have zero norm");
                return 0;
            }

            float similarity = dotProduct / (float)(Math.Sqrt(normA) * Math.Sqrt(normB));
            return similarity;
        }
    }
}
