using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public enum ElevenLabsOutputFormat
    {
        [InspectorName("MP3 44.1 kHz (128kbps)")]
        Mp3_44100_128,

        [InspectorName("MP3 44.1 kHz (192kbps)")]
        Mp3_44100_192,

        [InspectorName("WAV 8 kHz (Lossless)")]
        Wav_8000,

        [InspectorName("WAV 16 kHz")]
        Wav_16000,

        [InspectorName("WAV 22.05 kHz")]
        Wav_22050,

        [InspectorName("WAV 24 kHz")]
        Wav_24000,

        [InspectorName("WAV 44.1 kHz")]
        Wav_44100,

        [InspectorName("PCM 22.05 kHz")]
        Pcm_22050,

        [InspectorName("PCM 44.1 kHz")]
        Pcm_44100,
    }

    public class StreamingElevenLabsTTSClient : MonoBehaviour, IStreamingTTSService
    {
        private const string ApiKeyEnvironmentVariable = "ELEVENLABS_API_KEY";
        private string apiKey = string.Empty;

        private static ConversationSettings Settings => ConversationSettings.Instance;

        private string voiceId
        {
            get => Settings.StreamingElevenLabsTts.voiceId;
            set => Settings.StreamingElevenLabsTts.voiceId = value;
        }

        private string modelId
        {
            get => Settings.StreamingElevenLabsTts.modelId;
            set => Settings.StreamingElevenLabsTts.modelId = value;
        }

        private string languageCode
        {
            get => Settings.StreamingElevenLabsTts.languageCode;
            set => Settings.StreamingElevenLabsTts.languageCode = value;
        }

        private bool verboseLogging
        {
            get => Settings.StreamingElevenLabsTts.verboseLogging;
            set => Settings.StreamingElevenLabsTts.verboseLogging = value;
        }

        private int optimizeStreamingLatency
        {
            get => Settings.StreamingElevenLabsTts.optimizeStreamingLatency;
            set => Settings.StreamingElevenLabsTts.optimizeStreamingLatency = value;
        }

        private float speed
        {
            get => Settings.StreamingElevenLabsTts.speed;
            set => Settings.StreamingElevenLabsTts.speed = value;
        }

        private float stability
        {
            get => Settings.StreamingElevenLabsTts.stability;
            set => Settings.StreamingElevenLabsTts.stability = value;
        }

        private float similarityBoost
        {
            get => Settings.StreamingElevenLabsTts.similarityBoost;
            set => Settings.StreamingElevenLabsTts.similarityBoost = value;
        }

        private float style
        {
            get => Settings.StreamingElevenLabsTts.style;
            set => Settings.StreamingElevenLabsTts.style = value;
        }

        private bool useSpeakerBoost
        {
            get => Settings.StreamingElevenLabsTts.useSpeakerBoost;
            set => Settings.StreamingElevenLabsTts.useSpeakerBoost = value;
        }

        private ElevenLabsOutputFormat outputFormat
        {
            get => Settings.StreamingElevenLabsTts.outputFormat;
            set => Settings.StreamingElevenLabsTts.outputFormat = value;
        }

        private int requestTimeoutSeconds
        {
            get => Settings.StreamingElevenLabsTts.requestTimeoutSeconds;
            set => Settings.StreamingElevenLabsTts.requestTimeoutSeconds = value;
        }

        private float firstChunkTimeoutSeconds
        {
            get => Settings.StreamingElevenLabsTts.firstChunkTimeoutSeconds;
            set => Settings.StreamingElevenLabsTts.firstChunkTimeoutSeconds = value;
        }

        private float chunkSilenceTimeoutSeconds
        {
            get => Settings.StreamingElevenLabsTts.chunkSilenceTimeoutSeconds;
            set => Settings.StreamingElevenLabsTts.chunkSilenceTimeoutSeconds = value;
        }

        private float prebufferSeconds
        {
            get => Settings.StreamingElevenLabsTts.prebufferSeconds;
            set => Settings.StreamingElevenLabsTts.prebufferSeconds = value;
        }

        private float drainGraceSeconds
        {
            get => Settings.StreamingElevenLabsTts.drainGraceSeconds;
            set => Settings.StreamingElevenLabsTts.drainGraceSeconds = value;
        }

        private float largeChunkGapWarningSeconds
        {
            get => Settings.StreamingElevenLabsTts.largeChunkGapWarningSeconds;
            set => Settings.StreamingElevenLabsTts.largeChunkGapWarningSeconds = value;
        }

        private bool captureFullPcmForDebug
        {
            get => Settings.StreamingElevenLabsTts.captureFullPcmForDebug;
            set => Settings.StreamingElevenLabsTts.captureFullPcmForDebug = value;
        }

        private bool logExpectedDuration
        {
            get => Settings.StreamingElevenLabsTts.logExpectedDuration;
            set => Settings.StreamingElevenLabsTts.logExpectedDuration = value;
        }

        private bool buildDebugClipOnComplete
        {
            get => Settings.StreamingElevenLabsTts.buildDebugClipOnComplete;
            set => Settings.StreamingElevenLabsTts.buildDebugClipOnComplete = value;
        }

        private int maxClipSeconds
        {
            get => Settings.StreamingElevenLabsTts.maxClipSeconds;
            set => Settings.StreamingElevenLabsTts.maxClipSeconds = value;
        }

        [Header("Optional Continuity")]
        [SerializeField]
        [Tooltip("Texto anterior para mejorar continuidad entre requests. Opcional.")]
        [TextArea(2, 4)]
        private string previousText = string.Empty;

        [SerializeField]
        [Tooltip("Texto posterior para mejorar continuidad entre requests. Opcional.")]
        [TextArea(2, 4)]
        private string nextText = string.Empty;

        [SerializeField]
        [Tooltip("Seed opcional para repetir resultados de forma aproximada.")]
        private int seed = -1;

        private StreamingAudioBuffer audioBuffer;
        private AudioClip debugCapturedClip;
        private bool streamCompleted;
        private bool streamFailed;
        private string streamErrorMessage;
        private bool hasPendingOddByte;
        private byte pendingOddByte;
        private readonly List<byte> fullPcmCapture = new List<byte>(65536);
        private long totalBytesReceived;
        private long audioReadCallCount;
        private float requestStartedAt = -1f;
        private float firstChunkAt = -1f;
        private bool firstChunkLogged;
        private float lastChunkReceivedAt = -1f;
        private int activeGenerationId;
        private bool playbackStarted;
        private AudioSource activeAudioSource;
        private int activeAudioSourceGenerationId;

        public bool VerboseLogging { get => verboseLogging; set => verboseLogging = value; }
        public int OptimizeStreamingLatency { get => optimizeStreamingLatency; set => optimizeStreamingLatency = Mathf.Clamp(value, -1, 4); }
        public float Speed { get => speed; set => speed = Mathf.Clamp(value, 0.5f, 1.5f); }
        public float Stability { get => stability; set => stability = Mathf.Clamp01(value); }
        public float SimilarityBoost { get => similarityBoost; set => similarityBoost = Mathf.Clamp01(value); }
        public float Style { get => style; set => style = Mathf.Clamp01(value); }
        public bool UseSpeakerBoost { get => useSpeakerBoost; set => useSpeakerBoost = value; }
        public ElevenLabsOutputFormat OutputFormat { get => outputFormat; set => outputFormat = value; }
        public float PrebufferSeconds { get => prebufferSeconds; set => prebufferSeconds = Mathf.Max(0f, value); }
        public float DrainGraceSeconds { get => drainGraceSeconds; set => drainGraceSeconds = Mathf.Max(0f, value); }
        public float FirstChunkTimeoutSeconds { get => firstChunkTimeoutSeconds; set => firstChunkTimeoutSeconds = Mathf.Max(0.1f, value); }
        public float ChunkSilenceTimeoutSeconds { get => chunkSilenceTimeoutSeconds; set => chunkSilenceTimeoutSeconds = Mathf.Max(0.1f, value); }
        public float LargeChunkGapWarningSeconds { get => largeChunkGapWarningSeconds; set => largeChunkGapWarningSeconds = Mathf.Max(0f, value); }
        public bool CaptureFullPcmForDebug { get => captureFullPcmForDebug; set => captureFullPcmForDebug = value; }
        public bool BuildDebugClipOnComplete { get => buildDebugClipOnComplete; set => buildDebugClipOnComplete = value; }
        public string VoiceId { get => voiceId; set => voiceId = value; }
        public string ModelId { get => modelId; set => modelId = value; }
        public string LanguageCode { get => languageCode; set => languageCode = value; }

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(GetApiKey()))
                Debug.LogWarning($"[StreamingElevenLabsTTSClient] API key is empty. Set {ApiKeyEnvironmentVariable} in your local environment.");

            if (string.IsNullOrWhiteSpace(voiceId))
                Debug.LogWarning("[StreamingElevenLabsTTSClient] Voice ID is empty.");
        }

        public void InterruptPlayback()
        {
            activeGenerationId++;
            StopActiveAudioSource();
            ClearStreamState();
        }

        public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
        {
            yield return StartCoroutine(RequestSpeechAsClipCoroutine(text, onComplete));
        }

        public IEnumerator RequestSpeechStreamed(
            string text,
            AudioSource targetAudioSource,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("StreamingElevenLabsTTSClient: empty text.");
                yield break;
            }

            if (targetAudioSource == null)
            {
                onError?.Invoke("StreamingElevenLabsTTSClient: targetAudioSource is null.");
                yield break;
            }

            string resolvedApiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                onError?.Invoke($"StreamingElevenLabsTTSClient: API key not configured. Set {ApiKeyEnvironmentVariable}.");
                yield break;
            }

            int generationId = ++activeGenerationId;
            activeAudioSource = targetAudioSource;
            activeAudioSourceGenerationId = generationId;
            playbackStarted = false;
            ClearStreamState();

            if (IsPcmFormat(outputFormat))
            {
                yield return StartCoroutine(RequestPcmStreamCoroutine(text, targetAudioSource, generationId, onPlaybackStarted, onError, onCompleted));
                yield break;
            }

            yield return StartCoroutine(RequestBufferedAudioCoroutine(text, targetAudioSource, generationId, onPlaybackStarted, onError, onCompleted));
        }

        private IEnumerator RequestSpeechAsClipCoroutine(string text, Action<AudioClip, string> onComplete)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke(null, "StreamingElevenLabsTTSClient: empty text.");
                yield break;
            }

            if (IsPcmFormat(outputFormat))
            {
                onComplete?.Invoke(null, "StreamingElevenLabsTTSClient: RequestSpeech returns an AudioClip only for non-PCM formats. Use RequestSpeechStreamed for PCM.");
                yield break;
            }

            int generationId = ++activeGenerationId;
            ClearStreamState();

            bool finished = false;
            AudioClip clipResult = null;
            string errorResult = null;

            yield return StartCoroutine(RequestBufferedAudioCoroutine(
                text,
                null,
                generationId,
                null,
                err => errorResult = err,
                () => finished = true,
                clip => clipResult = clip));

            if (!string.IsNullOrWhiteSpace(errorResult))
            {
                onComplete?.Invoke(null, errorResult);
                yield break;
            }

            if (!finished)
            {
                onComplete?.Invoke(null, "StreamingElevenLabsTTSClient: the request could not be completed.");
                yield break;
            }

            onComplete?.Invoke(clipResult, null);
        }

        private IEnumerator RequestPcmStreamCoroutine(
            string text,
            AudioSource targetAudioSource,
            int generationId,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted)
        {
            string resolvedApiKey = GetApiKey();

            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                onError?.Invoke($"StreamingElevenLabsTTSClient: API key not configured. Set {ApiKeyEnvironmentVariable}.");
                yield break;
            }

            int sampleRate = GetPcmSampleRate(outputFormat);
            int channels = 1;
            int capacitySamples = sampleRate * Mathf.Max(1, maxClipSeconds);
            audioBuffer = new StreamingAudioBuffer(capacitySamples);

            AudioClip clip = AudioClip.Create(
                "ElevenLabs_PCM_Stream",
                capacitySamples,
                channels,
                sampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            targetAudioSource.Stop();
            targetAudioSource.clip = clip;

            var request = BuildRequest(text);
            string url = BuildRequestUrl();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(request);

            using (var unityRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                unityRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                unityRequest.downloadHandler = new PcmStreamingDownloadHandler(this);
                unityRequest.timeout = Mathf.Max(5, requestTimeoutSeconds);
                unityRequest.disposeDownloadHandlerOnDispose = true;
                unityRequest.SetRequestHeader("Content-Type", "application/json");
                unityRequest.SetRequestHeader("xi-api-key", resolvedApiKey);

                if (verboseLogging)
                {
                    Debug.Log(
                        $"[StreamingElevenLabsTTSClient] STREAM START voiceId={voiceId}, modelId={modelId}, output={GetOutputFormatWireValue()}, " +
                        $"speed={speed:0.00}, stability={stability:0.00}, similarity={similarityBoost:0.00}, style={style:0.00}, " +
                        $"speakerBoost={useSpeakerBoost}, sampleRate={sampleRate}");
                }

                requestStartedAt = Time.realtimeSinceStartup;
                firstChunkAt = -1f;
                firstChunkLogged = false;
                streamCompleted = false;
                streamFailed = false;
                streamErrorMessage = null;
                hasPendingOddByte = false;
                pendingOddByte = 0;
                fullPcmCapture.Clear();
                lastChunkReceivedAt = -1f;
                Interlocked.Exchange(ref totalBytesReceived, 0);
                Interlocked.Exchange(ref audioReadCallCount, 0);

                var operation = unityRequest.SendWebRequest();
                bool requestAbortedByTimeout = false;
                bool playbackRequested = false;
                float prebufferSamples = sampleRate * Mathf.Max(0f, prebufferSeconds);

                while (!operation.isDone)
                {
                    if (generationId != activeGenerationId)
                        yield break;

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
                            ? $"StreamingElevenLabsTTSClient: timeout waiting for the first PCM chunk ({firstChunkTimeoutSeconds:0.0}s)."
                            : $"StreamingElevenLabsTTSClient: PCM silence timeout ({chunkSilenceTimeoutSeconds:0.0}s) after the last chunk.";

                        try { unityRequest.Abort(); } catch (Exception) { }

                        if (verboseLogging)
                            Debug.LogWarning($"[StreamingElevenLabsTTSClient] {streamErrorMessage}");

                        break;
                    }

                    if (!playbackRequested && audioBuffer != null && audioBuffer.AvailableSamples >= prebufferSamples)
                    {
                        try { targetAudioSource.Play(); } catch (Exception) { }
                        playbackRequested = true;
                        playbackStarted = true;

                        if (verboseLogging)
                        {
                            Debug.Log(
                                $"[StreamingElevenLabsTTSClient] Play() started. available={audioBuffer.AvailableSamples}, " +
                                $"volume={targetAudioSource.volume}, enabled={targetAudioSource.enabled}, isPlaying={targetAudioSource.isPlaying}");
                        }

                        onPlaybackStarted?.Invoke();
                    }

                    yield return null;
                }

                if (verboseLogging)
                {
                    float requestElapsed = Time.realtimeSinceStartup - requestStartedAt;
                    Debug.Log(
                        $"[StreamingElevenLabsTTSClient] STREAM REQUEST DONE result={unityRequest.result}, code={unityRequest.responseCode}, " +
                        $"elapsed={requestElapsed:F2}s, error={unityRequest.error ?? "null"}");
                }

                if (unityRequest.result != UnityWebRequest.Result.Success && !requestAbortedByTimeout)
                {
                    streamFailed = true;
                    streamErrorMessage = unityRequest.responseCode == 402
                        ? "StreamingElevenLabsTTSClient: HTTP 402 Payment Required. Check your plan, payment method, and ElevenLabs quota."
                        : (string.IsNullOrWhiteSpace(unityRequest.error) ? "StreamingElevenLabsTTSClient: TTS request failed." : unityRequest.error);
                    onError?.Invoke(streamErrorMessage);
                    yield break;
                }
            }

            float waitTimeout = Mathf.Max(10f, maxClipSeconds + 5f);
            float waitElapsed = 0f;
            while (waitElapsed < waitTimeout)
            {
                if (generationId != activeGenerationId)
                    yield break;

                if (streamFailed)
                    break;

                if (streamCompleted && targetAudioSource.isPlaying == false && audioBuffer != null && audioBuffer.AvailableSamples <= 0)
                    break;

                waitElapsed += Time.deltaTime;
                yield return null;
            }

            if (waitElapsed >= waitTimeout && verboseLogging)
            {
                Debug.LogWarning(
                    $"[StreamingElevenLabsTTSClient] Playback wait timeout. streamCompleted={streamCompleted}, " +
                    $"availableSamples={(audioBuffer != null ? audioBuffer.AvailableSamples : 0)}");
            }

            FinalizeDiagnostics();
            onCompleted?.Invoke();
        }

        private IEnumerator RequestBufferedAudioCoroutine(
            string text,
            AudioSource targetAudioSource,
            int generationId,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted,
            Action<AudioClip> onClipReady = null)
        {
            string resolvedApiKey = GetApiKey();

            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                onError?.Invoke($"StreamingElevenLabsTTSClient: API key no configurada. Define {ApiKeyEnvironmentVariable}.");
                yield break;
            }

            string requestJson = BuildRequest(text);
            string url = BuildRequestUrl();
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);
            AudioType audioType = GetAudioTypeForOutputFormat(outputFormat);

            using (var unityRequest = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                unityRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                var downloadHandler = new DownloadHandlerAudioClip(url, audioType)
                {
                    streamAudio = false
                };
                unityRequest.downloadHandler = downloadHandler;
                unityRequest.timeout = Mathf.Max(5, requestTimeoutSeconds);
                unityRequest.disposeDownloadHandlerOnDispose = true;
                unityRequest.SetRequestHeader("Content-Type", "application/json");
                unityRequest.SetRequestHeader("xi-api-key", resolvedApiKey);

                if (verboseLogging)
                {
                    Debug.Log(
                        $"[StreamingElevenLabsTTSClient] BUFFERED START voiceId={voiceId}, modelId={modelId}, output={GetOutputFormatWireValue()}, " +
                        $"speed={speed:0.00}, stability={stability:0.00}, similarity={similarityBoost:0.00}, style={style:0.00}");
                }

                requestStartedAt = Time.realtimeSinceStartup;
                streamCompleted = false;
                streamFailed = false;
                streamErrorMessage = null;
                Interlocked.Exchange(ref totalBytesReceived, 0);

                yield return unityRequest.SendWebRequest();

                if (generationId != activeGenerationId)
                    yield break;

                if (unityRequest.result != UnityWebRequest.Result.Success)
                {
                    streamFailed = true;
                    streamErrorMessage = unityRequest.responseCode == 402
                        ? "StreamingElevenLabsTTSClient: HTTP 402 Payment Required. Check your plan, payment method, and ElevenLabs quota."
                        : (string.IsNullOrWhiteSpace(unityRequest.error) ? "StreamingElevenLabsTTSClient: TTS request failed." : unityRequest.error);
                    onError?.Invoke(streamErrorMessage);
                    yield break;
                }

                AudioClip clip = DownloadHandlerAudioClip.GetContent(unityRequest);
                if (clip == null)
                {
                    onError?.Invoke("StreamingElevenLabsTTSClient: the response did not return a valid AudioClip.");
                    yield break;
                }

                if (onClipReady != null)
                    onClipReady.Invoke(clip);

                if (targetAudioSource != null)
                {
                    targetAudioSource.Stop();
                    targetAudioSource.clip = clip;
                    try { targetAudioSource.Play(); } catch (Exception) { }
                    onPlaybackStarted?.Invoke();
                }

                if (verboseLogging)
                {
                    Debug.Log(
                        $"[StreamingElevenLabsTTSClient] Buffered clip ready. length={clip.length:0.00}s, samples={clip.samples}, channels={clip.channels}, freq={clip.frequency}");
                }

                if (captureFullPcmForDebug && outputFormat == ElevenLabsOutputFormat.Pcm_22050)
                {
                    // PCM debug capture is handled by the streaming path.
                }

                streamCompleted = true;
                onCompleted?.Invoke();
            }
        }

        private string BuildRequest(string text)
        {
            var sb = new StringBuilder();
            sb.Append('{');
            AppendJsonField(sb, "text", text, true);
            AppendJsonField(sb, "model_id", modelId);

            if (!string.IsNullOrWhiteSpace(languageCode))
                AppendJsonField(sb, "language_code", languageCode);

            sb.Append(",\"voice_settings\":{");
            sb.Append("\"speed\":").Append(speed.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"stability\":").Append(stability.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"similarity_boost\":").Append(similarityBoost.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"style\":").Append(style.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"use_speaker_boost\":").Append(useSpeakerBoost ? "true" : "false");
            sb.Append('}');

            if (seed >= 0)
                sb.Append(",\"seed\":").Append(seed);

            if (!string.IsNullOrWhiteSpace(previousText))
                AppendJsonField(sb, "previous_text", previousText);

            if (!string.IsNullOrWhiteSpace(nextText))
                AppendJsonField(sb, "next_text", nextText);

            sb.Append('}');
            return sb.ToString();
        }

        private string BuildRequestUrl()
        {
            var sb = new StringBuilder();
            sb.Append("https://api.elevenlabs.io/v1/text-to-speech/");
            sb.Append(UnityWebRequest.EscapeURL(voiceId));
            sb.Append("?output_format=").Append(GetOutputFormatWireValue());
            sb.Append("&enable_logging=").Append(verboseLogging ? "true" : "false");

            if (optimizeStreamingLatency >= 0)
                sb.Append("&optimize_streaming_latency=").Append(Mathf.Clamp(optimizeStreamingLatency, 0, 4));

            return sb.ToString();
        }

        private static void AppendJsonField(StringBuilder sb, string key, string value, bool first = false)
        {
            if (!first)
                sb.Append(',');

            sb.Append('"').Append(key).Append("\":");
            sb.Append('"').Append(EscapeJson(value)).Append('"');
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        private static bool IsPcmFormat(ElevenLabsOutputFormat format)
        {
            return format == ElevenLabsOutputFormat.Pcm_22050 || format == ElevenLabsOutputFormat.Pcm_44100;
        }

        private static string GetOutputFormatWireValue(ElevenLabsOutputFormat format)
        {
            return format switch
            {
                ElevenLabsOutputFormat.Mp3_44100_128 => "mp3_44100_128",
                ElevenLabsOutputFormat.Mp3_44100_192 => "mp3_44100_192",
                ElevenLabsOutputFormat.Wav_8000 => "wav_8000",
                ElevenLabsOutputFormat.Wav_16000 => "wav_16000",
                ElevenLabsOutputFormat.Wav_22050 => "wav_22050",
                ElevenLabsOutputFormat.Wav_24000 => "wav_24000",
                ElevenLabsOutputFormat.Wav_44100 => "wav_44100",
                ElevenLabsOutputFormat.Pcm_44100 => "pcm_44100",
                _ => "pcm_22050",
            };
        }

        private string GetOutputFormatWireValue()
        {
            return GetOutputFormatWireValue(outputFormat);
        }

        private static AudioType GetAudioTypeForOutputFormat(ElevenLabsOutputFormat format)
        {
            return format switch
            {
                ElevenLabsOutputFormat.Mp3_44100_128 => AudioType.MPEG,
                ElevenLabsOutputFormat.Mp3_44100_192 => AudioType.MPEG,
                ElevenLabsOutputFormat.Wav_8000 => AudioType.WAV,
                ElevenLabsOutputFormat.Wav_16000 => AudioType.WAV,
                ElevenLabsOutputFormat.Wav_22050 => AudioType.WAV,
                ElevenLabsOutputFormat.Wav_24000 => AudioType.WAV,
                ElevenLabsOutputFormat.Wav_44100 => AudioType.WAV,
                _ => AudioType.UNKNOWN,
            };
        }

        private static int GetPcmSampleRate(ElevenLabsOutputFormat format)
        {
            return format == ElevenLabsOutputFormat.Pcm_44100 ? 44100 : 22050;
        }

        private void ClearStreamState()
        {
            audioBuffer = null;
            streamCompleted = false;
            streamFailed = false;
            streamErrorMessage = null;
            hasPendingOddByte = false;
            pendingOddByte = 0;
            fullPcmCapture.Clear();
            totalBytesReceived = 0;
            audioReadCallCount = 0;
            firstChunkAt = -1f;
            firstChunkLogged = false;
            lastChunkReceivedAt = -1f;
            requestStartedAt = Time.realtimeSinceStartup;
            playbackStarted = false;
            debugCapturedClip = null;
        }

        private void StopActiveAudioSource()
        {
            if (activeAudioSource != null)
            {
                try
                {
                    activeAudioSource.Stop();
                    activeAudioSource.clip = null;
                }
                catch (Exception)
                {
                }
            }
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

            if (activeAudioSource != null && activeAudioSourceGenerationId > 0 && activeAudioSource == null)
                return;

            if (captureFullPcmForDebug)
            {
                for (int i = 0; i < dataLength; i++)
                    fullPcmCapture.Add(data[i]);
            }

            if (!firstChunkLogged)
            {
                firstChunkLogged = true;
                firstChunkAt = Time.realtimeSinceStartup;

                if (verboseLogging)
                {
                    Debug.Log(
                        $"[StreamingElevenLabsTTSClient] FIRST PCM CHUNK elapsed={(firstChunkAt - requestStartedAt):F2}s, bytes={dataLength}");
                }
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

            if (sampleIndex <= 0)
                return;

            float[] actual = new float[sampleIndex];
            Array.Copy(samples, 0, actual, 0, sampleIndex);

            int outputSr = AudioSettings.outputSampleRate;
            int sourceSr = GetPcmSampleRate(outputFormat);
            float[] toWrite = actual;

            if (sourceSr != outputSr)
                toWrite = ResampleFloatArray(actual, sampleIndex, sourceSr, outputSr);

            if (audioBuffer != null)
            {
                int written = audioBuffer.WriteSome(toWrite, 0, toWrite.Length);
                if (written < toWrite.Length && verboseLogging)
                    Debug.LogWarning($"[StreamingElevenLabsTTSClient] Buffer full: wrote {written}/{toWrite.Length} samples.");
            }

            float now = Time.realtimeSinceStartup;
            if (lastChunkReceivedAt > 0f)
            {
                float gap = now - lastChunkReceivedAt;
                if (gap > largeChunkGapWarningSeconds && verboseLogging)
                    Debug.LogWarning($"[StreamingElevenLabsTTSClient] LARGE CHUNK GAP: {gap:F3}s, bytes={dataLength}");
            }

            lastChunkReceivedAt = now;
            Interlocked.Add(ref totalBytesReceived, dataLength);
        }

        private void FinalizeDiagnostics()
        {
            if (!captureFullPcmForDebug)
                return;

            int usableBytes = fullPcmCapture.Count - (fullPcmCapture.Count % 2);
            if (usableBytes <= 0)
                return;

            int totalSamples = usableBytes / 2;
            int sampleRate = GetPcmSampleRate(outputFormat);
            float expectedDuration = totalSamples / (float)sampleRate;

            if (logExpectedDuration)
            {
                Debug.Log(
                    $"[StreamingElevenLabsTTSClient] PCM total bytes={fullPcmCapture.Count}, usableBytes={usableBytes}, samples={totalSamples}, expectedDuration={expectedDuration:F2}s");
            }

            if (buildDebugClipOnComplete)
            {
                debugCapturedClip = BuildClipFromCapturedPcm("ElevenLabs_TTS_DebugCaptured");
                if (debugCapturedClip != null)
                    Debug.Log($"[StreamingElevenLabsTTSClient] Debug clip creado. length={debugCapturedClip.length:F2}s");
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

            int frames = totalSamples;
            if (frames <= 0)
                return null;

            int sampleRate = GetPcmSampleRate(outputFormat);
            AudioClip clip = AudioClip.Create(clipName, frames, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

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

        private class PcmStreamingDownloadHandler : DownloadHandlerScript
        {
            private readonly StreamingElevenLabsTTSClient owner;

            public PcmStreamingDownloadHandler(StreamingElevenLabsTTSClient owner) : base()
            {
                this.owner = owner;
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (data == null || dataLength <= 0)
                    return true;

                try
                {
                    owner.AppendPcmChunk(data, dataLength);
                    return true;
                }
                catch (Exception e)
                {
                    owner.streamFailed = true;
                    owner.streamErrorMessage = "Error procesando chunk PCM: " + e.Message;
                    return false;
                }
            }

            protected override void CompleteContent()
            {
                owner.streamCompleted = true;
            }
        }

        private string GetApiKey()
        {
            return ApiKeyProvider.Resolve(apiKey, ApiKeyEnvironmentVariable, nameof(StreamingElevenLabsTTSClient));
        }

    }
}
