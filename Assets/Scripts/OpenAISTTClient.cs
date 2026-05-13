using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class OpenAISTTClient : MonoBehaviour, ISTTService
    {
        [Header("OpenAI STT")]
        [SerializeField]
        [Tooltip("Tu API key de OpenAI. Mantener privado.")]
        private string apiKey = "YOUR_OPENAI_API_KEY";
        
        [SerializeField]
        [Tooltip("Endpoint de OpenAI para transcripción. No cambiar a menos que uses proxy.")]
        private string endpoint = "https://api.openai.com/v1/audio/transcriptions";
        
        [SerializeField]
        [Tooltip("Modelo STT a usar. 'gpt-4o-mini-transcribe' es rápido y preciso.")]
        private string model = "gpt-4o-mini-transcribe";
        
        [SerializeField]
        [Tooltip("Formato respuesta. 'json' = solo texto. 'verbose_json' = con timestamps.")]
        private string responseFormat = "json";
        
        [SerializeField]
        [Tooltip("Idioma de entrada (es=español, en=inglés). Mejora precisión.")]
        private string language = "es";
        
        [Header("Request Guards")]
        [SerializeField]
        [Range(5, 180)]
        [Tooltip("Timeout STT (s). Audio largo tarda más. Aumentar si hay timeout en grabaciones largas.")]
        private int requestTimeoutSeconds = 90;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Mostrar logs detallados de la petición STT. Dejar apagado para pruebas normales.")]
        private bool logRequestDetails = false;

        public IEnumerator Transcribe(byte[] audioBytes, Action<string, string> onComplete)
        {
            if (audioBytes == null || audioBytes.Length == 0)
            {
                onComplete?.Invoke(null, "No hay audio para transcribir.");
                yield break;
            }


            if (logRequestDetails)
            {
                Debug.Log($"[OpenAISTTClient] Preparing STT request. bytes={audioBytes.Length}, model={model}, responseFormat={responseFormat}, language={language}");
                Debug.Log($"[OpenAISTTClient] WAV header preview: {GetHexPreview(audioBytes, 32)}");
            }

            string boundary = "----UnityOpenAIBoundary" + DateTime.UtcNow.Ticks.ToString("x");
            byte[] body = BuildMultipartBody(boundary, audioBytes, model, responseFormat, language);

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(5, requestTimeoutSeconds);
            request.disposeUploadHandlerOnDispose = true;
            request.disposeDownloadHandlerOnDispose = true;

            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.SetRequestHeader("Content-Type", $"multipart/form-data; boundary={boundary}");
            request.SetRequestHeader("Accept", "application/json");

            if (logRequestDetails)
            {
                Debug.Log($"[OpenAISTTClient] Multipart body bytes={body.Length}");
                Debug.Log($"[OpenAISTTClient] Content-Type=multipart/form-data; boundary={boundary}");
            }

            yield return request.SendWebRequest();

            string raw = request.downloadHandler?.text;
            byte[] rawBytes = request.downloadHandler?.data;

            if (logRequestDetails)
            {
                Debug.Log("[OpenAISTTClient] VERSION_MARKER_MANUAL_MULTIPART_V2");
                Debug.Log($"[OpenAISTTClient] STT START bytes={audioBytes.Length} wav={GetHexPreview(audioBytes, 16)}");
                Debug.Log($"[OpenAISTTClient] STT END code={request.responseCode} result={request.result} rawBytes={(rawBytes == null ? -1 : rawBytes.Length)} raw='{raw}'");

                Debug.Log($"[OpenAISTTClient] result={request.result}, code={request.responseCode}, error={request.error ?? "null"}");
                Debug.Log($"[OpenAISTTClient] RAW bytes len={(rawBytes == null ? -1 : rawBytes.Length)}");
                Debug.Log($"[OpenAISTTClient] RAW STT: '{raw}'");
            }

            var headers = request.GetResponseHeaders();
            if (logRequestDetails && headers != null)
            {
                foreach (var kv in headers)
                    Debug.Log($"[OpenAISTTClient] Header {kv.Key}: {kv.Value}");
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, $"STT request failed. code={request.responseCode}, error={request.error}\nRAW:\n{raw}");
                yield break;
            }

            if (rawBytes == null || rawBytes.Length == 0 || string.IsNullOrWhiteSpace(raw))
            {
                onComplete?.Invoke(null, $"STT response body vacío. code={request.responseCode}, rawBytes={(rawBytes == null ? -1 : rawBytes.Length)}");
                yield break;
            }

            try
            {
                if (responseFormat == "text")
                {
                    string plainText = raw.Trim();
                    if (logRequestDetails)
                        Debug.Log($"[OpenAISTTClient] Parsed text length={plainText.Length}");
                    onComplete?.Invoke(plainText, null);
                    yield break;
                }

                var parsed = JsonConvert.DeserializeObject<TranscriptionResponse>(raw);
                string parsedText = parsed?.text?.Trim();

                if (logRequestDetails)
                    Debug.Log($"[OpenAISTTClient] Parsed json text length={(parsedText == null ? -1 : parsedText.Length)}");

                if (string.IsNullOrWhiteSpace(parsedText))
                {
                    onComplete?.Invoke(null, $"JSON recibido pero sin campo text útil. RAW:\n{raw}");
                    yield break;
                }

                onComplete?.Invoke(parsedText, null);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, "Error parseando respuesta STT: " + e.Message + "\nRAW:\n" + raw);
            }
        }

        private static byte[] BuildMultipartBody(string boundary, byte[] audioBytes, string model, string responseFormat, string language)
        {
            string nl = "\r\n";
            var bytes = new List<byte>();

            void AddString(string s) => bytes.AddRange(Encoding.UTF8.GetBytes(s));

            AddFormField(bytes, boundary, "model", model, nl);
            AddFormField(bytes, boundary, "response_format", responseFormat, nl);
            AddFormField(bytes, boundary, "language", language, nl);

            AddString($"--{boundary}{nl}");
            AddString($"Content-Disposition: form-data; name=\"file\"; filename=\"microphone.wav\"{nl}");
            AddString($"Content-Type: audio/wav{nl}{nl}");
            bytes.AddRange(audioBytes);
            AddString(nl);

            AddString($"--{boundary}--{nl}");
            return bytes.ToArray();
        }

        private static void AddFormField(List<byte> bytes, string boundary, string fieldName, string value, string nl)
        {
            bytes.AddRange(Encoding.UTF8.GetBytes($"--{boundary}{nl}"));
            bytes.AddRange(Encoding.UTF8.GetBytes($"Content-Disposition: form-data; name=\"{fieldName}\"{nl}{nl}"));
            bytes.AddRange(Encoding.UTF8.GetBytes(value ?? string.Empty));
            bytes.AddRange(Encoding.UTF8.GetBytes(nl));
        }

        private static string GetHexPreview(byte[] data, int count)
        {
            if (data == null || data.Length == 0)
                return "(empty)";

            int len = Mathf.Min(count, data.Length);
            var sb = new StringBuilder(len * 3);
            for (int i = 0; i < len; i++)
            {
                sb.Append(data[i].ToString("X2"));
                if (i < len - 1) sb.Append(' ');
            }
            return sb.ToString();
        }

        [Serializable]
        private class TranscriptionResponse
        {
            public string text;
        }
    }
}