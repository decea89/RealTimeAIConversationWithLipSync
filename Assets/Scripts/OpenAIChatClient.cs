using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class OpenAIChatClient : MonoBehaviour, IChatService
    {
        [Header("OpenAI Chat")]
        [SerializeField]
        [Tooltip("Tu API key de OpenAI. Mantener privado.")]
        private string apiKey = "YOUR_OPENAI_API_KEY";
        
        [SerializeField]
        [Tooltip("Endpoint de OpenAI para chat. No cambiar a menos que uses proxy.")]
        private string endpoint = "https://api.openai.com/v1/chat/completions";
        
        [SerializeField]
        [Tooltip("Modelo de chat a usar. 'gpt-4o-mini' es rápido y barato.")]
        private string model = "gpt-4o-mini";
        
        [Header("Request Guards")]
        [SerializeField]
        [Range(5, 180)]
        [Tooltip("Timeout chat (s). Si respuestas largas se cortan, aumentar a 90-120.")]
        private int requestTimeoutSeconds = 90;

        [SerializeField]
        [TextArea(3, 8)]
        [Tooltip("Instrucciones al modelo. Define personalidad y estilo de respuesta.")]
        private string systemPrompt =
            "You are a historical character speaking inside a VR experience. " +
            "Reply briefly, clearly, and with strong personality. " +
            "Default to one short sentence under 25 words unless the user explicitly asks for more detail.";

        [SerializeField]
        [Range(0f, 2f)]
        [Tooltip("Creatividad (0=determinista, 2=muy creativo). 0.5-0.7=conversación natural.")]
        private float temperature = 0.5f;
        
        [SerializeField]
        [Range(10, 300)]
        [Tooltip("Máximo de tokens en respuesta. Más alto=respuestas más largas pero más costo.")]
        private int maxCompletionTokens = 60;

        public IEnumerator RequestChat(string userMessage, Action<string, string> onComplete)
        {
            var body = new ChatCompletionRequest
            {
                model = model,
                temperature = temperature,
                max_completion_tokens = maxCompletionTokens,
                messages = new List<Message>
                {
                    new Message { role = "system", content = systemPrompt },
                    new Message { role = "user", content = userMessage }
                }
            };

            string json = JsonConvert.SerializeObject(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(5, requestTimeoutSeconds);
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            string raw = request.downloadHandler.text;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, request.error + "\n" + raw);
                yield break;
            }

            ChatCompletionResponse parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<ChatCompletionResponse>(raw);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, "Error parseando JSON de OpenAI Chat: " + e.Message + "\nRAW:\n" + raw);
                yield break;
            }

            string content = parsed?.choices?[0]?.message?.content;
            if (string.IsNullOrWhiteSpace(content))
            {
                onComplete?.Invoke(null, "Respuesta vacía o inválida de OpenAI Chat.\nRAW:\n" + raw);
                yield break;
            }

            onComplete?.Invoke(content.Trim(), null);
        }

        public IEnumerator RequestChatRich(string userMessage, Action<ChatServiceResult, string> onComplete)
        {
            string responseText = null;
            string error = null;
            string rawJson = null;

            var body = new ChatCompletionRequest
            {
                model = model,
                temperature = temperature,
                max_completion_tokens = maxCompletionTokens,
                messages = new List<Message>
                {
                    new Message { role = "system", content = systemPrompt },
                    new Message { role = "user", content = userMessage }
                }
            };

            string json = JsonConvert.SerializeObject(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            rawJson = request.downloadHandler.text;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, request.error + "\n" + rawJson);
                yield break;
            }

            ChatCompletionResponse parsed;
            try
            {
                parsed = JsonConvert.DeserializeObject<ChatCompletionResponse>(rawJson);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, "Error parseando JSON de OpenAI Chat: " + e.Message + "\nRAW:\n" + rawJson);
                yield break;
            }

            responseText = parsed?.choices?[0]?.message?.content;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                onComplete?.Invoke(null, "Respuesta vacía o inválida de OpenAI Chat.\nRAW:\n" + rawJson);
                yield break;
            }

            onComplete?.Invoke(new ChatServiceResult
            {
                responseText = responseText.Trim(),
                emotion = CharacterEmotion.Neutral,
                intentTags = new List<IntentTag>(),
                rawJson = rawJson
            }, null);
        }

        [Serializable]
        private class ChatCompletionRequest
        {
            public string model;
            public float temperature;
            public int max_completion_tokens;
            public List<Message> messages;
        }

        [Serializable]
        private class Message
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ChatCompletionResponse
        {
            public List<Choice> choices;
        }

        [Serializable]
        private class Choice
        {
            public int index;
            public Message message;
            public string finish_reason;
        }
    }
}