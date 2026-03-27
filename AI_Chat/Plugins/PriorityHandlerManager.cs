using System;
using System.Collections.Generic;
using System.Linq;
using AI_Chat.Services;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 优先级处理器管理器 - 通用实现
    /// </summary>
    public class PriorityHandlerManager<TContext, TResult>
    {
        private readonly List<PriorityHandler<TContext, TResult>> _handlers = new List<PriorityHandler<TContext, TResult>>();
        private readonly object _lock = new object();
        private readonly string _handlerName;

        public PriorityHandlerManager(string handlerName)
        {
            _handlerName = handlerName;
        }

        /// <summary>
        /// 注册处理器，按优先级插入（保持有序，避免每次排序）
        /// </summary>
        public void Register(Func<TContext, TResult> handler, string pluginId, int priority)
        {
            if (handler == null) return;

            lock (_lock)
            {
                var newHandler = new PriorityHandler<TContext, TResult>
                {
                    PluginId = pluginId,
                    Priority = priority,
                    Handler = handler
                };

                // 使用二分查找找到插入位置，保持有序
                int index = FindInsertIndex(priority);
                _handlers.Insert(index, newHandler);
            }
        }

        /// <summary>
        /// 二分查找插入位置
        /// </summary>
        private int FindInsertIndex(int priority)
        {
            int left = 0, right = _handlers.Count;
            while (left < right)
            {
                int mid = (left + right) / 2;
                if (_handlers[mid].Priority <= priority)
                    left = mid + 1;
                else
                    right = mid;
            }
            return left;
        }

        /// <summary>
        /// 注销指定插件的所有处理器
        /// </summary>
        public void Unregister(string pluginId)
        {
            if (string.IsNullOrEmpty(pluginId)) return;

            lock (_lock)
            {
                _handlers.RemoveAll(h => h.PluginId == pluginId);
            }
        }

        /// <summary>
        /// 获取所有处理器（用于遍历执行）
        /// </summary>
        public List<PriorityHandler<TContext, TResult>> GetHandlers()
        {
            lock (_lock)
            {
                return _handlers.ToList();
            }
        }

        /// <summary>
        /// 清空所有处理器
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _handlers.Clear();
            }
        }
    }

    /// <summary>
    /// 优先级处理器包装类
    /// </summary>
    public class PriorityHandler<TContext, TResult>
    {
        public int Priority { get; set; }
        public Func<TContext, TResult> Handler { get; set; }
        public string PluginId { get; set; }
    }
}
