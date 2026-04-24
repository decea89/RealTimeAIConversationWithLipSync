using UnityEngine;

public class AvatarFrontPlacer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrCamera;
    [SerializeField] private Transform avatarRoot;

    [Header("Placement")]
    [SerializeField] private float distanceFromUser = 1.75f;
    [SerializeField] private float heightOffset = 0f;
    [SerializeField] private bool placeOnStart = true;
    [SerializeField] private bool faceUserOnPlace = true;

    [Header("Debug")]
    [SerializeField] private bool enableKeyboardRecenter = true;
    [SerializeField] private KeyCode recenterKey = KeyCode.R;

    private void Start()
    {
        if (placeOnStart)
        {
            RecenterAvatar();
        }
    }

    private void Update()
    {
        if (!enableKeyboardRecenter)
            return;

        if (Input.GetKeyDown(recenterKey))
        {
            RecenterAvatar();
        }
    }

    [ContextMenu("Recenter Avatar")]
    public void RecenterAvatar()
    {
        if (xrCamera == null)
        {
            Camera cam = Camera.main;
            if (cam != null)
                xrCamera = cam.transform;
        }

        if (xrCamera == null || avatarRoot == null)
        {
            Debug.LogWarning("[AvatarFrontPlacer] Missing xrCamera or avatarRoot.");
            return;
        }

        Vector3 forward = xrCamera.forward;
        forward.y = 0f;

        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 targetPosition = xrCamera.position + forward * distanceFromUser;
        targetPosition.y += heightOffset;

        avatarRoot.position = targetPosition;

        if (faceUserOnPlace)
        {
            Vector3 lookDirection = xrCamera.position - avatarRoot.position;
            lookDirection.y = 0f;

            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                avatarRoot.rotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            }
        }
    }
}