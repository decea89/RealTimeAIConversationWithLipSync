using UnityEngine;
using UnityEngine.UI;

namespace MVP.Conversation
{
    public class NewUserSessionButtonBinder : MonoBehaviour
    {
        [SerializeField] private Button newUserButton;
        [SerializeField] private OpenAIConversationController conversationController;

        private void Awake()
        {
            if (newUserButton != null)
            {
                newUserButton.onClick.AddListener(OnNewUserClicked);
            }
        }

        private void OnDestroy()
        {
            if (newUserButton != null)
            {
                newUserButton.onClick.RemoveListener(OnNewUserClicked);
            }
        }

        private void OnNewUserClicked()
        {
            if (conversationController != null)
            {
                conversationController.StartNewAnonymousUserSession();
            }
        }
    }
}
