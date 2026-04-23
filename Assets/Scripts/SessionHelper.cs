using System;

namespace MVP.Conversation
{
    public static class SessionManager
    {
        public static string CurrentSessionId { get; private set; }

        public static void StartNewSession()
        {
            CurrentSessionId = Guid.NewGuid().ToString();
        }
    }
}