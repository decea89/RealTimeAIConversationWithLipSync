using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class OpenAITTSClient : MonoBehaviour, ITTSService
    {
        [Header("OpenAI TTS")]
        [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
        [SerializeField] private string endpoint = "https://api.openai.com/v1/audio/speech";
        [SerializeField] private string model = "gpt-4o-mini-tts";
        [SerializeField] private string voice = "coral";
        [SerializeField] [TextArea(2, 5)]
        private string instructions = "Speak in warm, natural Spanish, with clear diction and a conversational tone suitable for a historical character in VR.";
        [SerializeField] private string responseFormat = "wav";

        [Header("Performance")]
        [SerializeField] [Range(0.5f, 2.0f)]
        private float speed = 1.10f; // 1.0 = normal, 1.1–1.2 = un poco más rápido

        public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
        {
            var body = new OpenAITtsRequest
            {
                model = model,
                input = text,
                voice = voice,
                instructions = instructions,
                response_format = responseFormat,
                speed = speed
            };

            string json = JsonUtility.ToJson(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, request.error + "\n" + request.downloadHandler.text);
                yield break;
            }

            byte[] audioBytes = request.downloadHandler.data;
            if (audioBytes == null || audioBytes.Length == 0)
            {
                onComplete?.Invoke(null, "OpenAI TTS devolvió audio vacío");
                yield break;
            }

            string extension = responseFormat.ToLowerInvariant() == "mp3" ? ".mp3" : ".wav";
            AudioType audioType = responseFormat.ToLowerInvariant() == "mp3" ? AudioType.MPEG : AudioType.WAV;
            string filePath = Path.Combine(Application.temporaryCachePath, $"openai_tts_{Guid.NewGuid():N}{extension}");

            try
            {
                File.WriteAllBytes(filePath, audioBytes);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, "No se pudo escribir el archivo TTS temporal: " + e.Message);
                yield break;
            }

            using var fileRequest = UnityWebRequestMultimedia.GetAudioClip("file://" + filePath, audioType);
            yield return fileRequest.SendWebRequest();

            if (fileRequest.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, "Error cargando AudioClip temporal: " + fileRequest.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(fileRequest);
            if (clip == null)
            {
                onComplete?.Invoke(null, "No se pudo crear AudioClip desde la respuesta de OpenAI TTS");
                yield break;
            }

            onComplete?.Invoke(clip, null);
        }

        [Serializable]
        private class OpenAITtsRequest
        {
            public string model;
            public string input;
            public string voice;
            public string instructions;
            public string response_format;
            public float speed;
        }
    }
}