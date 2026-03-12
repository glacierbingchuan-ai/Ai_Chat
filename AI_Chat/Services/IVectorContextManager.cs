using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Chat.Models;

namespace AI_Chat.Services
{
    /// <summary>
    /// 向量上下文管理器接口 - 扩展基础接口，提供向量数据库功能
    /// </summary>
    public interface IVectorContextManager : IContextManager
    {
        List<VectorEntry> VectorEntries { get; }

        /// <summary>
        /// 添加向量条目
        /// </summary>
        Task AddVectorEntryAsync(string content, string role);

        /// <summary>
        /// 添加助手消息并生成向量
        /// </summary>
        Task AddAssistantMessageWithVectorAsync(string content);

        /// <summary>
        /// 获取分页的向量条目
        /// </summary>
        (List<VectorEntry> Entries, int TotalCount) GetVectorEntriesPaged(int page, int pageSize);

        /// <summary>
        /// 搜索相似向量
        /// </summary>
        List<VectorEntry> SearchSimilar(string query, int topK = 5, float similarityThreshold = 0.2f);

        /// <summary>
        /// 删除指定ID的向量条目
        /// </summary>
        void DeleteVectorEntry(string id);

        /// <summary>
        /// 根据内容删除向量条目
        /// </summary>
        void DeleteVectorEntryByContent(string content);

        /// <summary>
        /// 清空所有向量
        /// </summary>
        void ClearVectors();

        /// <summary>
        /// 重新生成所有向量
        /// </summary>
        Task RegenerateAllVectorsAsync();
    }
}
