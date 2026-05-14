using UnityEngine;

namespace MVP.Conversation
{
    public class SimpleTTSLipSyncDriver : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private SkinnedMeshRenderer faceRenderer;
        [SerializeField] private int mouthOpenBlendShapeIndex = 0;

        [Header("Tuning")]
        [SerializeField] private float inputGain = 35f;
        [SerializeField] private float minLevel = 0.01f;
        [SerializeField] private float maxWeight = 100f;
        [SerializeField] private float attackSpeed = 18f;
        [SerializeField] private float releaseSpeed = 10f;

        private float targetWeight;
        private float currentWeight;
        private bool hasRecentAudio;
        private float lastAudioTime;

        public void PushLevel(float rmsLevel)
        {
            hasRecentAudio = true;
            lastAudioTime = Time.time;

            float normalized = Mathf.InverseLerp(minLevel, 1f / inputGain + minLevel, rmsLevel);
            targetWeight = Mathf.Clamp01(normalized * inputGain) * maxWeight;
        }

        public void ResetLipSync()
        {
            hasRecentAudio = false;
            targetWeight = 0f;
        }

        private void Update()
        {
            if (faceRenderer == null || mouthOpenBlendShapeIndex < 0 || mouthOpenBlendShapeIndex >= faceRenderer.sharedMesh.blendShapeCount)
                return;

            if (hasRecentAudio && Time.time - lastAudioTime > 0.12f)
            {
                hasRecentAudio = false;
                targetWeight = 0f;
            }

            float speed = targetWeight > currentWeight ? attackSpeed : releaseSpeed;
            currentWeight = Mathf.MoveTowards(currentWeight, targetWeight, speed * 100f * Time.deltaTime);
            faceRenderer.SetBlendShapeWeight(mouthOpenBlendShapeIndex, currentWeight);
        }
    }
}