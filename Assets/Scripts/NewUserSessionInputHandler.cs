using UnityEngine;
using UnityEngine.InputSystem;
using MVP.Conversation;
using System.Collections.Generic;

public class NewUserSessionInputHandler : MonoBehaviour
{
    [SerializeField] private OpenAIConversationController conversationController;
    [SerializeField] private InputActionProperty newUserAction;
    [SerializeField] private InputActionProperty toggleMenusAction;
    [SerializeField] private List<GameObject> menuRoots = new List<GameObject>();
    [SerializeField] private bool useRightSecondaryButtonFallback = true;

    private bool previousRightSecondaryPressed;

    private void OnEnable()
    {
        if (newUserAction.action != null)
        {
            newUserAction.action.performed += OnNewUser;
            newUserAction.action.Enable();
        }

        if (toggleMenusAction.action != null)
        {
            toggleMenusAction.action.performed += OnToggleMenus;
            toggleMenusAction.action.Enable();
        }
    }

    private void OnDisable()
    {
        if (newUserAction.action != null)
        {
            newUserAction.action.performed -= OnNewUser;
            newUserAction.action.Disable();
        }

        if (toggleMenusAction.action != null)
        {
            toggleMenusAction.action.performed -= OnToggleMenus;
            toggleMenusAction.action.Disable();
        }

        previousRightSecondaryPressed = false;
    }

    private void Update()
    {
        if (!useRightSecondaryButtonFallback)
            return;

        UnityEngine.XR.InputDevice rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
        if (!rightHand.isValid)
            return;

        bool isPressed = false;
        if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out isPressed))
        {
            if (isPressed && !previousRightSecondaryPressed)
                ToggleAllMenus();

            previousRightSecondaryPressed = isPressed;
        }
    }

    private void OnNewUser(InputAction.CallbackContext ctx)
    {
        if (conversationController != null)
        {
            conversationController.StartNewAnonymousUserSession();
        }
    }

    private void OnToggleMenus(InputAction.CallbackContext ctx)
    {
        ToggleAllMenus();
    }

    private void ToggleAllMenus()
    {
        if (menuRoots == null || menuRoots.Count == 0)
            return;

        bool anyVisible = false;
        for (int i = 0; i < menuRoots.Count; i++)
        {
            if (menuRoots[i] != null && menuRoots[i].activeSelf)
            {
                anyVisible = true;
                break;
            }
        }

        bool nextVisible = !anyVisible;
        for (int i = 0; i < menuRoots.Count; i++)
        {
            if (menuRoots[i] != null)
                menuRoots[i].SetActive(nextVisible);
        }
    }
}