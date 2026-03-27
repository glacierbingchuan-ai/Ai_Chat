using System.Collections.Generic;

namespace AI_Chat.Models
{
    public class GetDueEventsResult
    {
        public List<EventModel> DueEvents { get; set; }
        public bool EventsUpdated { get; set; }
    }
}
