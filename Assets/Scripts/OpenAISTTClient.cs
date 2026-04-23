using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class OpenAISTTClient : MonoBehaviour, ISTTService
    {
        [Header("OpenAI STT")]
        [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
        [SerializeField] private string endpoint = "https://api.openai.com/v1/audio/transcriptions";
        [SerializeField] private string model = "gpt-4o-mini-transcribe";
        [SerializeField] private string responseFormat = "text";
        [SerializeField] private string language = "es";

        public IEnumerator Transcribe(byte[] audioBytes, Action<string, string> onComplete)
        {
            if (audioBytes == null || audioBytes.Length == 0)
            {
                onComplete?.Invoke(null, "No hay audio para transcribir.");
                yield break;
            }

            byte[] boundary = Encoding.UTF8.GetBytes("------------------------" + DateTime.Now.Ticks.ToString("x"));
            WWWForm form = new WWWForm();
            form.AddBinaryData("file", audioBytes, "microphone.wav", "audio/wav");
            form.AddField("model", model);
            form.AddField("response_format", responseFormat);
            form.AddField("language", language);

            using var request = UnityWebRequest.Post(endpoint, form);
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            string raw = request.downloadHandler.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, request.error + "\n" + raw);
                yield break;
            }

            if (responseFormat == "text")
            {
                onComplete?.Invoke(raw?.Trim(), null);
                yield break;
            }

            try
            {
                var parsed = JsonConvert.DeserializeObject<TranscriptionResponse>(raw);
                onComplete?.Invoke(parsed?.text?.Trim(), null);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, "Error parseando respuesta STT: " + e.Message + "\nRAW:\n" + raw);
            }
        }

        [Serializable]
        private class TranscriptionResponse
        {
            public string text;
        }
    }
}