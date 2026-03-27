using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AI_Chat.Models;
using AI_Chat.Utils;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;

namespace AI_Chat.Services
{
    public class DatabaseService : IDisposable
    {
        private readonly string _dbPath;
        private SqliteConnection _connection;
        private bool _disposed;

        public DatabaseService(long userId = 0)
        {
            if (userId > 0)
            {
                _dbPath = PathUtils.GetUserDatabasePath(userId);
            }
            else
            {
                _dbPath = PathUtils.GetGlobalDatabasePath();
            }

            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            _connection = new SqliteConnection($"Data Source={_dbPath}");
            _connection.Open();

            CreateTables();
        }

        private void CreateTables()
        {
            // 聊天记录表（用于前端显示）
            string createMessagesTable = @"
                CREATE TABLE IF NOT EXISTS Messages (
                    Id TEXT PRIMARY KEY,
                    Role TEXT NOT NULL,
                    Content TEXT,
                    Meme TEXT,
                    Timestamp DATETIME NOT NULL,
                    UserId INTEGER NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

            // 上下文消息表（用于AI对话上下文，支持压缩）
            string createContextMessagesTable = @"
                CREATE TABLE IF NOT EXISTS ContextMessages (
                    Id TEXT PRIMARY KEY,
                    Role TEXT NOT NULL,
                    Content TEXT,
                    Timestamp DATETIME NOT NULL,
                    UserId INTEGER NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                )";

            string createVectorEntriesTable = @"
                CREATE TABLE IF NOT EXISTS VectorEntries (
                    Id TEXT PRIMARY KEY,
                    Content TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    UserId INTEGER NOT NULL,
                    Timestamp DATETIME NOT NULL,
                    Vector BLOB NOT NULL,
                    Metadata TEXT
                )";

            string createEventsTable = @"
                CREATE TABLE IF NOT EXISTS Events (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Time TEXT NOT NULL
                )";

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = createMessagesTable;
                command.ExecuteNonQuery();

                command.CommandText = createContextMessagesTable;
                command.ExecuteNonQuery();

                command.CommandText = createVectorEntriesTable;
                command.ExecuteNonQuery();

                command.CommandText = createEventsTable;
                command.ExecuteNonQuery();
            }

            CreateIndexes();
        }

        private void CreateIndexes()
        {
            string createUserTimeIndex = @"
                CREATE INDEX IF NOT EXISTS idx_messages_user_time 
                ON Messages(UserId, Timestamp DESC)";

            string createUserIdIndex = @"
                CREATE INDEX IF NOT EXISTS idx_messages_user_id 
                ON Messages(UserId, Id DESC)";

            using (var command = _connection.CreateCommand())
            {
                command.CommandText = createUserTimeIndex;
                command.ExecuteNonQuery();

                command.CommandText = createUserIdIndex;
                command.ExecuteNonQuery();
            }
        }

        #region ChatMessage Operations

        public void SaveChatMessage(ChatMessage message, long userId)
        {
            try
            {
                using (var insertCommand = _connection.CreateCommand())
                {
                    insertCommand.CommandText = @"
                        INSERT INTO Messages (Id, Role, Content, Meme, Timestamp, UserId)
                        VALUES (@id, @role, @content, @meme, @timestamp, @userId)";
                    
                    insertCommand.Parameters.AddWithValue("@id", message.Id);
                    insertCommand.Parameters.AddWithValue("@role", message.Role);
                    insertCommand.Parameters.AddWithValue("@content", message.Content ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@meme", message.Meme ?? (object)DBNull.Value);
                    insertCommand.Parameters.AddWithValue("@timestamp", DateTime.Parse(message.Timestamp));
                    insertCommand.Parameters.AddWithValue("@userId", userId);
                    
                    int rowsAffected = insertCommand.ExecuteNonQuery();
                    Logger.LogInfo("DB", $"Saved message {message.Id} for user {userId}, rows affected: {rowsAffected}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("DB", $"Failed to save message for user {userId}: {ex.Message}");
                throw;
            }
        }

        public List<ChatMessage> LoadChatMessages(long userId, int limit = 1000)
        {
            var messages = new List<ChatMessage>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = @"
                    SELECT Id, Role, Content, Meme, Timestamp 
                    FROM Messages 
                    WHERE UserId = @userId 
                    ORDER BY Timestamp ASC 
                    LIMIT @limit";
                command.Parameters.AddWithValue("@userId", userId);
                command.Parameters.AddWithValue("@limit", limit);
                
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // 处理 Timestamp 字段，兼容字符串和 DateTime 类型
                        string timestampStr;
                        var timestampValue = reader.GetValue(4);
                        if (timestampValue is DateTime dateTime)
                        {
                            timestampStr = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        else
                        {
                            timestampStr = timestampValue?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        }

                        messages.Add(new ChatMessage
                        {
                            Id = reader.GetString(0),
                            Role = reader.GetString(1),
                            Content = reader.IsDBNull(2) ? null : reader.GetString(2),
                            Meme = reader.IsDBNull(3) ? null : reader.GetString(3),
                            Timestamp = timestampStr
                        });
                    }
                }
            }
            return messages;
        }

        public (List<ChatMessage> messages, bool hasMore) GetChatMessagesPaged(long userId, string beforeId = null, DateTime? beforeTime = null, int limit = 20)
        {
            var messages = new List<ChatMessage>();
            DateTime? oldestTimestamp = null;
            
            try
            {
                using (var command = _connection.CreateCommand())
                {
                    // 如果提供了beforeId，先获取该消息的时间戳
                    if (!string.IsNullOrEmpty(beforeId) && !beforeTime.HasValue)
                    {
                        using (var timeCommand = _connection.CreateCommand())
                        {
                            timeCommand.CommandText = "SELECT Timestamp FROM Messages WHERE Id = @id AND UserId = @userId";
                            timeCommand.Parameters.AddWithValue("@id", beforeId);
                            timeCommand.Parameters.AddWithValue("@userId", userId);
                            var result = timeCommand.ExecuteScalar();
                            if (result != null && result != DBNull.Value)
                            {
                                // 兼容字符串和 DateTime 类型
                                if (result is DateTime dt)
                                {
                                    oldestTimestamp = dt;
                                }
                                else if (DateTime.TryParse(result.ToString(), out DateTime parsedDt))
                                {
                                    oldestTimestamp = parsedDt;
                                }
                            }
                        }
                    }
                    else if (beforeTime.HasValue)
                    {
                        oldestTimestamp = beforeTime;
                    }
                    
                    // 查询比指定时间更早的消息（倒序排列，最新的在前面）
                    if (oldestTimestamp.HasValue)
                    {
                        command.CommandText = @"
                            SELECT Id, Role, Content, Meme, Timestamp 
                            FROM Messages 
                            WHERE UserId = @userId AND Timestamp < @beforeTime
                            ORDER BY Timestamp DESC 
                            LIMIT @limitPlusOne";
                        command.Parameters.AddWithValue("@beforeTime", oldestTimestamp.Value);
                    }
                    else
                    {
                        // 首次查询，获取最新的消息
                        command.CommandText = @"
                            SELECT Id, Role, Content, Meme, Timestamp 
                            FROM Messages 
                            WHERE UserId = @userId 
                            ORDER BY Timestamp DESC 
                            LIMIT @limitPlusOne";
                    }
                    
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@limitPlusOne", limit + 1); // 多查一条用于判断是否有更多
                    
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // 处理 Timestamp 字段，兼容字符串和 DateTime 类型
                            string timestampStr;
                            var timestampValue = reader.GetValue(4);
                            if (timestampValue is DateTime dateTime)
                            {
                                timestampStr = dateTime.ToString("yyyy-MM-dd HH:mm:ss");
                            }
                            else
                            {
                                timestampStr = timestampValue?.ToString() ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                            }

                            messages.Add(new ChatMessage
                            {
                                Id = reader.GetString(0),
                                Role = reader.GetString(1),
                                Content = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Meme = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Timestamp = timestampStr
                            });
                        }
                    }
                }
                
                // 判断是否还有更多消息
                bool hasMore = messages.Count > limit;
                if (hasMore)
                {
                    messages.RemoveAt(messages.Count - 1); // 移除多查的那一条
                }
                
                // 将消息按时间正序排列（旧的在前面，方便前端显示）
                messages.Reverse();
                
                Logger.LogInfo("DB", $"Loaded {messages.Count} messages for user {userId}, hasMore: {hasMore}");
                return (messages, hasMore);
            }
            catch (Exception ex)
            {
                Logger.LogError("DB", $"Failed to load messages for user {userId}: {ex.Message}");
                throw;
            }
        }

        public int GetChatMessageCount(long userId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT COUNT(*) FROM Messages WHERE UserId = @userId";
                command.Parameters.AddWithValue("@userId", userId);
                return Convert.ToInt32(command.ExecuteScalar());
            }
        }

        public void ClearChatMessages(long userId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Messages WHERE UserId = @userId";
                command.Parameters.AddWithValue("@userId", userId);
                command.ExecuteNonQuery();
            }
        }

        public void DeleteChatMessage(string messageId, long userId)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM Messages WHERE Id = @id AND UserId = @userId";
                command.Parameters.AddWithValue("@id", messageId);
                command.Parameters.AddWithValue("@userId", userId);
                command.ExecuteNonQuery();
            }
        }

        #endregion

        #region ContextMessages Operations (AI上下文消息)

        public void SaveContextMessages(List<Message> messages, long userId = 0)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                // 删除该用户的所有上下文消息
                using (var deleteCommand = _connection.CreateCommand())
                {
                    deleteCommand.CommandText = "DELETE FROM ContextMessages WHERE UserId = @userId";
                    deleteCommand.Parameters.AddWithValue("@userId", userId);
                    deleteCommand.ExecuteNonQuery();
                }

                foreach (var message in messages)
                {
                    using (var insertCommand = _connection.CreateCommand())
                    {
                        insertCommand.CommandText = "INSERT INTO ContextMessages (Id, Role, Content, Timestamp, UserId) VALUES (@id, @role, @content, @timestamp, @userId)";
                        insertCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                        insertCommand.Parameters.AddWithValue("@role", message.Role);
                        insertCommand.Parameters.AddWithValue("@content", message.Content);
                        insertCommand.Parameters.AddWithValue("@timestamp", message.Timestamp);
                        insertCommand.Parameters.AddWithValue("@userId", userId);
                        insertCommand.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        public List<Message> LoadContextMessages(long userId = 0)
        {
            var messages = new List<Message>();
            using (var command = _connection.CreateCommand())
            {
                // 按时间戳降序获取所有消息，然后反转保持正序（不限制数量，让压缩机制决定）
                command.CommandText = @"
                    SELECT Role, Content, Timestamp FROM (
                        SELECT Role, Content, Timestamp 
                        FROM ContextMessages 
                        WHERE UserId = @userId
                        ORDER BY Timestamp DESC 
                    ) AS subquery ORDER BY Timestamp ASC";
                command.Parameters.AddWithValue("@userId", userId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var timestampValue = reader.GetValue(2);
                        DateTime timestamp = timestampValue is DateTime dt 
                            ? dt 
                            : DateTime.TryParse(timestampValue?.ToString(), out var parsed) ? parsed : DateTime.Now;

                        messages.Add(new Message
                        {
                            Role = reader.IsDBNull(0) ? "" : reader.GetString(0),
                            Content = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Timestamp = timestamp
                        });
                    }
                }
            }
            return messages;
        }

        #endregion

        public void SaveVectorEntries(List<VectorEntry> entries)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                using (var deleteCommand = _connection.CreateCommand())
                {
                    deleteCommand.CommandText = "DELETE FROM VectorEntries";
                    deleteCommand.ExecuteNonQuery();
                }

                foreach (var entry in entries)
                {
                    using (var insertCommand = _connection.CreateCommand())
                    {
                        insertCommand.CommandText = @"
                            INSERT INTO VectorEntries (Id, Content, Role, UserId, Timestamp, Vector, Metadata)
                            VALUES (@id, @content, @role, @userId, @timestamp, @vector, @metadata)";
                        
                        insertCommand.Parameters.AddWithValue("@id", entry.Id);
                        insertCommand.Parameters.AddWithValue("@content", entry.Content);
                        insertCommand.Parameters.AddWithValue("@role", entry.Role);
                        insertCommand.Parameters.AddWithValue("@userId", entry.UserId);
                        insertCommand.Parameters.AddWithValue("@timestamp", entry.Timestamp);
                        insertCommand.Parameters.AddWithValue("@vector", VectorToBytes(entry.Vector));
                        insertCommand.Parameters.AddWithValue("@metadata", JsonConvert.SerializeObject(entry.Metadata));
                        
                        insertCommand.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        public List<VectorEntry> LoadVectorEntries()
        {
            var entries = new List<VectorEntry>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT Id, Content, Role, UserId, Timestamp, Vector, Metadata FROM VectorEntries";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        entries.Add(new VectorEntry
                        {
                            Id = reader.GetString(0),
                            Content = reader.GetString(1),
                            Role = reader.GetString(2),
                            UserId = reader.GetInt64(3),
                            Timestamp = reader.GetDateTime(4),
                            Vector = BytesToVector((byte[])reader["Vector"]),
                            Metadata = JsonConvert.DeserializeObject<Dictionary<string, object>>(reader.GetString(6))
                        });
                    }
                }
            }
            return entries;
        }

        public void SaveEvents(List<EventModel> events)
        {
            using (var transaction = _connection.BeginTransaction())
            {
                using (var deleteCommand = _connection.CreateCommand())
                {
                    deleteCommand.CommandText = "DELETE FROM Events";
                    deleteCommand.ExecuteNonQuery();
                }

                foreach (var ev in events)
                {
                    using (var insertCommand = _connection.CreateCommand())
                    {
                        insertCommand.CommandText = "INSERT INTO Events (Name, Time) VALUES (@name, @time)";
                        insertCommand.Parameters.AddWithValue("@name", ev.Name);
                        insertCommand.Parameters.AddWithValue("@time", ev.Time);
                        insertCommand.ExecuteNonQuery();
                    }
                }

                transaction.Commit();
            }
        }

        public List<EventModel> LoadEvents()
        {
            var events = new List<EventModel>();
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "SELECT Name, Time FROM Events ORDER BY Id";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        events.Add(new EventModel
                        {
                            Name = reader.GetString(0),
                            Time = reader.GetString(1)
                        });
                    }
                }
            }
            return events;
        }

        public void DeleteVectorEntry(string id)
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM VectorEntries WHERE Id = @id";
                command.Parameters.AddWithValue("@id", id);
                command.ExecuteNonQuery();
            }
        }

        public void ClearVectors()
        {
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM VectorEntries";
                command.ExecuteNonQuery();
            }
        }

        public void AddVectorEntry(VectorEntry entry)
        {
            using (var insertCommand = _connection.CreateCommand())
            {
                insertCommand.CommandText = @"
                    INSERT INTO VectorEntries (Id, Content, Role, UserId, Timestamp, Vector, Metadata)
                    VALUES (@id, @content, @role, @userId, @timestamp, @vector, @metadata)";
                
                insertCommand.Parameters.AddWithValue("@id", entry.Id);
                insertCommand.Parameters.AddWithValue("@content", entry.Content);
                insertCommand.Parameters.AddWithValue("@role", entry.Role);
                insertCommand.Parameters.AddWithValue("@userId", entry.UserId);
                insertCommand.Parameters.AddWithValue("@timestamp", entry.Timestamp);
                insertCommand.Parameters.AddWithValue("@vector", VectorToBytes(entry.Vector));
                insertCommand.Parameters.AddWithValue("@metadata", JsonConvert.SerializeObject(entry.Metadata));
                
                insertCommand.ExecuteNonQuery();
            }
        }

        private byte[] VectorToBytes(float[] vector)
        {
            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        private float[] BytesToVector(byte[] bytes)
        {
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return vector;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _connection?.Dispose();
                _disposed = true;
            }
        }
    }
}
