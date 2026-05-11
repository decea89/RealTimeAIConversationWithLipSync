using System;
using UnityEngine;

namespace MVP.Conversation
{
    [RequireComponent(typeof(AudioSource))]
    public class RealtimeAudioPlayer : MonoBehaviour
    {
        [Header("Buffer")]
        [SerializeField] private int bufferCapacitySamples = 44100 * 4; // ~4s a 44.1kHz
        [SerializeField] private int prebufferSamples = 256;

        private StreamingAudioBuffer buffer;
        private AudioSource audioSource;
        private bool isInitialized;
        private bool audioStarted;
        private bool pendingAudioBegan;
        private int generationId;
        private Action onAudioBegan;

        public int GenerationId => generationId;
        public int AvailableSamples => buffer != null ? buffer.AvailableSamples : 0;
        public bool IsAudioStarted => audioStarted;

        public int FreeSamples => buffer != null ? buffer.FreeSamples : 0;


        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;

            int sampleRate = AudioSettings.outputSampleRate;

            buffer = new StreamingAudioBuffer(bufferCapacitySamples);

            audioSource.clip = AudioClip.Create(
                "RealtimeLoop",
                bufferCapacitySamples,
                1,                  // mono
                sampleRate,
                false);

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
        }

        public void ResetForNewGeneration(int newGenerationId, Action onAudioBeganCallback)
        {
            if (!isInitialized)
                return;

            generationId = newGenerationId;
            audioStarted = false;
            pendingAudioBegan = false;
            onAudioBegan = onAudioBeganCallback;

            buffer.Clear();

            if (!audioSource.isPlaying)
            {
                audioSource.time = 0f;
                // audioSource.pitch = 1f;
                audioSource.Play();
            }
        }

        public void StopAndClear()
        {
            if (!isInitialized)
                return;

            buffer.Clear();
            audioStarted = false;
            pendingAudioBegan = false;
            onAudioBegan = null;

            if (audioSource.isPlaying)
                audioSource.Stop();
        }

        public int WriteSomeSamples(float[] samples, int offset, int sampleCount, int sampleGeneration)
        {
            if (!isInitialized || samples == null || sampleCount <= 0)
                return 0;

            if (sampleGeneration != generationId)
                return 0;

            return buffer.WriteSome(samples, offset, sampleCount);
        }


        // public void WriteSamples(float[] samples, int sampleCount, int sampleGeneration)
        // {
        //     if (!isInitialized || samples == null || sampleCount <= 0)
        //         return;

        //     if (sampleGeneration != generationId)
        //         return;

        //     // samples viene ya como MONO (tras resampling)
        //     buffer.Write(samples, sampleCount);
        // }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!isInitialized || buffer == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            int frames = data.Length / channels;
            float[] mono = new float[frames];

            buffer.Read(mono); // leemos frames mono

            int idx = 0;
            for (int i = 0; i < frames; i++)
            {
                float s = mono[i];
                for (int c = 0; c < channels; c++)
                    data[idx++] = s;
            }

            if (!audioStarted && buffer.AvailableSamples >= prebufferSamples)
            {
                audioStarted = true;
                pendingAudioBegan = true;
            }
        }
    }
}