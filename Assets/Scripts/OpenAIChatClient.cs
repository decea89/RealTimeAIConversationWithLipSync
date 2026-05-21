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
        private const string ApiKeyEnvironmentVariable = "OPENAI_API_KEY";
        private string apiKey = string.Empty;

        private static ConversationSettings Settings => ConversationSettings.Instance;

        private string endpoint
        {
            get => Settings.Chat.endpoint;
            set => Settings.Chat.endpoint = value;
        }

        private string model
        {
            get => Settings.Chat.model;
            set => Settings.Chat.model = value;
        }

        private int requestTimeoutSeconds
        {
            get => Settings.Chat.requestTimeoutSeconds;
            set => Settings.Chat.requestTimeoutSeconds = value;
        }

        private string systemPrompt
        {
            get => Settings.Chat.systemPrompt;
            set => Settings.Chat.systemPrompt = value;
        }

        private float temperature
        {
            get => Settings.Chat.temperature;
            set => Settings.Chat.temperature = value;
        }

        private int maxCompletionTokens
        {
            get => Settings.Chat.maxCompletionTokens;
            set => Settings.Chat.maxCompletionTokens = value;
        }

        public IEnumerator RequestChat(string userMessage, Action<string, string> onComplete)
        {
            string resolvedApiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                onComplete?.Invoke(null, $"OpenAIChatClient: API key no configurada. Define {ApiKeyEnvironmentVariable}.");
                yield break;
            }

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
            request.SetRequestHeader("Authorization", $"Bearer {resolvedApiKey}");

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
                onComplete?.Invoke(null, "Error parsing OpenAI Chat JSON: " + e.Message + "\nRAW:\n" + raw);
                yield break;
            }

            string content = parsed?.choices?[0]?.message?.content;
            if (string.IsNullOrWhiteSpace(content))
            {
                onComplete?.Invoke(null, "Empty or invalid OpenAI Chat response.\nRAW:\n" + raw);
                yield break;
            }

            onComplete?.Invoke(content.Trim(), null);
        }

        public IEnumerator RequestChatRich(string userMessage, Action<ChatServiceResult, string> onComplete)
        {
            string resolvedApiKey = GetApiKey();
            if (string.IsNullOrWhiteSpace(resolvedApiKey))
            {
                onComplete?.Invoke(null, $"OpenAIChatClient: API key not configured. Set {ApiKeyEnvironmentVariable}.");
                yield break;
            }

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
            request.SetRequestHeader("Authorization", $"Bearer {resolvedApiKey}");

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
                onComplete?.Invoke(null, "Error parsing OpenAI Chat JSON: " + e.Message + "\nRAW:\n" + rawJson);
                yield break;
            }

            responseText = parsed?.choices?[0]?.message?.content;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                onComplete?.Invoke(null, "Empty or invalid OpenAI Chat response.\nRAW:\n" + rawJson);
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

        private string GetApiKey()
        {
            return ApiKeyProvider.Resolve(apiKey, ApiKeyEnvironmentVariable, nameof(OpenAIChatClient));
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