/* using System;
using UnityEngine;

namespace MVP.Conversation
{
    public class LipSyncAvatarBridge : MonoBehaviour
    {
        [Serializable]
        public class VisemeBinding
        {
            public OVRLipSync.Viseme viseme;
            public string blendShapeName;
            [Range(0f, 100f)] public float maxWeight = 100f;

            [NonSerialized] public int blendShapeIndex = -1;
        }

        [Header("Runtime References")]
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private OVRLipSyncContext lipSyncContext;
        [SerializeField] private SkinnedMeshRenderer faceRenderer;

        [Header("Viseme Mapping")]
        [SerializeField] private VisemeBinding[] visemeBindings;

        [Header("Tuning")]
        [SerializeField] [Range(1f, 40f)] private float smoothSpeed = 18f;
        [SerializeField] [Range(0f, 0.1f)] private float silenceThreshold = 0.001f;
        [SerializeField] private bool resetWhenAudioStops = true;

        [Header("Optional Debug")]
        [SerializeField] private bool logWarnings = true;
        [SerializeField] private bool logResolvedBindings = false;

        private OVRLipSync.Frame frame = new OVRLipSync.Frame();
        private float[] targetWeights;
        private float[] currentWeights;
        private int blendShapeCount;

        private void Awake()
        {
            ValidateReferences();
            InitializeRendererData();
            ResolveBindings();
        }

        private void Update()
        {
            if (lipSyncContext == null || faceRenderer == null || faceRenderer.sharedMesh == null)
                return;

            EnsureArrays();

            bool hasAudio = avatarAudioSource != null && avatarAudioSource.isPlaying;

            Array.Clear(targetWeights, 0, targetWeights.Length);

            if (hasAudio)
            {
                lipSyncContext.GetCurrentPhonemeFrame(frame);

                if (frame != null && frame.Visemes != null)
                {
                    for (int i = 0; i < visemeBindings.Length; i++)
                    {
                        var binding = visemeBindings[i];
                        if (binding == null || binding.blendShapeIndex < 0)
                            continue;

                        int visemeIndex = (int)binding.viseme;
                        if (visemeIndex < 0 || visemeIndex >= frame.Visemes.Length)
                            continue;

                        float visemeWeight01 = Mathf.Clamp01(frame.Visemes[visemeIndex]);
                        if (visemeWeight01 <= silenceThreshold)
                            continue;

                        float weight100 = visemeWeight01 * binding.maxWeight;
                        int blendIndex = binding.blendShapeIndex;

                        if (weight100 > targetWeights[blendIndex])
                            targetWeights[blendIndex] = weight100;
                    }
                }
            }
            else if (resetWhenAudioStops)
            {
                Array.Clear(targetWeights, 0, targetWeights.Length);
            }

            ApplyWeights();
        }

        public void BindAudioSource(AudioSource source)
        {
            avatarAudioSource = source;
        }

        public AudioSource GetAudioSource()
        {
            return avatarAudioSource;
        }

        public void RebuildBindings()
        {
            InitializeRendererData();
            ResolveBindings();
        }

        private void ValidateReferences()
        {
            if (avatarAudioSource == null && logWarnings)
                Debug.LogWarning("LipSyncAvatarBridge: falta AudioSource del avatar.", this);

            if (lipSyncContext == null && logWarnings)
                Debug.LogWarning("LipSyncAvatarBridge: falta referencia a OVRLipSyncContext.", this);

            if (faceRenderer == null && logWarnings)
                Debug.LogWarning("LipSyncAvatarBridge: falta SkinnedMeshRenderer facial.", this);
        }

        private void InitializeRendererData()
        {
            blendShapeCount = 0;

            if (faceRenderer == null || faceRenderer.sharedMesh == null)
                return;

            blendShapeCount = faceRenderer.sharedMesh.blendShapeCount;
            targetWeights = new float[blendShapeCount];
            currentWeights = new float[blendShapeCount];
        }

        private void EnsureArrays()
        {
            if (faceRenderer == null || faceRenderer.sharedMesh == null)
                return;

            int count = faceRenderer.sharedMesh.blendShapeCount;
            if (targetWeights == null || currentWeights == null || targetWeights.Length != count || currentWeights.Length != count)
            {
                blendShapeCount = count;
                targetWeights = new float[count];
                currentWeights = new float[count];
            }
        }

        private void ResolveBindings()
        {
            if (faceRenderer == null || faceRenderer.sharedMesh == null || visemeBindings == null)
                return;

            Mesh mesh = faceRenderer.sharedMesh;

            for (int i = 0; i < visemeBindings.Length; i++)
            {
                var binding = visemeBindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.blendShapeName))
                    continue;

                binding.blendShapeIndex = mesh.GetBlendShapeIndex(binding.blendShapeName);

                if (binding.blendShapeIndex < 0 && logWarnings)
                {
                    Debug.LogWarning(
                        $"LipSyncAvatarBridge: no se encontró el blendshape '{binding.blendShapeName}' para el visema {binding.viseme}.",
                        this);
                }
                else if (logResolvedBindings)
                {
                    Debug.Log(
                        $"LipSyncAvatarBridge: visema {binding.viseme} -> blendshape '{binding.blendShapeName}' (index {binding.blendShapeIndex})",
                        this);
                }
            }
        }

        private void ApplyWeights()
        {
            if (faceRenderer == null || faceRenderer.sharedMesh == null)
                return;

            float t = Mathf.Clamp01(Time.deltaTime * smoothSpeed);

            for (int i = 0; i < blendShapeCount; i++)
            {
                currentWeights[i] = Mathf.Lerp(currentWeights[i], targetWeights[i], t);
                faceRenderer.SetBlendShapeWeight(i, currentWeights[i]);
            }
        }

        [ContextMenu("Log BlendShapes")]
        private void LogBlendShapes()
        {
            if (faceRenderer == null || faceRenderer.sharedMesh == null)
            {
                Debug.LogWarning("LipSyncAvatarBridge: no hay faceRenderer/sharedMesh.", this);
                return;
            }

            Mesh mesh = faceRenderer.sharedMesh;
            Debug.Log($"LipSyncAvatarBridge: blendShapeCount = {mesh.blendShapeCount}", this);

            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                Debug.Log($"[{i}] {mesh.GetBlendShapeName(i)}", this);
            }
        }

        [ContextMenu("Reset All BlendShapes")]
        private void ResetAllBlendShapes()
        {
            if (faceRenderer == null || faceRenderer.sharedMesh == null)
                return;

            for (int i = 0; i < faceRenderer.sharedMesh.blendShapeCount; i++)
            {
                faceRenderer.SetBlendShapeWeight(i, 0f);
            }

            if (currentWeights != null)
                Array.Clear(currentWeights, 0, currentWeights.Length);

            if (targetWeights != null)
                Array.Clear(targetWeights, 0, targetWeights.Length);
        }
    }
} */