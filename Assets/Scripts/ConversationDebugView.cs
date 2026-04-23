using TMPro;
using UnityEngine;

namespace MVP.Conversation
{
    public class ConversationDebugView : MonoBehaviour
    {
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text debugText;

        public void SetState(string value)
        {
            if (stateText != null)
                stateText.text = value;
        }

        public void SetDebug(string value)
        {
            if (debugText != null)
                debugText.text = value;
        }

        public void SetMessage(string value)
        {
            SetDebug(value);
        }
    }
}