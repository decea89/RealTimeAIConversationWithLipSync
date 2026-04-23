using UnityEngine;

namespace MVP.Conversation
{
    public class LipSyncAvatarBridge : MonoBehaviour
    {
        [Header("Runtime References")]
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private MonoBehaviour lipSyncContextBehaviour;
        [SerializeField] private SkinnedMeshRenderer faceRenderer;

        [Header("Optional Debug")]
        [SerializeField] private bool logWarnings = true;

        private void Awake()
        {
            if (avatarAudioSource == null && logWarnings)
                Debug.LogWarning("LipSyncAvatarBridge: falta AudioSource del avatar.");

            if (lipSyncContextBehaviour == null && logWarnings)
                Debug.LogWarning("LipSyncAvatarBridge: falta referencia a OVRLipSyncContext.");

            if (faceRenderer == null && logWarnings)
                Debug.LogWarning("LipSyncAvatarBridge: falta SkinnedMeshRenderer facial.");
        }

        public void BindAudioSource(AudioSource source)
        {
            avatarAudioSource = source;
        }

        public AudioSource GetAudioSource()
        {
            return avatarAudioSource;
        }
    }
}