using UnityEngine;
using UnityEngine.InputSystem;
using MVP.Conversation;

public class NewUserSessionInputHandler : MonoBehaviour
{
    [SerializeField] private OpenAIConversationController conversationController;
    [SerializeField] private InputActionProperty newUserAction;

    private void OnEnable()
    {
        if (newUserAction.action != null)
        {
            newUserAction.action.performed += OnNewUser;
            newUserAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (newUserAction.action != null)
        {
            newUserAction.action.performed -= OnNewUser;
            newUserAction.action.Disable();
        }
    }

    private void OnNewUser(InputAction.CallbackContext ctx)
    {
        if (conversationController != null)
        {
            conversationController.StartNewAnonymousUserSession();
        }
    }
}