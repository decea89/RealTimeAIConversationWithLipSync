using System;
using System.Collections;
using System.Text;
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
        private string instructions = "Speak in warm, natural Spanish, with clear diction and a conversational tone suitable for a historical character in VR.";

        [Header("PCM Stream")]
        [SerializeField] private int sampleRate = 24000;
        [SerializeField] private int channels = 1;
        [SerializeField] private int maxClipSeconds = 45;
        [SerializeField] private float prebufferSeconds = 0.40f;

        [Header("Debug")]
        [SerializeField] private bool logChunkProgress = false;

        private StreamingAudioBuffer audioBuffer;
        private bool isStreamingFinished;
        private bool hasPlaybackStarted;
        private string streamError;

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

            isStreamingFinished = false;
            hasPlaybackStarted = false;
            streamError = null;

            int capacitySamples = sampleRate * channels * maxClipSeconds;
            audioBuffer = new StreamingAudioBuffer(capacitySamples);

            AudioClip streamClip = AudioClip.Create(
                "OpenAI_TTS_Stream_PCM",
                capacitySamples,
                channels,
                sampleRate,
                true,
                OnAudioRead,
                OnAudioSetPosition);

            targetAudioSource.Stop();
            targetAudioSource.clip = streamClip;

            StartCoroutine(StreamPcmFromOpenAI(text));

            int prebufferSamples = Mathf.CeilToInt(sampleRate * channels * prebufferSeconds);

            while (!hasPlaybackStarted && string.IsNullOrEmpty(streamError))
            {
                if (audioBuffer.AvailableSamples >= prebufferSamples)
                {
                    targetAudioSource.Play();
                    hasPlaybackStarted = true;
                    onPlaybackStarted?.Invoke();
                    break;
                }

                if (isStreamingFinished && audioBuffer.AvailableSamples == 0)
                    break;

                yield return null;
            }

            if (!string.IsNullOrEmpty(streamError))
            {
                onError?.Invoke(streamError);
                yield break;
            }

            while (true)
            {
                bool sourcePlaying = targetAudioSource.isPlaying;
                bool stillBuffered = audioBuffer.AvailableSamples > 0;

                if (isStreamingFinished && !stillBuffered)
                {
                    if (sourcePlaying)
                        targetAudioSource.Stop();
                    break;
                }

                yield return null;
            }

            onCompleted?.Invoke();
        }

        private IEnumerator StreamPcmFromOpenAI(string text)
        {
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
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            var op = request.SendWebRequest();
            int processedBytes = 0;

            while (!op.isDone)
            {
                byte[] allData = request.downloadHandler?.data;
                if (allData != null && allData.Length > processedBytes)
                {
                    int newBytes = allData.Length - processedBytes;
                    ProcessPcm16Chunk(allData, processedBytes, newBytes);
                    processedBytes = allData.Length;

                    if (logChunkProgress)
                        Debug.Log($"[StreamingOpenAITTSClient] bytes recibidos: {processedBytes}");
                }

                yield return null;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                streamError = request.error + "\n" + request.downloadHandler.text;
                yield break;
            }

            byte[] finalData = request.downloadHandler?.data;
            if (finalData != null && finalData.Length > processedBytes)
            {
                int newBytes = finalData.Length - processedBytes;
                ProcessPcm16Chunk(finalData, processedBytes, newBytes);
            }

            isStreamingFinished = true;
        }

        private void ProcessPcm16Chunk(byte[] data, int startByte, int byteCount)
        {
            int sampleCount = byteCount / 2;
            if (sampleCount <= 0)
                return;

            float[] samples = new float[sampleCount];
            int s = 0;

            int end = startByte + byteCount;
            for (int i = startByte; i + 1 < end; i += 2)
            {
                short pcm = (short)(data[i] | (data[i + 1] << 8));
                samples[s++] = pcm / 32768f;
            }

            audioBuffer.Write(samples, s);
        }

        private void OnAudioRead(float[] data)
        {
            audioBuffer?.Read(data);
        }

        private void OnAudioSetPosition(int newPosition)
        {
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
    }
}