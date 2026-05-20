using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class StreamingOpenAITTSClient : MonoBehaviour, IStreamingTTSService
    {
        [Header("OpenAI TTS")]
        [SerializeField]
        [Tooltip("Tu API key de OpenAI. Mantener privado.")]
        private string apiKey = "YOUR_OPENAI_API_KEY";
        
        [SerializeField]
        [Tooltip("Endpoint de OpenAI para TTS. No cambiar a menos que uses proxy.")]
        private string endpoint = "https://api.openai.com/v1/audio/speech";
        
        [SerializeField]
        [Tooltip("Modelo TTS a usar. 'gpt-4o-mini-tts' es el default rápido.")]
        private string model = "gpt-4o-mini-tts";
        
        [SerializeField]
        [Tooltip("Voz (coral/sage/shimmer/echo/alloy). 'coral' es cálida y natural.")]
        private string voice = "coral";

        [SerializeField]
        [TextArea(2, 5)]
        [Tooltip("Instrucciones al modelo sobre cómo sonar. Afecta velocidad, acento y tono.")]
        private string instructions =
            "Speak in warm, natural Spanish, with clear diction and a conversational tone suitable for a historical character in VR.";

        [Header("Voice Tuning")]
        [SerializeField]
        [Range(0.25f, 4.0f)]
        [Tooltip("Velocidad de voz. 0.5=lento y profundo. 1.0=normal. 2.0=rápido y agudo.")]
        private float speed = 1.0f;

        [Header("PCM Stream Config")]
        [SerializeField]
        [Range(16000, 48000)]
        [Tooltip("Frecuencia de muestreo (Hz). Más bajo=demora pero menos CPU. Típico: 24000.")]
        private int sampleRate = 24000;
        
        [SerializeField]
        [Tooltip("Canales (1=mono). Mantener en 1 para VR.")]
        private int channels = 1;
        
        [SerializeField]
        [Range(10, 120)]
        [Tooltip("Duración max audio (s). Más alto=buffer seguro para respuestas largas.")]
        private int maxClipSeconds = 60;

        [SerializeField]
        [Range(5, 180)]
        [Tooltip("Timeout de la petición TTS (s). Súbelo si respuestas largas empiezan a cortar o fallar después de iniciar el audio; bájalo solo si quieres abortar antes.")]
        private int requestTimeoutSeconds = 90;

        [SerializeField]
        [Range(1f, 15f)]
        [Tooltip("Tiempo máximo esperando el primer chunk PCM antes de abortar el stream. Útil para detectar arranques muertos sin tocar la reproducción normal.")]
        private float firstChunkTimeoutSeconds = 4f;

        [SerializeField]
        [Range(1f, 15f)]
        [Tooltip("Tiempo máximo sin recibir PCM una vez que el stream ya empezó. Pensado para diagnóstico; no modifica el audio por sí mismo.")]
        private float chunkSilenceTimeoutSeconds = 2.5f;

        [SerializeField]
        [Range(0.05f, 2.0f)]
        [Tooltip("Buffer inicial (s) antes de reproducir. Más alto=seguro. Más bajo=rápido pero riesgo de cortes.")]
        private float prebufferSeconds = 0.35f;
        
        [SerializeField]
        [Range(0.5f, 10.0f)]
        [Tooltip("Tiempo drenar buffer (s) al final. Espera para asegurar reproducción completa.")]
        private float drainGraceSeconds = 2.0f;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Mostrar logs de chunks PCM en consola. Útil para diagnosticar problemas.")]
        private bool logChunks = true;

        [SerializeField]
        [Range(0.08f, 1.0f)]
        [Tooltip("Gap mínimo entre chunks para mostrar un warning. Sube este valor para reducir ruido de consola cuando el streaming está aceptable.")]
        private float largeChunkGapWarningSeconds = 0.25f;

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("Capturar PCM para crear clip debug. Útil para auditar calidad.")]
        private bool captureFullPcmForDebug = false;
        
        [SerializeField]
        [Tooltip("Mostrar duración esperada del audio en logs.")]
        private bool logExpectedDuration = false;
        
        [SerializeField]
        [Tooltip("Crear AudioClip debug al terminar. Acumula memoria en sesiones largas.")]
        private bool buildDebugClipOnComplete = false;

        // Public runtime accessors for UI
        public float FirstChunkTimeoutSeconds { get => firstChunkTimeoutSeconds; set => firstChunkTimeoutSeconds = Mathf.Max(0.1f, value); }
        public float ChunkSilenceTimeoutSeconds { get => chunkSilenceTimeoutSeconds; set => chunkSilenceTimeoutSeconds = Mathf.Max(0.1f, value); }
        public float PrebufferSeconds { get => prebufferSeconds; set => prebufferSeconds = Mathf.Max(0f, value); }
        public float DrainGraceSeconds { get => drainGraceSeconds; set => drainGraceSeconds = Mathf.Max(0f, value); }
        public bool LogChunks { get => logChunks; set => logChunks = value; }
        public float LargeChunkGapWarningSeconds { get => largeChunkGapWarningSeconds; set => largeChunkGapWarningSeconds = Mathf.Max(0f, value); }
        public bool CaptureFullPcmForDebug { get => captureFullPcmForDebug; set => captureFullPcmForDebug = value; }
        public bool BuildDebugClipOnComplete { get => buildDebugClipOnComplete; set => buildDebugClipOnComplete = value; }

        private StreamingAudioBuffer audioBuffer;
        // Optional external player to write samples into (shared playback path)
        private RealtimeAudioPlayer externalPlayer;
        private OVRLipSyncChunkBridge externalLipSyncBridge;
        private int externalPlayerGenerationId; // Track generation for cancellation detection
        private volatile bool streamCompleted;
        private volatile bool streamFailed;
        private volatile string streamErrorMessage;
        private long totalBytesReceived;
        private long audioReadCallCount;

        private bool hasPendingOddByte;
        private byte pendingOddByte;

        private readonly List<byte> fullPcmCapture = new List<byte>(65536);
        private AudioClip debugCapturedClip;
        private int activeTurnId = -1;
        private float requestStartedAt = -1f;
        private float firstChunkAt = -1f;
        private bool firstChunkLogged;
        private float lastChunkReceivedAt = -1f;

        public AudioClip DebugCapturedClip => debugCapturedClip;

        public IEnumerator RequestSpeechStreamedToPlayer(
            string text,
            RealtimeAudioPlayer targetPlayer,
            OVRLipSyncChunkBridge lipSyncBridge,
            int turnId,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("StreamingOpenAITTSClient: text vacío.");
                yield break;
            }

            if (targetPlayer == null)
            {
                onError?.Invoke("StreamingOpenAITTSClient: targetPlayer nulo.");
                yield break;
            }

            externalPlayer = targetPlayer;
            externalPlayerGenerationId = targetPlayer.GenerationId;
            externalLipSyncBridge = lipSyncBridge;
            if (externalLipSyncBridge != null && logChunks)
                Debug.Log($"[StreamingOpenAITTSClient] LipSync bridge attached for turn={turnId}, gen={externalPlayerGenerationId}");
            activeTurnId = turnId;
            requestStartedAt = Time.realtimeSinceStartup;
            firstChunkAt = -1f;
            firstChunkLogged = false;

            streamCompleted = false;
            streamFailed = false;

                        if (logChunks)
                        {
                            Debug.Log(
                                $"[StreamingOpenAITTSClient] STREAM START turn={turnId}, generation={externalPlayerGenerationId}, " +
                                $"textLength={text.Length}, requestTimeout={requestTimeoutSeconds}s, speed={speed:0.00}, " +
                                $"sampleRate={sampleRate}, channels={channels}");
                        }
            streamErrorMessage = null;
            Interlocked.Exchange(ref totalBytesReceived, 0);
            Interlocked.Exchange(ref audioReadCallCount, 0);
            hasPendingOddByte = false;
            pendingOddByte = 0;
            fullPcmCapture.Clear();
            lastChunkReceivedAt = -1f;

            var downloadHandler = new PcmStreamingDownloadHandler(this);

            var body = new OpenAITtsRequest
            {
                model = model,
                input = text,
                voice = voice,
                instructions = instructions,
                speed = speed,
                response_format = "pcm"
            };

            string json = JsonUtility.ToJson(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using (var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = downloadHandler;
                request.timeout = Mathf.Max(5, requestTimeoutSeconds);
                request.disposeDownloadHandlerOnDispose = true;
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                    var operation = request.SendWebRequest();
                bool requestAbortedByTimeout = false;

                while (!operation.isDone)
                {
                    if (streamFailed)
                        break;

                    float now = Time.realtimeSinceStartup;
                    bool waitingForFirstChunk = !firstChunkLogged && firstChunkAt < 0f;
                    bool firstChunkTimedOut = waitingForFirstChunk && (now - requestStartedAt) >= firstChunkTimeoutSeconds;
                    bool chunkSilenceTimedOut = firstChunkLogged && lastChunkReceivedAt > 0f && (now - lastChunkReceivedAt) >= chunkSilenceTimeoutSeconds;

                    if (firstChunkTimedOut || chunkSilenceTimedOut)
                    {
                        requestAbortedByTimeout = true;
                        streamFailed = true;
                        streamErrorMessage = firstChunkTimedOut
                            ? $"StreamingOpenAITTSClient: timeout esperando el primer chunk PCM ({firstChunkTimeoutSeconds:0.0}s)."
                            : $"StreamingOpenAITTSClient: timeout por silencio PCM ({chunkSilenceTimeoutSeconds:0.0}s) tras el último chunk.";

                        try { request.Abort(); } catch (Exception) { }

                        if (logChunks)
                            Debug.LogWarning($"[StreamingOpenAITTSClient] {streamErrorMessage}");

                        break;
                    }

                    yield return null;
                }

                if (logChunks)
                {
                    float requestElapsed = Time.realtimeSinceStartup - requestStartedAt;
                    Debug.Log(
                        $"[StreamingOpenAITTSClient] STREAM REQUEST DONE turn={turnId}, result={request.result}, " +
                        $"code={request.responseCode}, elapsed={requestElapsed:F2}s, error={request.error ?? "null"}");
                }

                if (request.result != UnityWebRequest.Result.Success && !requestAbortedByTimeout)
                {
                    streamFailed = true;
                    streamErrorMessage = request.error;
                    if (logChunks)
                        Debug.LogError($"[StreamingOpenAITTSClient] Stream request failed: {request.error}");
                    onError?.Invoke(streamErrorMessage);
                    yield break;
                }
            }

            // Wait for the network stream to end and for the player to fully drain.
            float waitTimeout = maxClipSeconds + 5f;
            float waitElapsed = 0f;
            while (waitElapsed < waitTimeout)
            {
                if (streamFailed)
                    break;

                if (streamCompleted && targetPlayer.IsPlaybackFinished)
                {
                    if (logChunks)
                        Debug.Log("[StreamingOpenAITTSClient] Playback finished.");
                    break;
                }

                waitElapsed += Time.deltaTime;
                yield return null;
            }

            if (waitElapsed >= waitTimeout && logChunks)
            {
                Debug.LogWarning(
                    $"[StreamingOpenAITTSClient] Playback wait timeout turn={turnId}, " +
                    $"streamCompleted={streamCompleted}, playerFinished={targetPlayer.IsPlaybackFinished}, " +
                    $"availableSamples={targetPlayer.AvailableSamples}");
            }

            FinalizeDiagnostics();

            onCompleted?.Invoke();
        }

        // Deprecated: use RequestSpeechStreamedToPlayer instead
        public IEnumerator RequestSpeechStreamed(
            string text,
            AudioSource targetAudioSource,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("StreamingOpenAITTSClient: text vacío.");
                activeTurnId = -1;
                yield break;
            }

            if (targetAudioSource == null)
            {
                onError?.Invoke("StreamingOpenAITTSClient: targetAudioSource nulo.");
                yield break;
            }

            streamCompleted = false;
            streamFailed = false;
            streamErrorMessage = null;
            Interlocked.Exchange(ref totalBytesReceived, 0);
            Interlocked.Exchange(ref audioReadCallCount, 0);
            hasPendingOddByte = false;
            pendingOddByte = 0;

            fullPcmCapture.Clear();
            debugCapturedClip = null;
            lastChunkReceivedAt = -1f;

            int capacitySamples = sampleRate * channels * maxClipSeconds;
            audioBuffer = new StreamingAudioBuffer(capacitySamples);

            AudioClip clip = AudioClip.Create(
                "OpenAI_TTS_Stream_PCM",
                capacitySamples,
                channels,
                sampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            targetAudioSource.Stop();
            targetAudioSource.clip = clip;
            
            // Ensure AudioSource is configured for playback
            if (targetAudioSource.volume <= 0f)
            {
                targetAudioSource.volume = 1f;
                if (logChunks)
                    Debug.Log("[StreamingOpenAITTSClient] Set AudioSource volume to 1.0");
            }
            
            if (!targetAudioSource.enabled)
            {
                targetAudioSource.enabled = true;
                if (logChunks)
                    Debug.Log("[StreamingOpenAITTSClient] Enabled AudioSource");
            }

            var downloadHandler = new PcmStreamingDownloadHandler(this);

            var body = new OpenAITtsRequest
            {
                model = model,
                input = text,
                voice = voice,
                instructions = instructions,
                speed = speed,
                response_format = "pcm"
            };

            string json = JsonUtility.ToJson(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = downloadHandler;
            request.disposeDownloadHandlerOnDispose = true;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            var operation = request.SendWebRequest();

            int prebufferSamples = Mathf.CeilToInt(sampleRate * channels * prebufferSeconds);
            bool playbackStarted = false;
            float streamCompletedAt = -1f;
            float bufferEmptySince = -1f;
            bool drainOnceLogged = false;

            while (!operation.isDone)
            {
                if (streamFailed)
                {
                    targetAudioSource.Stop();
                    onError?.Invoke(streamErrorMessage ?? "StreamingOpenAITTSClient: fallo en streaming.");
                    yield break;
                }

                if (!playbackStarted && audioBuffer != null && audioBuffer.AvailableSamples >= prebufferSamples)
                {
                    try { targetAudioSource.Play(); } catch (Exception) { }
                    playbackStarted = true;

                    if (logChunks)
                    {
                        Debug.Log(
                            $"[StreamingOpenAITTSClient] Play() started. " +
                            $"available={audioBuffer.AvailableSamples}, " +
                            $"volume={targetAudioSource.volume}, " +
                            $"enabled={targetAudioSource.enabled}, " +
                            $"isPlaying={targetAudioSource.isPlaying}");
                    }

                    onPlaybackStarted?.Invoke();
                }

                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                streamFailed = true;
                streamErrorMessage = request.error;

                if (!string.IsNullOrWhiteSpace(request.downloadHandler?.text))
                    streamErrorMessage += "\n" + request.downloadHandler.text;
            }

            if (streamFailed)
            {
                targetAudioSource.Stop();
                onError?.Invoke(streamErrorMessage ?? "StreamingOpenAITTSClient: fallo en la petición TTS.");
                yield break;
            }

            if (!playbackStarted && audioBuffer != null && audioBuffer.AvailableSamples > 0)
            {
                try { targetAudioSource.Play(); } catch (Exception) { }
                playbackStarted = true;
                onPlaybackStarted?.Invoke();
            }

            while (true)
            {
                if (streamFailed)
                {
                    targetAudioSource.Stop();
                    onError?.Invoke(streamErrorMessage ?? "StreamingOpenAITTSClient: fallo durante reproducción.");
                    yield break;
                }

                long written = audioBuffer != null ? audioBuffer.TotalSamplesWritten : 0;
                long read = audioBuffer != null ? audioBuffer.TotalSamplesRead : 0;
                bool allSamplesConsumed = streamCompleted && read >= written;
                bool nothingBuffered = audioBuffer == null || audioBuffer.AvailableSamples <= 0;

                if (streamCompleted && streamCompletedAt < 0f)
                    streamCompletedAt = Time.realtimeSinceStartup;

                if (nothingBuffered)
                {
                    if (bufferEmptySince < 0f)
                        bufferEmptySince = Time.realtimeSinceStartup;
                }
                else
                {
                    bufferEmptySince = -1f;
                }

                bool downloadGraceElapsed = streamCompletedAt > 0f &&
                                            (Time.realtimeSinceStartup - streamCompletedAt) >= drainGraceSeconds;
                bool emptyGraceElapsed = bufferEmptySince > 0f &&
                                         (Time.realtimeSinceStartup - bufferEmptySince) >= 0.10f;

                if (allSamplesConsumed && nothingBuffered && downloadGraceElapsed && emptyGraceElapsed)
                {
                    if (logChunks && !drainOnceLogged)
                    {
                        drainOnceLogged = true;
                        Debug.Log(
                            $"[StreamingOpenAITTSClient] Drain complete: " +
                            $"allSamplesConsumed={allSamplesConsumed}, " +
                            $"nothingBuffered={nothingBuffered}, " +
                            $"downloadGraceElapsed={downloadGraceElapsed}, " +
                            $"emptyGraceElapsed={emptyGraceElapsed}, " +
                            $"isPlaying={targetAudioSource.isPlaying}");
                    }

                    if (targetAudioSource.isPlaying)
                    {
                        if (logChunks)
                            Debug.Log("[StreamingOpenAITTSClient] Buffer drained but AudioSource still playing; stopping AudioSource.");

                        try { targetAudioSource.Stop(); } catch (Exception) { }
                    }

                    try { targetAudioSource.clip = null; } catch (Exception) { }

                    if (logChunks)
                        Debug.Log("[StreamingOpenAITTSClient] Exiting drain loop after stopping AudioSource.");

                    break;
                }

                yield return null;
            }

            FinalizeDiagnostics();

            onCompleted?.Invoke();
        }

        private void OnAudioRead(float[] data)
        {
            Interlocked.Increment(ref audioReadCallCount);
            audioBuffer?.Read(data);
        }

        private void OnAudioSetPosition(int newPosition)
        {
        }

        private void AppendPcmChunk(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
                return;

            // Check if we're still processing the current generation
            if (externalPlayer != null && externalPlayerGenerationId > 0 && externalPlayer.GenerationId != externalPlayerGenerationId)
            {
                if (logChunks)
                    Debug.Log($"[StreamingOpenAITTSClient] Dropping chunk: generation changed (old={externalPlayerGenerationId}, new={externalPlayer.GenerationId})");
                return; // Silently drop stale data
            }

            if (captureFullPcmForDebug)
            {
                for (int i = 0; i < dataLength; i++)
                    fullPcmCapture.Add(data[i]);
            }

            if (!firstChunkLogged)
            {
                firstChunkLogged = true;
                firstChunkAt = Time.realtimeSinceStartup;

                if (logChunks)
                {
                    Debug.Log(
                        $"[StreamingOpenAITTSClient] FIRST PCM CHUNK turn={activeTurnId}, " +
                        $"elapsed={(firstChunkAt - requestStartedAt):F2}s, bytes={dataLength}, " +
                        $"generation={(externalPlayer != null ? externalPlayer.GenerationId : -1)}");
                }

                MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.FirstChunkReceived, activeTurnId, externalPlayer != null ? externalPlayer.GenerationId : externalPlayerGenerationId, dataLength);
            }

            int totalBytesToProcess = dataLength + (hasPendingOddByte ? 1 : 0);
            if (totalBytesToProcess < 2)
            {
                if (dataLength == 1)
                {
                    pendingOddByte = data[0];
                    hasPendingOddByte = true;
                }
                return;
            }

            int sampleCount = totalBytesToProcess / 2;
            float[] samples = new float[sampleCount];
            int sampleIndex = 0;
            int sourceIndex = 0;

            if (hasPendingOddByte)
            {
                short pcm = (short)(pendingOddByte | (data[0] << 8));
                samples[sampleIndex++] = pcm / 32768f;
                sourceIndex = 1;
                hasPendingOddByte = false;
            }

            int evenByteLimit = dataLength - ((dataLength - sourceIndex) % 2);

            for (int i = sourceIndex; i + 1 < evenByteLimit; i += 2)
            {
                short pcm = (short)(data[i] | (data[i + 1] << 8));
                samples[sampleIndex++] = pcm / 32768f;
            }

            if (evenByteLimit < dataLength)
            {
                pendingOddByte = data[dataLength - 1];
                hasPendingOddByte = true;
            }

            if (sampleIndex > 0)
            {
                try
                {
                    // Trim to actual samples
                    float[] actual = new float[sampleIndex];
                    Array.Copy(samples, 0, actual, 0, sampleIndex);

                    int outputSr = AudioSettings.outputSampleRate;
                    float[] toWrite = actual;

                    // Resample if incoming sampleRate differs from device output rate
                    if (sampleRate != outputSr)
                    {
                        toWrite = ResampleFloatArray(actual, sampleIndex, sampleRate, outputSr);
                    }

                    if (externalPlayer != null)
                    {
                        int gen = externalPlayer.GenerationId;
                        int written = externalPlayer.WriteSomeSamples(toWrite, 0, toWrite.Length, gen);
                        if (written < toWrite.Length && logChunks)
                        {
                            Debug.LogWarning($"[StreamingOpenAITTSClient] Player buffer full: wrote {written}/{toWrite.Length} samples, dropping remainder.");
                        }
                    }
                    else if (audioBuffer != null)
                    {
                        int written = audioBuffer.WriteSome(toWrite, 0, toWrite.Length);
                        if (written < toWrite.Length && logChunks)
                        {
                            Debug.LogWarning($"[StreamingOpenAITTSClient] Buffer full: wrote {written}/{toWrite.Length} samples, dropping remainder.");
                        }
                    }

                }
                catch (Exception e)
                {
                    Debug.LogWarning("[StreamingOpenAITTSClient] Error writing PCM samples: " + e.Message);
                }
            }

            // Telemetry: detect gaps between consecutive ReceiveData calls
            try
            {
                float now = Time.realtimeSinceStartup;
                if (lastChunkReceivedAt > 0f)
                {
                    float gap = now - lastChunkReceivedAt;
                    // Log only meaningful gaps so the console stays readable.
                    if (gap > largeChunkGapWarningSeconds && logChunks)
                    {
                        int buffered = externalPlayer != null ? externalPlayer.AvailableSamples : (audioBuffer != null ? audioBuffer.AvailableSamples : 0);
                        Debug.LogWarning($"[StreamingOpenAITTSClient] LARGE CHUNK GAP: {gap:F3}s, bytes={dataLength}, bufferedSamples={buffered}");
                    }
                    else if (gap > 0.08f && logChunks)
                    {
                        Debug.Log($"[StreamingOpenAITTSClient] chunk gap: {gap:F3}s, bytes={dataLength}");
                    }
                }
                    // Telemetry: record chunk gap in ms (value)
                    int gapMs = lastChunkReceivedAt > 0f ? (int)((now - lastChunkReceivedAt) * 1000f) : 0;
                    MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.ChunkReceived, activeTurnId, externalPlayer != null ? externalPlayer.GenerationId : externalPlayerGenerationId, gapMs);
                    lastChunkReceivedAt = now;
            }
            catch (Exception) { }

            Interlocked.Add(ref totalBytesReceived, dataLength);

            if (logChunks)
            {
                long total = Interlocked.Read(ref totalBytesReceived);
                int buffered = externalPlayer != null ? externalPlayer.AvailableSamples : (audioBuffer != null ? audioBuffer.AvailableSamples : 0);
                long totalWritten = audioBuffer != null ? audioBuffer.TotalSamplesWritten : 0;
                long totalRead = audioBuffer != null ? audioBuffer.TotalSamplesRead : 0;
                Debug.Log(
                    $"[StreamingOpenAITTSClient] +{dataLength} bytes PCM, total={total}, " +
                    $"bufferedSamples={buffered}, written={totalWritten}, " +
                    $"read={totalRead}, pendingOddByte={hasPendingOddByte}");
            }
        }

        private void FinalizeDiagnostics()
        {
            if (!captureFullPcmForDebug)
                return;

            if (hasPendingOddByte)
            {
                Debug.LogWarning("[StreamingOpenAITTSClient] Quedó 1 byte PCM suelto al final del stream. Se descartará para el clip debug.");
            }

            int usableBytes = fullPcmCapture.Count - (fullPcmCapture.Count % 2);
            int totalSamples = usableBytes / 2;
            float expectedDuration = totalSamples / (float)(sampleRate * channels);

            if (logExpectedDuration)
            {
                long audioReadCalls = Interlocked.Read(ref audioReadCallCount);
                long written = audioBuffer != null ? audioBuffer.TotalSamplesWritten : 0;
                long read = audioBuffer != null ? audioBuffer.TotalSamplesRead : 0;
                Debug.Log(
                    $"[StreamingOpenAITTSClient] PCM total bytes={fullPcmCapture.Count}, usableBytes={usableBytes}, " +
                    $"samples={totalSamples}, expectedDuration={expectedDuration:F2}s, " +
                    $"written={written}, read={read}, " +
                    $"audioReadCalls={audioReadCalls}");
            }

            if (buildDebugClipOnComplete)
            {
                debugCapturedClip = BuildClipFromCapturedPcm("OpenAI_TTS_DebugCaptured");

                if (debugCapturedClip != null)
                {
                    Debug.Log($"[StreamingOpenAITTSClient] Debug clip creado. length={debugCapturedClip.length:F2}s");
                }
                else
                {
                    Debug.LogWarning("[StreamingOpenAITTSClient] No se pudo crear debugCapturedClip.");
                }
            }
        }

        private AudioClip BuildClipFromCapturedPcm(string clipName)
        {
            int usableBytes = fullPcmCapture.Count - (fullPcmCapture.Count % 2);
            if (usableBytes <= 0)
                return null;

            int totalSamples = usableBytes / 2;
            float[] samples = new float[totalSamples];

            int s = 0;
            for (int i = 0; i + 1 < usableBytes; i += 2)
            {
                short pcm = (short)(fullPcmCapture[i] | (fullPcmCapture[i + 1] << 8));
                samples[s++] = pcm / 32768f;
            }

            int frames = totalSamples / channels;
            if (frames <= 0)
                return null;

            AudioClip clip = AudioClip.Create(clipName, frames, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // Simple linear resampler for mono float arrays.
        private float[] ResampleFloatArray(float[] src, int srcCount, int srcRate, int dstRate)
        {
            if (src == null || srcCount <= 0)
                return Array.Empty<float>();

            if (srcRate == dstRate)
            {
                if (srcCount == src.Length)
                    return src;
                var copy = new float[srcCount];
                Array.Copy(src, 0, copy, 0, srcCount);
                return copy;
            }

            float ratio = (float)srcRate / dstRate;
            int dstCount = Mathf.CeilToInt(srcCount / ratio);
            if (dstCount <= 0)
                return Array.Empty<float>();

            float[] dst = new float[dstCount];
            for (int i = 0; i < dstCount; i++)
            {
                float srcPos = i * ratio;
                int i0 = Mathf.Clamp((int)srcPos, 0, srcCount - 1);
                int i1 = Mathf.Min(i0 + 1, srcCount - 1);
                float t = srcPos - i0;
                dst[i] = Mathf.Lerp(src[i0], src[i1], t);
            }

            return dst;
        }

        public void PlayDebugCapturedClip(AudioSource targetAudioSource)
        {
            Debug.Log("[StreamingOpenAITTSClient] PlayDebugCapturedClip llamado.");

            if (targetAudioSource == null)
            {
                Debug.LogWarning("[StreamingOpenAITTSClient] PlayDebugCapturedClip: AudioSource nulo.");
                return;
            }

            if (debugCapturedClip == null)
            {
                Debug.LogWarning("[StreamingOpenAITTSClient] No hay debugCapturedClip generado.");
                return;
            }

            Debug.Log($"[StreamingOpenAITTSClient] Reproduciendo debugCapturedClip. length={debugCapturedClip.length:F2}s");

            targetAudioSource.Stop();
            targetAudioSource.clip = debugCapturedClip;
            targetAudioSource.Play();
        }

        private void NotifyComplete()
        {
            streamCompleted = true;
            
            if (externalPlayer != null)
            {
                externalPlayer.MarkStreamingComplete(externalPlayerGenerationId);
            }
            
            if (logChunks)
            {
                Debug.Log(
                    $"[StreamingOpenAITTSClient] Stream COMPLETED turn={activeTurnId}, " +
                    $"bytes={Interlocked.Read(ref totalBytesReceived)}, " +
                    $"buffered={(externalPlayer != null ? externalPlayer.AvailableSamples : -1)}");
            }
        }

        private void NotifyError(string error)
        {
            streamFailed = true;
            streamErrorMessage = error;
        }

        [Serializable]
        private class OpenAITtsRequest
        {
            public string model;
            public string input;
            public string voice;
            public string instructions;
            public float speed;
            public string response_format;
        }

        private class PcmStreamingDownloadHandler : DownloadHandlerScript
        {
            private readonly StreamingOpenAITTSClient owner;

            public PcmStreamingDownloadHandler(StreamingOpenAITTSClient owner) : base()
            {
                this.owner = owner;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                    return true;

                try
                {

                    if (owner.logChunks)
                    {
                        Debug.Log($"[StreamingOpenAITTSClient] ReceiveData bytes={dataLength}");
                    }

                    owner.AppendPcmChunk(data, dataLength);
                    return true;
                }
                catch (Exception e)
                {
                    owner.NotifyError("Error procesando chunk PCM: " + e.Message);
                    return false;
                }
            }

            protected override void CompleteContent()
            {
                owner.NotifyComplete();
            }
        }
    }
}