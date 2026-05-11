using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MVP.Conversation
{
    public interface IRealtimeTTSHandle
    {
        int TurnId { get; }
        bool IsCompleted { get; }
        void Cancel();
    }

    public interface IRealtimeTTSService
    {
        IRealtimeTTSHandle StartStream(
            string text,
            int turnId,
            Action onAudioBegan,
            Action<string> onError);

        void CancelAll();
    }

    public class RealtimeOpenAITTSClient : MonoBehaviour, IRealtimeTTSService
    {
        [Header("Dependencies")]
        [SerializeField] private MonoBehaviour innerTtsServiceBehaviour; // ITTSService
        [SerializeField] private RealtimeAudioPlayer realtimeAudioPlayer;

        [Header("Pseudo-streaming")]
        [SerializeField] private int maxChunkChars = 80;
        [SerializeField] private float interChunkGapSeconds = 0.0f;

        private ITTSService innerTtsService;
        private readonly List<RealtimeTTSHandle> activeHandles = new();
        private int generationId;

        private void Awake()
        {
            if (innerTtsServiceBehaviour != null)
                innerTtsService = innerTtsServiceBehaviour as ITTSService;

            if (innerTtsService == null)
                Debug.LogError("[RealtimeOpenAITTSClient] innerTtsServiceBehaviour no implementa ITTSService.");

            if (realtimeAudioPlayer == null)
                Debug.LogError("[RealtimeOpenAITTSClient] realtimeAudioPlayer no está asignado.");
        }

        public IRealtimeTTSHandle StartStream(
            string text,
            int turnId,
            Action onAudioBegan,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("RealtimeOpenAITTSClient: text vacío.");
                return null;
            }

            if (innerTtsService == null || realtimeAudioPlayer == null)
            {
                onError?.Invoke("RealtimeOpenAITTSClient: dependencias no configuradas.");
                return null;
            }

            generationId++;
            int localGeneration = generationId;

            realtimeAudioPlayer.ResetForNewGeneration(localGeneration, onAudioBegan);

            var handle = new RealtimeTTSHandle(turnId, localGeneration, this);
            activeHandles.Add(handle);

            StartCoroutine(RunPseudoStreaming(text, handle, onError));

            return handle;
        }

        public void CancelAll()
        {
            generationId++;

            foreach (var h in activeHandles)
                h.MarkCanceled();

            activeHandles.Clear();

            if (realtimeAudioPlayer != null)
                realtimeAudioPlayer.StopAndClear();
        }

        private IEnumerator RunPseudoStreaming(string text, RealtimeTTSHandle handle, Action<string> onError)
        {
            List<string> chunks = SplitText(text, maxChunkChars);

            foreach (string chunk in chunks)
            {
                if (handle.IsCanceled || handle.GenerationId != generationId)
                    yield break;

                AudioClip clip = null;
                string ttsError = null;

                yield return innerTtsService.RequestSpeech(chunk, (c, err) =>
                {
                    clip = c;
                    ttsError = err;
                });

                if (handle.IsCanceled || handle.GenerationId != generationId)
                    yield break;

                if (!string.IsNullOrEmpty(ttsError))
                {
                    onError?.Invoke(ttsError);
                    handle.MarkCompleted();
                    yield break;
                }

                if (clip == null)
                {
                    onError?.Invoke("RealtimeOpenAITTSClient: clip nulo en chunk TTS.");
                    handle.MarkCompleted();
                    yield break;
                }

                int clipSr = clip.frequency;
                int outputSr = AudioSettings.outputSampleRate;
                int clipChannels = clip.channels;

                Debug.Log($"[RealtimeOpenAITTSClient] clip sr={clipSr}, channels={clipChannels}, length={clip.length}");
                Debug.Log($"[RealtimeOpenAITTSClient] output sr={outputSr}, device channels={AudioSettings.speakerMode}");

                float[] raw = new float[clip.samples * clipChannels];
                clip.GetData(raw, 0);

                float[] samples = ResampleToOutputMono(
                    raw,
                    clipSamples: clip.samples,
                    clipChannels: clipChannels,
                    srcRate: clipSr,
                    dstRate: outputSr);

                Debug.Log($"[RealtimeTTS] monoSamples={clip.samples}, resampledSamples={samples.Length}");

                // realtimeAudioPlayer.WriteSamples(samples, samples.Length, handle.GenerationId);
                yield return EnqueueSamplesGradually(samples, handle);

                if (interChunkGapSeconds > 0f)
                {
                    float endTime = Time.time + interChunkGapSeconds;
                    while (Time.time < endTime)
                    {
                        if (handle.IsCanceled || handle.GenerationId != generationId)
                            yield break;
                        yield return null;
                    }
                }
            }

            handle.MarkCompleted();
            RemoveHandle(handle);
        }

        private IEnumerator EnqueueSamplesGradually(float[] samples, RealtimeTTSHandle handle)
        {
            int offset = 0;

            while (offset < samples.Length)
            {
                if (handle.IsCanceled || handle.GenerationId != generationId)
                    yield break;

                int written = realtimeAudioPlayer.WriteSomeSamples(
                    samples,
                    offset,
                    samples.Length - offset,
                    handle.GenerationId);

                if (written > 0)
                {
                    offset += written;
                }
                else
                {
                    yield return null; // esperar a que OnAudioFilterRead consuma
                }
            }
        }

        private List<string> SplitText(string text, int maxChars)
        {
            var result = new List<string>();
            string remaining = text.Trim();

            while (remaining.Length > maxChars)
            {
                int cut = remaining.LastIndexOf(' ', maxChars);
                if (cut <= 0) cut = maxChars;

                string chunk = remaining.Substring(0, cut).Trim();
                if (!string.IsNullOrEmpty(chunk))
                    result.Add(chunk);

                remaining = remaining.Substring(cut).Trim();
            }

            if (!string.IsNullOrEmpty(remaining))
                result.Add(remaining);

            return result;
        }

        private float[] ResampleToOutputMono(
            float[] source,
            int clipSamples,
            int clipChannels,
            int srcRate,
            int dstRate)
        {
            int monoSamples = clipSamples;
            float[] mono = new float[monoSamples];

            if (clipChannels == 1)
            {
                Array.Copy(source, mono, monoSamples);
            }
            else
            {
                for (int i = 0; i < monoSamples; i++)
                {
                    float sum = 0f;
                    int baseIndex = i * clipChannels;
                    for (int c = 0; c < clipChannels; c++)
                        sum += source[baseIndex + c];

                    mono[i] = sum / clipChannels;
                }
            }

            if (srcRate == dstRate)
                return mono;

            float ratio = (float)srcRate / dstRate;
            int dstSamples = Mathf.CeilToInt(monoSamples / ratio);
            float[] dst = new float[dstSamples];

            for (int i = 0; i < dstSamples; i++)
            {
                float srcPos = i * ratio;
                int i0 = Mathf.Clamp((int)srcPos, 0, monoSamples - 1);
                int i1 = Mathf.Min(i0 + 1, monoSamples - 1);
                float t = srcPos - i0;

                dst[i] = Mathf.Lerp(mono[i0], mono[i1], t);
            }

            return dst;
        }

        private void RemoveHandle(RealtimeTTSHandle handle)
        {
            activeHandles.Remove(handle);
        }

        private class RealtimeTTSHandle : IRealtimeTTSHandle
        {
            private bool canceled;
            private bool completed;

            public int TurnId { get; }
            public int GenerationId { get; }
            public bool IsCompleted => completed || canceled;
            public bool IsCanceled => canceled;

            public RealtimeTTSHandle(int turnId, int generationId, RealtimeOpenAITTSClient owner)
            {
                TurnId = turnId;
                GenerationId = generationId;
            }

            public void Cancel()
            {
                if (canceled || completed)
                    return;

                canceled = true;
            }

            public void MarkCanceled()
            {
                canceled = true;
            }

            public void MarkCompleted()
            {
                completed = true;
            }
        }
    }
}