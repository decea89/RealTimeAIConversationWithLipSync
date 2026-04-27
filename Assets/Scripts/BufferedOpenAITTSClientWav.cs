using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class BufferedOpenAITTSClientWav : MonoBehaviour, ITTSService
    {
        [Header("OpenAI TTS")]
        [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
        [SerializeField] private string endpoint = "https://api.openai.com/v1/audio/speech";
        [SerializeField] private string model = "gpt-4o-mini-tts";
        [SerializeField] private string voice = "echo";

        [SerializeField] [TextArea(2, 5)]
        private string instructions =
            "Speak in a warm, natural, conversational tone suitable for a VR character. Keep the pacing clear and slightly expressive.";

        public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke(null, "BufferedOpenAITTSClientWav: text vacío.");
                yield break;
            }

            var body = new OpenAITtsRequest
            {
                model = model,
                input = text,
                voice = voice,
                instructions = instructions,
                response_format = "wav"
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
                string err = request.error;
                if (!string.IsNullOrWhiteSpace(request.downloadHandler?.text))
                    err += "\n" + request.downloadHandler.text;

                onComplete?.Invoke(null, err);
                yield break;
            }

            byte[] wavBytes = request.downloadHandler.data;

            if (wavBytes == null || wavBytes.Length == 0)
            {
                onComplete?.Invoke(null, "BufferedOpenAITTSClientWav: respuesta WAV vacía.");
                yield break;
            }

            Debug.Log($"[BufferedOpenAITTSClientWav] WAV raw length={wavBytes.Length} bytes");

            int previewLen = Mathf.Min(32, wavBytes.Length);
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < previewLen; i++)
            {
                sb.Append(wavBytes[i].ToString("X2"));
                if (i < previewLen - 1) sb.Append(" ");
            }
            Debug.Log($"[BufferedOpenAITTSClientWav] WAV header hex preview (32 bytes): {sb}");

            // if (wavBytes == null || wavBytes.Length == 0)
            // {
            //     onComplete?.Invoke(null, "BufferedOpenAITTSClientWav: respuesta WAV vacía.");
            //     yield break;
            // }

            // AudioClip clip = WavUtility.ToAudioClip(wavBytes, "OpenAI_TTS_WAV");
            // if (clip == null)
            // {
            //     onComplete?.Invoke(null, "BufferedOpenAITTSClientWav: no se pudo decodificar el WAV.");
            //     yield break;
            // }

            // Debug.Log($"[BufferedOpenAITTSClientWav] WAV bytes={wavBytes.Length}, clipLength={clip.length:F2}s, frequency={clip.frequency}, channels={clip.channels}");
            // onComplete?.Invoke(clip, null);

            AudioClip clip = WavUtility.ToAudioClip(wavBytes, "OpenAI_TTS_WAV");
            if (clip == null)
            {
                Debug.LogError("[BufferedOpenAITTSClientWav] WavUtility.ToAudioClip devolvió null.");
                onComplete?.Invoke(null, "BufferedOpenAITTSClientWav: no se pudo decodificar el WAV.");
                yield break;
            }

            Debug.Log($"[BufferedOpenAITTSClientWav] WAV bytes={wavBytes.Length}, clipLength={clip.length:F2}s, frequency={clip.frequency}, channels={clip.channels}");
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
        }
    }
}