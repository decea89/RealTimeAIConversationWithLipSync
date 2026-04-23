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
        [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
        [SerializeField] private string endpoint = "https://api.openai.com/v1/chat/completions";
        [SerializeField] private string model = "gpt-4o-mini";
        [SerializeField] [TextArea(3, 8)]
        private string systemPrompt =
            "You are a historical character speaking inside a VR experience. " +
            "Reply briefly, clearly, and with strong personality. " +
            "Default to one short sentence under 25 words unless the user explicitly asks for more detail.";
        [SerializeField] [Range(0f, 2f)] private float temperature = 0.5f;
        [SerializeField] private int maxCompletionTokens = 60;

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

            yield return RequestChat(userMessage, (text, err) =>
            {
                responseText = text;
                error = err;
            });

            if (!string.IsNullOrEmpty(error))
            {
                onComplete?.Invoke(null, error);
                yield break;
            }

            onComplete?.Invoke(new ChatServiceResult
            {
                responseText = responseText,
                emotion = CharacterEmotion.Neutral,
                intentTags = new List<IntentTag>(),
                rawJson = responseText
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