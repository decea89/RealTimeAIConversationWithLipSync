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
        [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
        [SerializeField] private string endpoint = "https://api.openai.com/v1/audio/speech";
        [SerializeField] private string model = "gpt-4o-mini-tts";
        [SerializeField] private string voice = "coral";

        [SerializeField] [TextArea(2, 5)]
        private string instructions =
            "Speak in warm, natural Spanish, with clear diction and a conversational tone suitable for a historical character in VR.";

        [Header("PCM Stream Config")]
        [SerializeField] private int sampleRate = 24000;
        [SerializeField] private int channels = 1;
        [SerializeField] private int maxClipSeconds = 60;
        [SerializeField] private float prebufferSeconds = 0.35f;
        [SerializeField] private float drainGraceSeconds = 0.40f;

        [Header("Debug")]
        [SerializeField] private bool logChunks = false;

        [Header("Diagnostics")]
        [SerializeField] private bool captureFullPcmForDebug = true;
        [SerializeField] private bool logExpectedDuration = true;
        [SerializeField] private bool buildDebugClipOnComplete = true;

        private StreamingAudioBuffer audioBuffer;
        private volatile bool streamCompleted;
        private volatile bool streamFailed;
        private volatile string streamErrorMessage;
        private long totalBytesReceived;

        private bool hasPendingOddByte;
        private byte pendingOddByte;

        private readonly List<byte> fullPcmCapture = new List<byte>(65536);
        private AudioClip debugCapturedClip;

        public AudioClip DebugCapturedClip => debugCapturedClip;

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
            hasPendingOddByte = false;
            pendingOddByte = 0;

            fullPcmCapture.Clear();
            debugCapturedClip = null;

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

            var downloadHandler = new PcmStreamingDownloadHandler(this);

            var body = new OpenAITtsRequest
            {
                model = model,
                input = text,
                voice = voice,
                instructions = instructions,
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

            while (true)
            {
                if (streamFailed)
                {
                    targetAudioSource.Stop();
                    onError?.Invoke(streamErrorMessage ?? "StreamingOpenAITTSClient: fallo en streaming.");
                    yield break;
                }

                if (!playbackStarted && audioBuffer.AvailableSamples >= prebufferSamples)
                {
                    targetAudioSource.Play();
                    playbackStarted = true;
                    onPlaybackStarted?.Invoke();
                }

                if (operation.isDone)
                    break;

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

            if (!playbackStarted && audioBuffer.AvailableSamples > 0)
            {
                targetAudioSource.Play();
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

                long written = audioBuffer.TotalSamplesWritten;
                long read = audioBuffer.TotalSamplesRead;

                bool allSamplesConsumed = streamCompleted && read >= written;
                bool nothingBuffered = audioBuffer.AvailableSamples <= 0;

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
                    if (targetAudioSource.isPlaying)
                        targetAudioSource.Stop();

                    break;
                }

                yield return null;
            }

            FinalizeDiagnostics();

            onCompleted?.Invoke();
        }

        private void OnAudioRead(float[] data)
        {
            audioBuffer?.Read(data);
        }

        private void OnAudioSetPosition(int newPosition)
        {
        }

        private void AppendPcmChunk(byte[] data, int dataLength)
        {
            if (data == null || dataLength <= 0)
                return;

            if (captureFullPcmForDebug)
            {
                for (int i = 0; i < dataLength; i++)
                    fullPcmCapture.Add(data[i]);
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
                // audioBuffer.Write(samples, sampleIndex);

            Interlocked.Add(ref totalBytesReceived, dataLength);

            if (logChunks)
            {
                long total = Interlocked.Read(ref totalBytesReceived);
                Debug.Log(
                    $"[StreamingOpenAITTSClient] +{dataLength} bytes PCM, total={total}, " +
                    $"bufferedSamples={audioBuffer.AvailableSamples}, written={audioBuffer.TotalSamplesWritten}, " +
                    $"read={audioBuffer.TotalSamplesRead}, pendingOddByte={hasPendingOddByte}");
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
                Debug.Log(
                    $"[StreamingOpenAITTSClient] PCM total bytes={fullPcmCapture.Count}, usableBytes={usableBytes}, " +
                    $"samples={totalSamples}, expectedDuration={expectedDuration:F2}s, " +
                    $"written={audioBuffer.TotalSamplesWritten}, read={audioBuffer.TotalSamplesRead}");
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