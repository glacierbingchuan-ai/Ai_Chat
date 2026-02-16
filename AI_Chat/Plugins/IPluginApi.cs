using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AI_Chat.Plugins
{
    /// <summary>
    /// 插件API接口 - 提供给插件调用的核心功能
    /// </summary>
    public interface IPluginApi
    {
        #region 1. 合并前用户消息接口（可拦截，可修改）
        /// <summary>
        /// 注册合并前消息处理器
        /// </summary>
        void RegisterPreMergeMessageHandler(Func<PreMergeMessageContext, PreMergeMessageResult> handler);
        #endregion

        #region 2. 合并后用户消息接口（可拦截，可修改）
        /// <summary>
        /// 注册合并后消息处理器
        /// </summary>
        void RegisterPostMergeMessageHandler(Func<PostMergeMessageContext, PostMergeMessageResult> handler);
        #endregion

        #region 2.5 消息追加完成接口（可修改追加后的消息）
        /// <summary>
        /// 注册消息追加完成处理器（当消息被追加到上一条用户消息时触发）
        /// </summary>
        void RegisterMessageAppendedHandler(Func<MessageAppendedContext, MessageAppendedResult> handler);
        #endregion

        #region 3. 大模型回复消息接口（可拦截，可修改）
        /// <summary>
        /// 注册大模型回复处理器（格式审查前）
        /// </summary>
        void RegisterLLMResponseHandler(Func<LLMResponseContext, LLMResponseResult> handler);
        #endregion

        #region 4. 大模型请求接口（插件自己生成JSON）
        /// <summary>
        /// 请求大模型（插件自己构建请求JSON）
        /// </summary>
        /// <param name="requestJson">插件生成的请求JSON</param>
        /// <returns>大模型原始响应JSON</returns>
        Task<string> RequestLLMAsync(string requestJson);
        #endregion

        #region 5. 获取当前软件设置接口
        /// <summary>
        /// 获取当前软件配置
        /// </summary>
        AppConfig GetConfig();
        #endregion

        #region 6. 修改当前软件设置接口
        /// <summary>
        /// 修改软件配置
        /// </summary>
        void SetConfig(AppConfig config);

        /// <summary>
        /// 获取配置项
        /// </summary>
        T GetConfigValue<T>(string key, T defaultValue = default);

        /// <summary>
        /// 设置配置项
        /// </summary>
        void SetConfigValue<T>(string key, T value);
        #endregion

        #region 7. 发送消息接口
        /// <summary>
        /// 发送消息
        /// </summary>
        /// <param name="message">消息内容（文字或文件路径）</param>
        /// <param name="options">发送选项</param>
        Task<bool> SendMessageAsync(string message, SendMessageOptions options = null);
        #endregion

        #region 8. 当前完整上下文获取接口
        /// <summary>
        /// 获取当前完整上下文
        /// </summary>
        List<ContextMessage> GetFullContext();
        #endregion

        #region 9. 上下文写入接口
        /// <summary>
        /// 添加消息到上下文（仅添加到LLM上下文，不会触发前端显示）
        /// </summary>
        /// <param name="role">角色：user, assistant, system</param>
        /// <param name="content">消息内容</param>
        void AddContextMessage(string role, string content);

        /// <summary>
        /// 清空上下文
        /// </summary>
        void ClearContext();

        /// <summary>
        /// 删除指定角色的最后N条消息
        /// </summary>
        /// <param name="role">角色：user, assistant, system</param>
        /// <param name="count">删除数量</param>
        /// <returns>实际删除的数量</returns>
        int RemoveLastMessages(string role, int count);
        #endregion

        #region 10. 权限相关接口
        /// <summary>
        /// 获取当前插件已注册的所有权限
        /// </summary>
        List<string> GetRegisteredPermissions();

        /// <summary>
        /// 获取指定插件的权限列表
        /// </summary>
        /// <param name="pluginId">插件ID</param>
        /// <returns>权限列表</returns>
        List<string> GetPluginPermissions(string pluginId);

        /// <summary>
        /// 获取所有插件的权限信息
        /// </summary>
        /// <returns>插件ID到权限列表的映射</returns>
        Dictionary<string, List<string>> GetAllPluginPermissions();
        #endregion
    }

    #region 消息处理上下文和结果类

    /// <summary>
    /// 合并前消息上下文
    /// </summary>
    public class PreMergeMessageContext
    {
        /// <summary>
        /// 原始消息内容
        /// </summary>
        public string RawMessage { get; set; }

        /// <summary>
        /// 发送者ID
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// 消息时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// 合并前消息处理结果
    /// </summary>
    public class PreMergeMessageResult
    {
        /// <summary>
        /// 是否拦截（不继续处理）
        /// </summary>
        public bool IsIntercepted { get; set; }

        /// <summary>
        /// 拦截时的响应内容（直接回复给用户）
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// 修改后的消息内容（继续处理）
        /// </summary>
        public string ModifiedMessage { get; set; }

        /// <summary>
        /// 是否修改了消息
        /// </summary>
        public bool IsModified { get; set; }
    }

    /// <summary>
    /// 合并后消息上下文
    /// </summary>
    public class PostMergeMessageContext
    {
        /// <summary>
        /// 完整消息内容
        /// </summary>
        public string FullMessage { get; set; }

        /// <summary>
        /// 发送者ID
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// 消息时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 消息片段列表
        /// </summary>
        public List<string> MessageFragments { get; set; }
    }

    /// <summary>
    /// 合并后消息处理结果
    /// </summary>
    public class PostMergeMessageResult
    {
        /// <summary>
        /// 是否拦截（不交给LLM）
        /// </summary>
        public bool IsIntercepted { get; set; }

        /// <summary>
        /// 拦截时的响应内容（直接回复给用户）
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// 修改后的消息内容（交给LLM）
        /// </summary>
        public string ModifiedMessage { get; set; }

        /// <summary>
        /// 是否修改了消息
        /// </summary>
        public bool IsModified { get; set; }
    }

    /// <summary>
    /// 消息追加完成上下文（当消息被追加到上一条用户消息时触发）
    /// </summary>
    public class MessageAppendedContext
    {
        /// <summary>
        /// 追加前的完整消息内容
        /// </summary>
        public string OriginalMessage { get; set; }

        /// <summary>
        /// 追加的新消息内容
        /// </summary>
        public string AppendedContent { get; set; }

        /// <summary>
        /// 追加后的完整消息内容
        /// </summary>
        public string FullMessage { get; set; }

        /// <summary>
        /// 消息在上下文中的索引
        /// </summary>
        public int MessageIndex { get; set; }
    }

    /// <summary>
    /// 消息追加完成处理结果
    /// </summary>
    public class MessageAppendedResult
    {
        /// <summary>
        /// 是否拦截（不继续处理追加的消息）
        /// </summary>
        public bool IsIntercepted { get; set; }

        /// <summary>
        /// 拦截时的响应内容（直接回复给用户）
        /// </summary>
        public string Response { get; set; }

        /// <summary>
        /// 修改后的完整消息内容
        /// </summary>
        public string ModifiedMessage { get; set; }

        /// <summary>
        /// 是否修改了消息
        /// </summary>
        public bool IsModified { get; set; }
    }

    /// <summary>
    /// 大模型回复上下文
    /// </summary>
    public class LLMResponseContext
    {
        /// <summary>
        /// 原始响应内容（JSON或文本）
        /// </summary>
        public string RawResponse { get; set; }

        /// <summary>
        /// 请求ID
        /// </summary>
        public string RequestId { get; set; }
    }

    /// <summary>
    /// 大模型回复处理结果
    /// </summary>
    public class LLMResponseResult
    {
        /// <summary>
        /// 是否拦截（不发送给用户）
        /// </summary>
        public bool IsIntercepted { get; set; }

        /// <summary>
        /// 拦截或修改时的替代响应（JSON字符串）
        /// </summary>
        public string AlternativeResponse { get; set; }

        /// <summary>
        /// 是否修改了响应
        /// </summary>
        public bool IsModified { get; set; }
    }

    #endregion

    #region 配置和消息类

    /// <summary>
    /// 发送消息选项
    /// </summary>
    public class SendMessageOptions
    {
        /// <summary>
        /// 目标QQ号（null表示使用默认）
        /// </summary>
        public long? TargetUserId { get; set; }

        /// <summary>
        /// 消息类型
        /// </summary>
        public MessageType MessageType { get; set; } = MessageType.Text;
    }

    /// <summary>
    /// 消息类型
    /// </summary>
    public enum MessageType
    {
        Text,
        Image,
        Voice
    }

    /// <summary>
    /// 软件配置
    /// </summary>
    public class AppConfig
    {
        // LLM 配置
        public string ApiKey { get; set; }
        public string ApiUrl { get; set; }
        public string Model { get; set; }
        public float Temperature { get; set; }
        public int MaxTokens { get; set; }
        public float TopP { get; set; }

        // WebSocket 配置
        public string WebsocketServerUri { get; set; }
        public string WebsocketToken { get; set; }
        public int WebsocketKeepAliveInterval { get; set; }

        // 功能开关
        public bool IntentAnalysisEnabled { get; set; }
        public bool ProactiveChatEnabled { get; set; }
        public bool ReminderEnabled { get; set; }

        // 聊天配置
        public long TargetUserId { get; set; }
        public int MaxContextRounds { get; set; }
        public int ActiveChatProbability { get; set; }

        // 日志配置
        public string LogRootFolder { get; set; }
        public string GeneralLogSubfolder { get; set; }
        public string ContextLogSubfolder { get; set; }

        // 提示词配置
        public string BaseSystemPrompt { get; set; }
        public string IncompleteInputPrompt { get; set; }

        // 角色卡配置
        public string RoleCardsApiUrl { get; set; }
    }

    /// <summary>
    /// 上下文消息
    /// </summary>
    public class ContextMessage
    {
        /// <summary>
        /// 角色：user, assistant, system
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// 消息内容
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// 时间戳
        /// </summary>
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// 消息类型标记
        /// </summary>
        public string Tag { get; set; }
    }

    #endregion
}
