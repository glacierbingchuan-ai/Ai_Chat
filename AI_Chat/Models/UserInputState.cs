using System;
using System.Text;

namespace AI_Chat.Models
{
    public class UserInputState
    {
        public StringBuilder AccumulatedMessage { get; set; } = new StringBuilder();
        public DateTime LastMessageTime { get; set; } = DateTime.Now;
    }
}
