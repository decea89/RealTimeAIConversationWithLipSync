using System;

namespace MVP.Conversation
{
    public static class SessionManager
    {
        public static string CurrentSessionId { get; private set; }
        public static int CurrentUserIndex { get; private set; }
        public static int CurrentTurnIndex { get; private set; }

        public static bool HasActiveSession => !string.IsNullOrWhiteSpace(CurrentSessionId);

        public static void StartNewSession()
        {
            CurrentSessionId = Guid.NewGuid().ToString();
            CurrentUserIndex++;
            CurrentTurnIndex = 0;
        }

        public static void RegisterTurn()
        {
            if (!HasActiveSession)
            {
                StartNewSession();
            }

            CurrentTurnIndex++;
        }

        public static void ResetTurnCounter()
        {
            CurrentTurnIndex = 0;
        }

        public static void ClearSession()
        {
            CurrentSessionId = null;
            CurrentTurnIndex = 0;
        }
    }
}
