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
        [SerializeField]
        [Tooltip("Componente BufferedOpenAITTSClient o similar para pseudo-streaming (fallback si no hay streaming).")]
        private MonoBehaviour innerTtsServiceBehaviour; // ITTSService
        
        [SerializeField]
        [Tooltip("Componente RealtimeAudioPlayer. Maneja playback de streaming de audio.")]
        private RealtimeAudioPlayer realtimeAudioPlayer;
        
        [SerializeField]
        [Tooltip("Componente OVRLipSyncChunkBridge. Enqueue clips para lip sync en tiempo real. Opcional.")]
        private OVRLipSyncChunkBridge lipSyncBridge;
        
        [SerializeField]
        [Tooltip("(Obsoleto) AudioSource antiguo. No se usa en la ruta de streaming actual.")]
        private AudioSource streamingAudioSource;

        [Header("Pseudo-streaming")]
        [SerializeField]
        [Range(10, 100)]
        [Tooltip("Caracteres por chunk TTS. Bajo (10-20)=latencia alta. Alto (50-100)=menos chunks. Recomendado: 30-40.")]
        private int maxChunkChars = 30;
        
        [SerializeField]
        [Range(0f, 1.0f)]
        [Tooltip("Espera entre chunks (s). 0=sin espera. 0.1-0.3=pausa natural entre frases.")]
        private float interChunkGapSeconds = 0.0f;
        
        [SerializeField]
        [Tooltip("Mostrar logs detallados (inicio, cancela, etc).")]
        private bool verboseLogging = true;
        
        [Header("Telemetry")]
        [SerializeField]
        [Tooltip("Mostrar métricas: tiempo a primer audio, duración reproducción.")]
        private bool enableTelemetry = true;
        
        [SerializeField]
        [Range(0.5f, 30f)]
        [Tooltip("Tiempo máximo esperando a que entren más samples (s). Sube esto si en respuestas largas el audio se queda parado o tarda en reanudar; bájalo si prefieres abortar antes cuando la cola se atasca.")]
        private float enqueueStallTimeoutSeconds = 8f;
        
        [Header("Smoothing")]
        [SerializeField]
        [Range(0f, 200f)]
        [Tooltip("Transición entre chunks (ms). 0=sin suavizado. 20-50=recomendado.")]
        private float chunkCrossfadeMs = 35f;

        // Public runtime accessors
        public bool VerboseLogging { get => verboseLogging; set => verboseLogging = value; }
        public bool EnableTelemetry { get => enableTelemetry; set => enableTelemetry = value; }
        public float EnqueueStallTimeoutSeconds { get => enqueueStallTimeoutSeconds; set => enqueueStallTimeoutSeconds = Mathf.Max(0f, value); }
        public float ChunkCrossfadeMs { get => chunkCrossfadeMs; set => chunkCrossfadeMs = Mathf.Max(0f, value); }

        private ITTSService innerTtsService;
        private readonly List<RealtimeTTSHandle> activeHandles = new();
        private int generationId;
        // telemetry
        private float telemetry_timeToFirstAudio = -1f;
        private int telemetry_totalEnqueueStalls = 0;

        public int MaxChunkChars
        {
            get => maxChunkChars;
            set => maxChunkChars = Mathf.Max(1, value);
        }

        public float InterChunkGapSeconds
        {
            get => interChunkGapSeconds;
            set => interChunkGapSeconds = Mathf.Max(0f, value);
        }

        private void Awake()
        {
            // Try to get ITTSService for pseudo-streaming path (optional)
            if (innerTtsServiceBehaviour != null)
                innerTtsService = innerTtsServiceBehaviour as ITTSService;

            // Validate that at least one valid service is configured
            var streamingService = innerTtsServiceBehaviour as IStreamingTTSService;
            if (innerTtsService == null && streamingService == null)
            {
                Debug.LogError(
                    "[RealtimeOpenAITTSClient] innerTtsServiceBehaviour must implement either " +
                    "ITTSService (pseudo-streaming) or IStreamingTTSService (true streaming).");
            }

            if (realtimeAudioPlayer == null)
                Debug.LogError("[RealtimeOpenAITTSClient] realtimeAudioPlayer no está asignado.");

            if (streamingAudioSource == null)
            {
                var go = new GameObject("RealtimeTTS_StreamingSource");
                go.transform.SetParent(transform);
                streamingAudioSource = go.AddComponent<AudioSource>();
                streamingAudioSource.playOnAwake = false;
                streamingAudioSource.spatialBlend = 0f;
                streamingAudioSource.loop = false;
                streamingAudioSource.volume = 1f;
            }
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

            generationId++;
            int localGeneration = generationId;

            // Check which service path is available
            var streamingService = innerTtsServiceBehaviour as IStreamingTTSService;
            bool hasStreaming = streamingService != null;
            bool hasPseudo = innerTtsService != null;

            if (!hasStreaming && !hasPseudo)
            {
                onError?.Invoke("RealtimeOpenAITTSClient: no valid TTS service configured (need ITTSService or IStreamingTTSService).");
                return null;
            }

            if (realtimeAudioPlayer == null)
            {
                onError?.Invoke("RealtimeOpenAITTSClient: realtimeAudioPlayer no está asignado.");
                return null;
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"[RealtimeOpenAITTSClient] StartStream turn={turnId}, generation={localGeneration}, " +
                    $"textLength={text.Length}, hasStreaming={hasStreaming}, hasPseudo={hasPseudo}");
            }

            Action wrappedOnAudioBegan = () =>
            {
                if (verboseLogging)
                    Debug.Log($"[RealtimeOpenAITTSClient] onAudioBegan turn={turnId}, generation={localGeneration}");

                onAudioBegan?.Invoke();
            };

            realtimeAudioPlayer.ResetForNewGeneration(localGeneration, wrappedOnAudioBegan);
            realtimeAudioPlayer.BindLipSyncBridge(lipSyncBridge);

            if (lipSyncBridge != null)
                lipSyncBridge.ResetForNewGeneration(localGeneration);

            var handle = new RealtimeTTSHandle(turnId, localGeneration, this);
            activeHandles.Add(handle);

            // Prefer streaming path if available (better latency)
            if (hasStreaming)
            {
                if (verboseLogging)
                    Debug.Log("[RealtimeOpenAITTSClient] Using IStreamingTTSService path.");

                    // If the streaming service supports direct player writing, prefer that unified path.
                    if (streamingService is StreamingOpenAITTSClient soc)
                    {
                        void CompleteDirectStreamHandle()
                        {
                            if (verboseLogging)
                                Debug.Log($"[RealtimeOpenAITTSClient] Direct stream complete turn={turnId}, generation={localGeneration}");

                            handle.MarkCompleted();
                            RemoveHandle(handle);
                        }

                        StartCoroutine(soc.RequestSpeechStreamedToPlayer(
                            text,
                            realtimeAudioPlayer,
                            lipSyncBridge,
                            turnId,
                            () => { try { onAudioBegan?.Invoke(); } catch (Exception) { } },
                            err =>
                            {
                                if (verboseLogging)
                                    Debug.LogError($"[RealtimeOpenAITTSClient] Direct stream error turn={turnId}, generation={localGeneration}: {err}");

                                try { onError?.Invoke(err); } catch (Exception) { }
                                CompleteDirectStreamHandle();
                            },
                            () =>
                            {
                                if (verboseLogging)
                                    Debug.Log($"[RealtimeOpenAITTSClient] Direct stream onCompleted turn={turnId}, generation={localGeneration}");

                                CompleteDirectStreamHandle();
                            }));
                        return handle;
                    }

                    StartCoroutine(RunStreaming(text, handle, streamingService, onAudioBegan, onError));
                    return handle;
            }

            int effectiveMaxChunk = Mathf.Clamp(maxChunkChars, 10, 40);
            if (effectiveMaxChunk != maxChunkChars)
            {
                Debug.LogWarning($"[RealtimeOpenAITTSClient] maxChunkChars clamped from {maxChunkChars} to {effectiveMaxChunk} to reduce TTS latency.");
            }

            StartCoroutine(RunPseudoStreaming(text, handle, onError, effectiveMaxChunk));

            return handle;
        }

        public void CancelAll()
        {
            generationId++;

            if (verboseLogging)
                Debug.Log($"[RealtimeOpenAITTSClient] CancelAll -> new generation={generationId}");

            foreach (var h in activeHandles)
                h.MarkCanceled();

            activeHandles.Clear();

            if (realtimeAudioPlayer != null)
            {
                realtimeAudioPlayer.StopAndClear();
                realtimeAudioPlayer.BindLipSyncBridge(null);
            }

            if (lipSyncBridge != null)
                lipSyncBridge.StopAndClear();

            if (streamingAudioSource != null)
            {
                try
                {
                    streamingAudioSource.Stop();
                    streamingAudioSource.clip = null;
                }
                catch (Exception)
                {
                    // ignore
                }
            }
        }

    private IEnumerator RunPseudoStreaming(string text, RealtimeTTSHandle handle, Action<string> onError, int chunkSize)
{
        List<string> chunks = SplitText(text, chunkSize);

    if (chunks.Count == 0)
    {
        handle.MarkCompleted();
        RemoveHandle(handle);
        yield break;
    }

    if (verboseLogging)
        Debug.Log($"[RealtimeOpenAITTSClient] SplitText -> {chunks.Count} chunks");

    float streamStartTime = Time.realtimeSinceStartup;
    bool firstChunkReadyLogged = false;
    telemetry_timeToFirstAudio = -1f;
    telemetry_totalEnqueueStalls = 0;

    AudioClip currentClip = null;
    string currentError = null;

    int currentIndex = 0;
    string currentText = chunks[currentIndex];

    if (verboseLogging)
    {
        Debug.Log(
            $"[RealtimeTTS] chunk {currentIndex + 1}/{chunks.Count} start " +
            $"len={currentText.Length} turn={handle.TurnId} generation={handle.GenerationId} text=\"{currentText}\"");
    }

    Debug.Log($"[RealtimeTTS] RequestSpeech START chunk {currentIndex+1}/{chunks.Count} generation={handle.GenerationId}");
    yield return innerTtsService.RequestSpeech(
        currentText,
        (clip, err) =>
        {
            currentClip = clip;
            currentError = err;
        });
    Debug.Log($"[RealtimeTTS] RequestSpeech END chunk {currentIndex+1}/{chunks.Count} err={(string.IsNullOrEmpty(currentError)?"none":currentError)} clip={(currentClip==null?"null":"ok")}");

    while (true)
    {
        if (handle.IsCanceled || handle.GenerationId != generationId)
            yield break;

        float chunkStartTime = Time.realtimeSinceStartup;

        if (!string.IsNullOrEmpty(currentError))
        {
            Debug.LogError(
                $"[RealtimeTTS] chunk {currentIndex + 1}/{chunks.Count} ERROR -> {currentError}");

            onError?.Invoke(currentError);
            handle.MarkCompleted();
            RemoveHandle(handle);
            yield break;
        }

        if (currentClip == null)
        {
            string err = "RealtimeOpenAITTSClient: clip nulo en chunk TTS.";
            Debug.LogError(
                $"[RealtimeTTS] chunk {currentIndex + 1}/{chunks.Count} returned null clip");

            onError?.Invoke(err);
            handle.MarkCompleted();
            RemoveHandle(handle);
            yield break;
        }

        int clipSr = currentClip.frequency;
        int outputSr = AudioSettings.outputSampleRate;
        int clipChannels = currentClip.channels;

        if (verboseLogging)
        {
            Debug.Log(
                $"[RealtimeTTS] chunk {currentIndex + 1}/{chunks.Count} clip ready " +
                $"clipLength={currentClip.length:F2}s sr={clipSr} channels={clipChannels}");
            Debug.Log($"[RealtimeTTS] output sr={outputSr}, device channels={AudioSettings.speakerMode}");
        }

        float[] raw = new float[currentClip.samples * clipChannels];
        currentClip.GetData(raw, 0);

        float[] samples = ResampleToOutputMono(
            raw,
            clipSamples: currentClip.samples,
            clipChannels: clipChannels,
            srcRate: clipSr,
            dstRate: outputSr);

        if (!firstChunkReadyLogged)
        {
            firstChunkReadyLogged = true;
            float firstReady = Time.realtimeSinceStartup - streamStartTime;
            telemetry_timeToFirstAudio = firstReady;
            Debug.Log(
                $"[RealtimeTTS] FIRST CHUNK READY in {firstReady:F2}s " +
                $"turn={handle.TurnId}, clipLength={currentClip.length:F2}s");
        }

        float resampleDoneTime = Time.realtimeSinceStartup;

        if (verboseLogging)
        {
            Debug.Log(
                $"[RealtimeTTS] chunk {currentIndex + 1}/{chunks.Count} resample done " +
                $"resampleTime={resampleDoneTime - chunkStartTime:F2}s " +
                $"monoSamples={currentClip.samples}, resampledSamples={samples.Length}");
        }

        int nextIndex = currentIndex + 1;
        AudioClip nextClip = null;
        string nextError = null;
        Coroutine nextCoroutine = null;

        if (nextIndex < chunks.Count)
        {
            string nextText = chunks[nextIndex];

            if (verboseLogging)
            {
                Debug.Log(
                    $"[RealtimeTTS] prefetch chunk {nextIndex + 1}/{chunks.Count} start " +
                    $"len={nextText.Length} turn={handle.TurnId} generation={handle.GenerationId} text=\"{nextText}\"");
            }

            nextCoroutine = StartCoroutine(RequestSpeechCoroutine(
                nextText,
                clip => nextClip = clip,
                err => nextError = err));
        }

        Debug.Log($"[RealtimeTTS] EnqueueSamplesGradually START chunk {currentIndex+1}/{chunks.Count} samples={samples.Length}");
        
        // Enqueue clip to lip sync bridge if available
        if (lipSyncBridge != null && currentClip != null)
        {
            lipSyncBridge.EnqueueClip(currentClip, handle.GenerationId);
        }
        
        yield return EnqueueSamplesGradually(samples, handle, currentIndex, chunks.Count);
        Debug.Log($"[RealtimeTTS] EnqueueSamplesGradually END chunk {currentIndex+1}/{chunks.Count}");

        float enqueueDoneTime = Time.realtimeSinceStartup;

        if (verboseLogging)
        {
            Debug.Log(
                $"[RealtimeTTS] chunk {currentIndex + 1}/{chunks.Count} enqueue done " +
                $"enqueueTime={enqueueDoneTime - resampleDoneTime:F2}s " +
                $"chunkTotal={enqueueDoneTime - chunkStartTime:F2}s");
        }

        if (handle.IsCanceled || handle.GenerationId != generationId)
        {
            if (nextCoroutine != null)
                StopCoroutine(nextCoroutine);
            yield break;
        }

        if (interChunkGapSeconds > 0f)
        {
            float endTime = Time.time + interChunkGapSeconds;
            while (Time.time < endTime)
            {
                if (handle.IsCanceled || handle.GenerationId != generationId)
                {
                    if (nextCoroutine != null)
                        StopCoroutine(nextCoroutine);
                    yield break;
                }

                yield return null;
            }
        }

        if (nextIndex >= chunks.Count)
            break;

        if (nextCoroutine != null && (nextClip == null && string.IsNullOrEmpty(nextError)))
            yield return nextCoroutine;

        if (handle.IsCanceled || handle.GenerationId != generationId)
            yield break;

        currentIndex = nextIndex;
        currentClip = nextClip;
        currentError = nextError;
    }

    // Avisamos al player de que no vendrán más muestras,
    // pero NO esperamos a que drene el buffer aquí.
    realtimeAudioPlayer.MarkProducerCompleted(handle.GenerationId);

    if (verboseLogging)
    {
        Debug.Log(
            $"[RealtimeOpenAITTSClient] Stream completed (enqueue done) turn={handle.TurnId}, " +
            $"generation={handle.GenerationId}, totalTime={Time.realtimeSinceStartup - streamStartTime:F2}s");
    }

    if (enableTelemetry)
    {
        float totalTime = Time.realtimeSinceStartup - streamStartTime;
        Debug.Log($"[RealtimeTTS][Telemetry] chunks={chunks.Count} totalTime={totalTime:F2}s timeToFirstAudio={(telemetry_timeToFirstAudio<0? -1:telemetry_timeToFirstAudio):F2}s enqueueStalls={telemetry_totalEnqueueStalls}");
    }

    handle.MarkCompleted();
    RemoveHandle(handle);
}

// Pequeño helper para usar RequestSpeech como coroutine
private IEnumerator RequestSpeechCoroutine(
    string text,
    Action<AudioClip> onClip,
    Action<string> onError)
{
    AudioClip clip = null;
    string err = null;

    yield return innerTtsService.RequestSpeech(text, (c, e) =>
    {
        clip = c;
        err = e;
    });

    if (!string.IsNullOrEmpty(err))
        onError?.Invoke(err);
    else
        onClip?.Invoke(clip);
}

        private IEnumerator EnqueueSamplesGradually(float[] samples, RealtimeTTSHandle handle, int chunkIndex, int chunkCount)
        {
            int offset = 0;
            int stallFrames = 0;
            float stallStartTime = -1f;
            float stallDeadline = -1f;

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
                    if (stallFrames == 0)
                    {
                        stallStartTime = Time.realtimeSinceStartup;
                        stallDeadline = stallStartTime + enqueueStallTimeoutSeconds;
                    }

                    stallFrames++;
                    telemetry_totalEnqueueStalls++;

                    if (Time.realtimeSinceStartup >= stallDeadline)
                    {
                        Debug.LogError($"[RealtimeTTS] Enqueue stalled for more than {enqueueStallTimeoutSeconds:F1}s on chunk {chunkIndex+1}/{chunkCount}; aborting.");
                        try
                        {
                            realtimeAudioPlayer?.StopAndClear();
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning("[RealtimeTTS] Error stopping audio player: " + e.Message);
                        }

                        handle.MarkCompleted();
                        RemoveHandle(handle);
                        yield break;
                    }

                    yield return null;
                }
            }

            // Apply short fade-in / fade-out to this chunk to smooth joins between chunks.
            try
            {
                int sr = AudioSettings.outputSampleRate;
                int crossfadeSamples = Mathf.CeilToInt(sr * (chunkCrossfadeMs / 1000f));
                crossfadeSamples = Math.Min(crossfadeSamples, samples.Length / 4);

                // fade-in
                for (int i = 0; i < Math.Min(crossfadeSamples, samples.Length); i++)
                {
                    float f = (float)i / (float)crossfadeSamples;
                    samples[i] *= f;
                }

                // fade-out
                for (int i = 0; i < Math.Min(crossfadeSamples, samples.Length); i++)
                {
                    int idx = samples.Length - 1 - i;
                    float f = (float)i / (float)crossfadeSamples;
                    samples[idx] *= (1f - f);
                }
            }
            catch (Exception e)
            {
                if (verboseLogging)
                    Debug.LogWarning("[RealtimeTTS] Failed applying crossfade: " + e.Message);
            }

            if (verboseLogging && stallFrames > 0)
            {
                Debug.Log(
                    $"[RealtimeTTS] chunk {chunkIndex + 1}/{chunkCount} enqueue stalled for {stallFrames} frames");
            }
        }

        private List<string> SplitText(string text, int maxChars)
        {
            var result = new List<string>();
            string remaining = text.Trim();

            while (remaining.Length > maxChars)
            {
                // Prefer to cut at sentence-ending punctuation within range to preserve prosody.
                int cut = -1;
                int searchStart = Math.Min(maxChars, remaining.Length - 1);

                // look backwards for sentence punctuation
                for (int i = searchStart; i >= Math.Max(0, searchStart - 80); i--)
                {
                    char c = remaining[i];
                    if (c == '.' || c == '!' || c == '?' || c == ';')
                    {
                        cut = i + 1; // include punctuation
                        break;
                    }
                }

                if (cut <= 0)
                {
                    // fallback: split at last space within maxChars
                    cut = remaining.LastIndexOf(' ', maxChars);
                    if (cut <= 0)
                        cut = maxChars;
                }

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

        private IEnumerator RunStreaming(
            string text,
            RealtimeTTSHandle handle,
            IStreamingTTSService streamingService,
            Action onAudioBegan,
            Action<string> onError)
        {
            float startTime = Time.realtimeSinceStartup;
            bool began = false;

            if (verboseLogging)
                Debug.Log($"[RealtimeOpenAITTSClient] RunStreaming start generation={handle.GenerationId}");

            yield return streamingService.RequestSpeechStreamed(
                text,
                streamingAudioSource,
                () =>
                {
                    if (!began)
                    {
                        began = true;
                        float timeToFirstAudio = Time.realtimeSinceStartup - startTime;
                        
                        if (enableTelemetry)
                            telemetry_timeToFirstAudio = timeToFirstAudio;
                        
                        if (verboseLogging)
                            Debug.Log($"[RealtimeOpenAITTSClient] onAudioBegan fired after {timeToFirstAudio:F2}s");
                        
                        try { onAudioBegan?.Invoke(); } catch (Exception) { }
                    }
                },
                err => { try { onError?.Invoke(err); } catch (Exception) { } },
                () => { /* completed */ });

            handle.MarkCompleted();
            RemoveHandle(handle);
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