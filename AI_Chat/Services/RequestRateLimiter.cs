using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AI_Chat.Services
{
    public class RequestRateLimiter
    {
        private readonly ConfigManager _configManager;
        private readonly ConcurrentQueue<Func<Task>> _requestQueue = new ConcurrentQueue<Func<Task>>();
        private readonly List<DateTime> _requestHistory = new List<DateTime>();
        private readonly object _historyLock = new object();
        private bool _isProcessing = false;
        private readonly SemaphoreSlim _queueSemaphore = new SemaphoreSlim(1, 1);
        private int _queueCount = 0;

        public event Action<int> OnQueueCountChanged;

        public int QueueCount
        {
            get => _queueCount;
            private set
            {
                if (_queueCount != value)
                {
                    _queueCount = value;
                    OnQueueCountChanged?.Invoke(value);
                }
            }
        }

        public RequestRateLimiter(ConfigManager configManager)
        {
            _configManager = configManager;
        }

        public async Task<T> EnqueueRequest<T>(Func<Task<T>> requestFunc)
        {
            var tcs = new TaskCompletionSource<T>();
            
            QueueCount++;
            
            _requestQueue.Enqueue(async () =>
            {
                try
                {
                    await WaitForRateLimitAsync();
                    var result = await requestFunc();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
                finally
                {
                    QueueCount--;
                }
            });

            _ = ProcessQueueAsync();
            return await tcs.Task;
        }

        public async Task EnqueueRequest(Func<Task> requestFunc)
        {
            await EnqueueRequest(async () =>
            {
                await requestFunc();
                return true;
            });
        }

        private async Task ProcessQueueAsync()
        {
            await _queueSemaphore.WaitAsync();
            try
            {
                if (_isProcessing) return;
                _isProcessing = true;

                while (_requestQueue.TryDequeue(out var request))
                {
                    await request();
                }
            }
            finally
            {
                _isProcessing = false;
                _queueSemaphore.Release();
            }
        }

        private async Task WaitForRateLimitAsync()
        {
            int timeWindow = _configManager.Config.RateLimitTimeWindow;
            int maxRequests = _configManager.Config.RateLimitMaxRequests;

            while (true)
            {
                DateTime now = DateTime.Now;
                DateTime windowStart = now.AddSeconds(-timeWindow);

                lock (_historyLock)
                {
                    _requestHistory.RemoveAll(t => t < windowStart);

                    if (_requestHistory.Count < maxRequests)
                    {
                        _requestHistory.Add(now);
                        return;
                    }
                }

                DateTime oldestRequest;
                lock (_historyLock)
                {
                    oldestRequest = _requestHistory[0];
                }

                TimeSpan waitTime = oldestRequest.AddSeconds(timeWindow) - now;
                if (waitTime.TotalMilliseconds > 0)
                {
                    await Task.Delay((int)waitTime.TotalMilliseconds + 10);
                }
            }
        }

        public void ClearQueue()
        {
            while (_requestQueue.TryDequeue(out _))
            {
                QueueCount = Math.Max(0, QueueCount - 1);
            }
        }

        public int GetRequestCountInWindow()
        {
            int timeWindow = _configManager.Config.RateLimitTimeWindow;
            DateTime now = DateTime.Now;
            DateTime windowStart = now.AddSeconds(-timeWindow);

            lock (_historyLock)
            {
                _requestHistory.RemoveAll(t => t < windowStart);
                return _requestHistory.Count;
            }
        }
    }
}
