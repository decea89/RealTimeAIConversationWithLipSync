using System;
using UnityEngine;

namespace MVP.Conversation
{
    [RequireComponent(typeof(AudioSource))]
    public class LipSyncAudioFeed : MonoBehaviour
    {
        [Header("Buffer")]
        [SerializeField] private int bufferCapacitySamples = 48000 * 4;
        [SerializeField] private int prebufferSamples = 256;

        [Header("Debug")]
        [SerializeField] private bool autoMuteAudioSource = true;

        private StreamingAudioBuffer buffer;
        private AudioSource audioSource;
        private bool isInitialized;
        private int generationId;

        public int GenerationId => generationId;
        public int AvailableSamples => buffer != null ? buffer.AvailableSamples : 0;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;

            if (autoMuteAudioSource)
                audioSource.volume = 0f;

            int sampleRate = AudioSettings.outputSampleRate;

            buffer = new StreamingAudioBuffer(bufferCapacitySamples);

            audioSource.clip = AudioClip.Create(
                "LipSyncFeed",
                bufferCapacitySamples,
                1,
                sampleRate,
                true,
                OnClipRead);

            isInitialized = true;
            generationId = 0;
        }

        public void ResetForNewGeneration(int newGenerationId)
        {
            if (!isInitialized)
                return;

            generationId = newGenerationId;
            buffer.Clear();

            if (!audioSource.isPlaying)
            {
                audioSource.time = 0f;
                audioSource.Play();
            }
        }

        public void StopAndClear()
        {
            if (!isInitialized)
                return;

            buffer.Clear();

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

        private void OnClipRead(float[] data)
        {
            if (!isInitialized || buffer == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            if (buffer.AvailableSamples < prebufferSamples)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            buffer.Read(data);
        }
    }
}