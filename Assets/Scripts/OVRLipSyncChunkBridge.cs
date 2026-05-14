using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MVP.Conversation
{
    [RequireComponent(typeof(AudioSource))]
    public class OVRLipSyncChunkBridge : MonoBehaviour
    {
        [Header("Playback")]
        [SerializeField] private AudioSource lipSyncAudioSource;
        [SerializeField] private bool muteAudioSource = true;
        [SerializeField] private float bridgeVolume = 0f;
        [SerializeField] private bool stopAndFlushOnNewGeneration = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        private readonly Queue<QueuedChunk> queuedChunks = new();
        private Coroutine playbackRoutine;
        private int generationId;
        private bool isPlaying;

        public int GenerationId => generationId;
        public bool IsPlaying => isPlaying;
        public int PendingChunkCount => queuedChunks.Count;

        private void Awake()
        {
            if (lipSyncAudioSource == null)
                lipSyncAudioSource = GetComponent<AudioSource>();

            if (lipSyncAudioSource == null)
            {
                Debug.LogError("[OVRLipSyncChunkBridge] No AudioSource available.");
                return;
            }

            lipSyncAudioSource.playOnAwake = false;
            lipSyncAudioSource.loop = false;
            lipSyncAudioSource.spatialBlend = 0f;

            if (muteAudioSource)
                lipSyncAudioSource.volume = 0f;
            else
                lipSyncAudioSource.volume = bridgeVolume;
        }

        public void ResetForNewGeneration(int newGenerationId)
        {
            generationId = newGenerationId;

            if (stopAndFlushOnNewGeneration)
                StopAndClear();

            if (verboseLogs)
                Debug.Log($"[OVRLipSyncChunkBridge] ResetForNewGeneration generation={generationId}");
        }

        public void StopAndClear()
        {
            queuedChunks.Clear();
            isPlaying = false;

            if (playbackRoutine != null)
            {
                StopCoroutine(playbackRoutine);
                playbackRoutine = null;
            }

            if (lipSyncAudioSource != null)
            {
                lipSyncAudioSource.Stop();
                lipSyncAudioSource.clip = null;
            }

            if (verboseLogs)
                Debug.Log("[OVRLipSyncChunkBridge] StopAndClear");
        }

        public void EnqueueClip(AudioClip clip, int clipGeneration)
        {
            if (clip == null)
                return;

            if (clipGeneration != generationId)
                return;

            queuedChunks.Enqueue(new QueuedChunk
            {
                clip = clip,
                generationId = clipGeneration
            });

            if (verboseLogs)
                Debug.Log($"[OVRLipSyncChunkBridge] EnqueueClip len={clip.length:F3}s gen={clipGeneration} pending={queuedChunks.Count}");

            if (playbackRoutine == null && gameObject.activeInHierarchy)
                playbackRoutine = StartCoroutine(PlayQueuedChunks());
        }

        public void EnqueueSamples(float[] samples, int sampleRate, int clipGeneration)
        {
            if (samples == null || samples.Length == 0)
                return;

            if (clipGeneration != generationId)
                return;

            int frameCount = samples.Length;
            AudioClip clip = AudioClip.Create($"OVR_LipSync_{clipGeneration}_{Time.frameCount}", frameCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            EnqueueClip(clip, clipGeneration);
        }

        private IEnumerator PlayQueuedChunks()
        {
            while (queuedChunks.Count > 0)
            {
                var item = queuedChunks.Dequeue();

                if (item.clip == null || item.generationId != generationId)
                    continue;

                lipSyncAudioSource.clip = item.clip;
                lipSyncAudioSource.time = 0f;
                lipSyncAudioSource.Play();
                isPlaying = true;

                if (verboseLogs)
                    Debug.Log($"[OVRLipSyncChunkBridge] Playing chunk len={item.clip.length:F3}s gen={item.generationId}");

                while (lipSyncAudioSource != null && lipSyncAudioSource.isPlaying)
                    yield return null;

                isPlaying = false;
                lipSyncAudioSource.clip = null;
            }

            playbackRoutine = null;
        }

        private struct QueuedChunk
        {
            public AudioClip clip;
            public int generationId;
        }
    }
}