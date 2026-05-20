using System;
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
        [SerializeField] private OVRLipSyncContext lipSyncContext;
        [SerializeField] private bool muteAudioSource = true;
        [SerializeField] private float bridgeVolume = 0f;
        [SerializeField] private bool stopAndFlushOnNewGeneration = true;
        [SerializeField] private bool processPlaybackSamplesDirectly = true;

        [Header("Debug")]
        [SerializeField] private bool verboseLogs = false;

        // Public runtime accessors
        public bool ProcessPlaybackSamplesDirectly { get => processPlaybackSamplesDirectly; set => processPlaybackSamplesDirectly = value; }
        public bool MuteAudioSource { get => muteAudioSource; set { muteAudioSource = value; if (lipSyncAudioSource != null) lipSyncAudioSource.volume = muteAudioSource ? 0f : bridgeVolume; } }
        public float BridgeVolume { get => bridgeVolume; set { bridgeVolume = value; if (lipSyncAudioSource != null && !muteAudioSource) lipSyncAudioSource.volume = bridgeVolume; } }
        public bool StopAndFlushOnNewGeneration { get => stopAndFlushOnNewGeneration; set => stopAndFlushOnNewGeneration = value; }
        public bool VerboseLogs { get => verboseLogs; set => verboseLogs = value; }

        private readonly Queue<QueuedChunk> queuedChunks = new();
        private Coroutine playbackRoutine;
        private int generationId;
        private bool isPlaying;
        private float[] directProcessBuffer;

        public int GenerationId => generationId;
        public bool IsPlaying => isPlaying;
        public int PendingChunkCount => queuedChunks.Count;

        private void Awake()
        {
            if (lipSyncAudioSource == null)
                lipSyncAudioSource = GetComponent<AudioSource>();

            if (lipSyncContext == null)
                lipSyncContext = GetComponent<OVRLipSyncContext>();

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

            if (processPlaybackSamplesDirectly && lipSyncContext != null)
            {
                int sampleCount = samples.Length;

                if (directProcessBuffer == null || directProcessBuffer.Length < sampleCount)
                    directProcessBuffer = new float[sampleCount];

                Array.Copy(samples, 0, directProcessBuffer, 0, sampleCount);
                lipSyncContext.ProcessAudioSamplesRaw(directProcessBuffer, 1);

                if (verboseLogs)
                    Debug.Log($"[OVRLipSyncChunkBridge] Processed {sampleCount} playback samples directly for lip sync gen={clipGeneration}");

                return;
            }

            int frameCount = samples.Length;
            AudioClip clip = AudioClip.Create($"OVR_LipSync_{clipGeneration}_{Time.frameCount}", frameCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            EnqueueClip(clip, clipGeneration);
        }

        public void ProcessPlaybackSamples(float[] samples, int sampleCount, int clipGeneration)
        {
            if (samples == null || sampleCount <= 0)
                return;

            if (clipGeneration != generationId)
                return;

            if (!processPlaybackSamplesDirectly || lipSyncContext == null)
                return;

            if (directProcessBuffer == null || directProcessBuffer.Length < sampleCount)
                directProcessBuffer = new float[sampleCount];

            Array.Copy(samples, 0, directProcessBuffer, 0, sampleCount);
            // Telemetry: note that lip sync processed a block (value = sampleCount)
            MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.LipSyncProcess, -1, clipGeneration, sampleCount);
            lipSyncContext.ProcessAudioSamplesRaw(directProcessBuffer, 1);
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