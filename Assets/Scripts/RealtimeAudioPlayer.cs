using System;
using UnityEngine;

namespace MVP.Conversation
{
    public class RealtimeAudioPlayer : MonoBehaviour
    {
        private static ConversationSettings Settings => ConversationSettings.Instance;

        private int bufferCapacitySamples
        {
            get => Settings.RealtimeAudioPlayer.bufferCapacitySamples;
            set => Settings.RealtimeAudioPlayer.bufferCapacitySamples = Mathf.Clamp(value, 48000, 768000);
        }

        private int prebufferSamples
        {
            get => Settings.RealtimeAudioPlayer.prebufferSamples;
            set => Settings.RealtimeAudioPlayer.prebufferSamples = Mathf.Clamp(value, 256, 16384);
        }

        private int startSafetySamples
        {
            get => Settings.RealtimeAudioPlayer.startSafetySamples;
            set => Settings.RealtimeAudioPlayer.startSafetySamples = Mathf.Max(0, value);
        }

        private bool enableAdaptiveStart = true;

        private int adaptiveStartMinSamples = 2048;

        private int adaptiveStartMaxWaitMs
        {
            get => Settings.RealtimeAudioPlayer.adaptiveStartMaxWaitMs;
            set => Settings.RealtimeAudioPlayer.adaptiveStartMaxWaitMs = Mathf.Clamp(value, 20, 10000);
        }

        private int drainGraceSamples
        {
            get => Settings.RealtimeAudioPlayer.drainGraceSamples;
            set => Settings.RealtimeAudioPlayer.drainGraceSamples = Mathf.Clamp(value, 0, 16384);
        }

        [Header("Audio Source")]
        [SerializeField]
        [Tooltip("AudioSource externo del escenario (p.ej., avatarAudioSource de OpenAIConversationController). Se reutiliza para todas las respuestas.")]
        private AudioSource audioSource; // External AudioSource (from OpenAIConversationController)

        private StreamingAudioBuffer buffer;
        private bool isInitialized;
        private bool audioStarted;
        private bool pendingAudioBegan;
        private bool producerCompleted;
        private bool playbackFinished;
        private int generationId;
        private Action onAudioBegan;
        private float[] scratchMono;
        private bool audioStartedLogged;
        private bool playbackFinishedLogged;
        private int lastReportedAvailableSamples = -1;
        private OVRLipSyncChunkBridge lipSyncBridge;
        private bool hasFirstWrite;
        private long firstWriteAtMs;

        public int GenerationId => generationId;
        public int AvailableSamples => buffer != null ? buffer.AvailableSamples : 0;
        public bool IsAudioStarted => audioStarted;
        public int FreeSamples => buffer != null ? buffer.FreeSamples : 0;
        public bool IsPlaybackFinished => playbackFinished;
        public bool IsProducerCompleted => producerCompleted;

        // Expose runtime-configurable parameters to UI
        public int PrebufferSamples
        {
            get => prebufferSamples;
            set => prebufferSamples = Mathf.Clamp(value, 256, 16384);
        }

        public int StartSafetySamples
        {
            get => startSafetySamples;
            set => startSafetySamples = Mathf.Max(0, value);
        }

        public bool EnableAdaptiveStart
        {
            get => enableAdaptiveStart;
            set => enableAdaptiveStart = value;
        }

        public int AdaptiveStartMinSamples
        {
            get => adaptiveStartMinSamples;
            set => adaptiveStartMinSamples = Mathf.Clamp(value, 256, 8192);
        }

        public int AdaptiveStartMaxWaitMs
        {
            get => adaptiveStartMaxWaitMs;
            set => adaptiveStartMaxWaitMs = Mathf.Clamp(value, 20, 10000);
        }

        public int BufferCapacitySamples => bufferCapacitySamples;

        public void BindLipSyncBridge(OVRLipSyncChunkBridge bridge)
        {
            lipSyncBridge = bridge;
        }

        public void MarkStreamingComplete(int producerGeneration)
        {
            if (!isInitialized || producerGeneration != generationId)
                return;

            producerCompleted = true;
            MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.ProducerCompleted, -1, generationId, 0);
        }

        private void OnEnable()
        {
            // Lazy initialization: set up when enabled
            if (isInitialized)
                return;

            if (audioSource == null)
            {
                Debug.LogError("[RealtimeAudioPlayer] audioSource is not assigned. Assign from OpenAIConversationController.avatarAudioSource.");
                return;
            }

            audioSource.playOnAwake = false;
            audioSource.loop = true;

            int sampleRate = AudioSettings.outputSampleRate;
            buffer = new StreamingAudioBuffer(bufferCapacitySamples);

            audioSource.clip = AudioClip.Create(
                "RealtimeLoop",
                bufferCapacitySamples,
                1,
                sampleRate,
                true);

            isInitialized = true;
            generationId = 0;
        }

        private void Update()
        {
            if (pendingAudioBegan)
            {
                pendingAudioBegan = false;
                onAudioBegan?.Invoke();
            }

            if (playbackFinished && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        public void ResetForNewGeneration(int newGenerationId, Action onAudioBeganCallback)
        {
            if (!isInitialized)
                return;

            generationId = newGenerationId;
            audioStarted = false;
            pendingAudioBegan = false;
            producerCompleted = false;
            playbackFinished = false;
            onAudioBegan = onAudioBeganCallback;
            audioStartedLogged = false;
            playbackFinishedLogged = false;
            lastReportedAvailableSamples = -1;
            hasFirstWrite = false;
            firstWriteAtMs = 0;

            buffer.Clear();

            Debug.Log(
                $"[RealtimeAudioPlayer] ResetForNewGeneration generation={generationId}, " +
                $"prebufferSamples={prebufferSamples}, drainGraceSamples={drainGraceSamples}, " +
                $"bufferCapacity={bufferCapacitySamples}, audioSourcePlaying={audioSource.isPlaying}");

            if (!audioSource.isPlaying)
                audioSource.Play();
        }

        public void StopAndClear()
        {
            if (!isInitialized)
                return;

            buffer.Clear();
            audioStarted = false;
            pendingAudioBegan = false;
            producerCompleted = false;
            playbackFinished = true;
            onAudioBegan = null;
            audioStartedLogged = false;
            playbackFinishedLogged = true;
            lastReportedAvailableSamples = 0;

            Debug.Log($"[RealtimeAudioPlayer] StopAndClear generation={generationId}");

            if (audioSource.isPlaying)
                audioSource.Stop();
        }

        public void MarkProducerCompleted(int producerGeneration)
        {
            if (!isInitialized || producerGeneration != generationId)
                return;

            producerCompleted = true;

            if (buffer.AvailableSamples <= 0)
                playbackFinished = true;
        }

        public int WriteSomeSamples(float[] samples, int offset, int sampleCount, int sampleGeneration)
        {
            if (!isInitialized || samples == null || sampleCount <= 0)
                return 0;

            if (sampleGeneration != generationId)
                return 0;

            playbackFinished = false;
            int written = buffer.WriteSome(samples, offset, sampleCount);

            if (written > 0 && !audioStartedLogged)
            {
                audioStartedLogged = true;
                Debug.Log(
                    $"[RealtimeAudioPlayer] First write generation={generationId}, " +
                    $"written={written}, available={buffer.AvailableSamples}, free={buffer.FreeSamples}");
            }

            int availableNow = buffer.AvailableSamples;
            if (written > 0 && !hasFirstWrite)
            {
                hasFirstWrite = true;
                firstWriteAtMs = DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond;
            }

            if (written > 0 && Mathf.Abs(availableNow - lastReportedAvailableSamples) >= prebufferSamples)
            {
                lastReportedAvailableSamples = availableNow;
                Debug.Log(
                    $"[RealtimeAudioPlayer] WriteSomeSamples generation={generationId}, " +
                    $"written={written}, available={availableNow}, free={buffer.FreeSamples}, " +
                    $"producerCompleted={producerCompleted}, playbackFinished={playbackFinished}");
            }

            // Telemetry: log writes (value = written samples)
            MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.AudioWrite, -1, sampleGeneration, written);

            return written;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!isInitialized || buffer == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            int frames = data.Length / channels;

            if (scratchMono == null || scratchMono.Length < frames)
                scratchMono = new float[frames];

            int available = buffer.AvailableSamples;
            int startThreshold = prebufferSamples + startSafetySamples;
            bool strictReady = available >= startThreshold;
            bool adaptiveReady = false;

            if (!strictReady && enableAdaptiveStart && hasFirstWrite && available >= adaptiveStartMinSamples)
            {
                long waitedMs = (DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond) - firstWriteAtMs;
                adaptiveReady = waitedMs >= adaptiveStartMaxWaitMs || producerCompleted;
            }

            if (!audioStarted && (strictReady || adaptiveReady))
            {
                audioStarted = true;
                pendingAudioBegan = true;

                Debug.Log(
                    $"[RealtimeAudioPlayer] Audio started generation={generationId}, " +
                    $"available={available}, prebuffer={prebufferSamples}, " +
                    $"safety={startSafetySamples}, threshold={startThreshold}, " +
                    $"adaptive={adaptiveReady}, min={adaptiveStartMinSamples}, waitMs={adaptiveStartMaxWaitMs}");

                MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.AudioStarted, -1, generationId, available);
            }

            Array.Clear(scratchMono, 0, frames);
            int availableBeforeRead = buffer.AvailableSamples;
            buffer.Read(scratchMono);

            int idx = 0;
            for (int i = 0; i < frames; i++)
            {
                float s = scratchMono[i];
                for (int c = 0; c < channels; c++)
                    data[idx++] = s;
            }

            // Keep viseme processing aligned with audible playback: skip silent pre-roll before audio starts.
            if (lipSyncBridge != null && audioStarted && availableBeforeRead > 0)
                lipSyncBridge.ProcessPlaybackSamples(scratchMono, frames, generationId);

            if (producerCompleted && buffer.AvailableSamples == 0)
            {
                playbackFinished = true;

                if (!playbackFinishedLogged)
                {
                    playbackFinishedLogged = true;
                    Debug.Log(
                        $"[RealtimeAudioPlayer] Playback finished generation={generationId}, " +
                        $"available={buffer.AvailableSamples}, drainGraceSamples={drainGraceSamples}, " +
                        $"producerCompleted={producerCompleted}, audioStarted={audioStarted}");
                }
            }
        }
    }
}