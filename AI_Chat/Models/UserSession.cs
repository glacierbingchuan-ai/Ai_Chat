using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace AI_Chat.Models
{
    using Timer = System.Threading.Timer;
    public class UserSession
    {
        public long UserId { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime LastActiveTime { get; set; }

        private readonly object _inputStateLock = new object();
        private readonly object _ctsLock = new object();
        private readonly object _processedMessagesLock = new object();

        private UserInputState _userInputState = new UserInputState();
        private CancellationTokenSource _masterCts;
        private HashSet<string> _processedMessages = new HashSet<string>();
        private string _latestHandlerId = "";
        private Timer _incompleteTimeoutTimer;

        private int _totalMessages = 0;
        private int _proactiveChats = 0;
        private int _reminders = 0;

        public UserSession(long userId)
        {
            UserId = userId;
            CreatedAt = DateTime.Now;
            LastActiveTime = DateTime.Now;
        }

        public UserInputState InputState
        {
            get
            {
                lock (_inputStateLock) return _userInputState;
            }
        }

        public CancellationTokenSource MasterCts
        {
            get
            {
                lock (_ctsLock) return _masterCts;
            }
            set
            {
                lock (_ctsLock) _masterCts = value;
            }
        }

        public string LatestHandlerId
        {
            get
            {
                lock (_inputStateLock) return _latestHandlerId;
            }
            set
            {
                lock (_inputStateLock) _latestHandlerId = value;
            }
        }

        public Timer IncompleteTimeoutTimer
        {
            get => _incompleteTimeoutTimer;
            set => _incompleteTimeoutTimer = value;
        }

        public object InputStateLock => _inputStateLock;
        public object CtsLock => _ctsLock;
        public object ProcessedMessagesLock => _processedMessagesLock;

        public int TotalMessages => _totalMessages;
        public int ProactiveChats => _proactiveChats;
        public int Reminders => _reminders;

        public void IncrementTotalMessages() => _totalMessages++;
        public void IncrementProactiveChats() => _proactiveChats++;
        public void IncrementReminders() => _reminders++;

        public bool TryAddProcessedMessage(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) return true;
            lock (_processedMessagesLock)
            {
                return _processedMessages.Add(messageId);
            }
        }

        public void ClearAccumulatedMessage()
        {
            lock (_inputStateLock)
            {
                _userInputState.AccumulatedMessage.Clear();
            }
        }

        public void AppendToAccumulatedMessage(string content)
        {
            lock (_inputStateLock)
            {
                if (_userInputState.AccumulatedMessage.Length > 0)
                    _userInputState.AccumulatedMessage.Append(" ");
                _userInputState.AccumulatedMessage.Append(content);
                _userInputState.LastMessageTime = DateTime.Now;
            }
        }

        public string GetAndClearAccumulatedMessage()
        {
            lock (_inputStateLock)
            {
                string result = _userInputState.AccumulatedMessage.ToString().Trim();
                _userInputState.AccumulatedMessage.Clear();
                _incompleteTimeoutTimer?.Dispose();
                _incompleteTimeoutTimer = null;
                return result;
            }
        }

        public string GetAccumulatedMessage()
        {
            lock (_inputStateLock)
            {
                return _userInputState.AccumulatedMessage.ToString();
            }
        }

        public void CancelMasterCts()
        {
            lock (_ctsLock)
            {
                _masterCts?.Cancel();
                _masterCts = null;
            }
        }

        public void DisposeTimer()
        {
            _incompleteTimeoutTimer?.Dispose();
            _incompleteTimeoutTimer = null;
        }

        public SessionStats GetStats()
        {
            return new SessionStats
            {
                UserId = UserId,
                TotalMessages = _totalMessages,
                ProactiveChats = _proactiveChats,
                Reminders = _reminders,
                LastActiveTime = LastActiveTime
            };
        }
    }

    public class SessionStats
    {
        public long UserId { get; set; }
        public int TotalMessages { get; set; }
        public int ProactiveChats { get; set; }
        public int Reminders { get; set; }
        public DateTime LastActiveTime { get; set; }
    }
}
