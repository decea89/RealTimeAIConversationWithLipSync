using System;
using UnityEngine;

namespace MVP.Conversation
{
    public class RealtimeAudioPlayer : MonoBehaviour
    {
        [Header("Buffer")]
        [SerializeField]
        [Range(48000, 768000)]
        [Tooltip("Capacidad total del buffer (samples). Más alto=más memoria pero más seguro para audio largo. Típico: 768000 (~16s a 48kHz).")]
        private int bufferCapacitySamples = 48000 * 16; // ~16s mono a 48kHz
        
        [SerializeField]
        [Range(512, 16384)]
        [Tooltip("Buffer mínimo antes de reproducir (samples). Más alto=más seguro pero latencia inicial. 2048=~42ms delay.")]
        private int prebufferSamples = 2048;
        
        [SerializeField]
        [Range(0, 16384)]
        [Tooltip("Muestras extra de margen de seguridad antes de considerar que el audio ha comenzado. Ajusta para absorber pequeñas pausas al inicio.")]
        private int startSafetySamples = 2048;
        
        [SerializeField]
        [Range(256, 4096)]
        [Tooltip("Muestras que deben quedar como máximo para dar por terminado el audio. Más alto=cierra antes; más bajo=espera más a que drene.")]
        private int drainGraceSamples = 256;

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

        public int GenerationId => generationId;
        public int AvailableSamples => buffer != null ? buffer.AvailableSamples : 0;
        public bool IsAudioStarted => audioStarted;
        public int FreeSamples => buffer != null ? buffer.FreeSamples : 0;
        public bool IsPlaybackFinished => playbackFinished;
        public bool IsProducerCompleted => producerCompleted;

        public void MarkStreamingComplete(int producerGeneration)
        {
            if (!isInitialized || producerGeneration != generationId)
                return;

            producerCompleted = true;
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
            if (written > 0 && Mathf.Abs(availableNow - lastReportedAvailableSamples) >= prebufferSamples)
            {
                lastReportedAvailableSamples = availableNow;
                Debug.Log(
                    $"[RealtimeAudioPlayer] WriteSomeSamples generation={generationId}, " +
                    $"written={written}, available={availableNow}, free={buffer.FreeSamples}, " +
                    $"producerCompleted={producerCompleted}, playbackFinished={playbackFinished}");
            }

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

            int startThreshold = prebufferSamples + startSafetySamples;
            if (!audioStarted && buffer.AvailableSamples >= startThreshold)
            {
                audioStarted = true;
                pendingAudioBegan = true;

                Debug.Log(
                    $"[RealtimeAudioPlayer] Audio started generation={generationId}, " +
                    $"available={buffer.AvailableSamples}, prebuffer={prebufferSamples}, " +
                    $"safety={startSafetySamples}, threshold={startThreshold}");
            }

            Array.Clear(scratchMono, 0, frames);
            buffer.Read(scratchMono);

            int idx = 0;
            for (int i = 0; i < frames; i++)
            {
                float s = scratchMono[i];
                for (int c = 0; c < channels; c++)
                    data[idx++] = s;
            }

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