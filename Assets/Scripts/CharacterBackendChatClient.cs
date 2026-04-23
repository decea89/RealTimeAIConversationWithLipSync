using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class CharacterBackendChatClient : MonoBehaviour, IChatService
    {
        [Header("Backend Chat API")]
        [SerializeField] private string baseUrl = "https://your-backend.example.com";
        [SerializeField] private string chatPath = "/chat";
        [SerializeField] private string bearerToken = "YOUR_BACKEND_TOKEN";
        [SerializeField] private string characterId = "default_character";
        [SerializeField] private string locale = "es-ES";

        [Header("Debug")]
        [SerializeField] private bool logRequests = false;
        [SerializeField] private bool logResponses = false;

        private string ChatUrl => baseUrl.TrimEnd('/') + chatPath;

        // Compat: versión simple que devuelve solo texto
        public IEnumerator RequestChat(string userMessage, Action<string, string> onComplete)
        {
            ChatServiceResult richResult = null;
            string error = null;

            yield return RequestChatRich(userMessage, (result, err) =>
            {
                richResult = result;
                error = err;
            });

            if (!string.IsNullOrEmpty(error))
            {
                onComplete?.Invoke(null, error);
                yield break;
            }

            onComplete?.Invoke(richResult?.responseText, null);
        }

        public IEnumerator RequestChatRich(string userMessage, Action<ChatServiceResult, string> onComplete)
        {
            var requestBody = new ChatRequestDto
            {
                session_id = SessionManager.CurrentSessionId,
                user_text = userMessage,
                character_id = characterId,
                metadata = new ChatRequestMetadata
                {
                    locale = locale,
                    user_id = null // si quieres pasarlo más adelante
                }
            };

            string json = JsonConvert.SerializeObject(requestBody);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(ChatUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

            if (logRequests)
                Debug.Log($"[CharacterBackendChatClient] POST {ChatUrl}\n{json}");

            yield return request.SendWebRequest();

            string raw = request.downloadHandler.text;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, request.error + "\n" + raw);
                yield break;
            }

            if (logResponses)
                Debug.Log($"[CharacterBackendChatClient] RESPONSE {raw}");

            ChatResponseDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<ChatResponseDto>(raw);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, "Error parseando JSON de backend chat: " + e.Message + "\nRAW:\n" + raw);
                yield break;
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.response_text))
            {
                onComplete?.Invoke(null, "Respuesta vacía o inválida del backend chat.\nRAW:\n" + raw);
                yield break;
            }

            var result = new ChatServiceResult
            {
                responseText = dto.response_text.Trim(),
                emotion = MapEmotion(dto.emotion),
                intentTags = MapIntentTags(dto.intent_tags),
                rawJson = raw
            };

            onComplete?.Invoke(result, null);
        }

        private CharacterEmotion MapEmotion(string emotion)
        {
            if (string.IsNullOrEmpty(emotion))
                return CharacterEmotion.Neutral;

            switch (emotion.ToLowerInvariant())
            {
                case "happy": return CharacterEmotion.Happy;
                case "thinking": return CharacterEmotion.Thinking;
                case "concerned": return CharacterEmotion.Concerned;
                case "angry": return CharacterEmotion.Angry;
                case "sad": return CharacterEmotion.Sad;
                default: return CharacterEmotion.Neutral;
            }
        }

        private List<IntentTag> MapIntentTags(List<string> tags)
        {
            var list = new List<IntentTag>();
            if (tags == null || tags.Count == 0)
                return list;

            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t))
                    continue;

                switch (t.ToLowerInvariant())
                {
                    case "greeting":
                        list.Add(IntentTag.Greeting);
                        break;
                    case "knowledge_answer":
                        list.Add(IntentTag.KnowledgeAnswer);
                        break;
                    case "fallback":
                        list.Add(IntentTag.Fallback);
                        break;
                    case "out_of_scope":
                        list.Add(IntentTag.OutOfScope);
                        break;
                    default:
                        list.Add(IntentTag.Unknown);
                        break;
                }
            }

            return list;
        }

        [Serializable]
        private class ChatRequestMetadata
        {
            public string locale;
            public string user_id;
        }

        [Serializable]
        private class ChatRequestDto
        {
            public string session_id;
            public string user_text;
            public string character_id;
            public ChatRequestMetadata metadata;
        }

        [Serializable]
        private class SourceDto
        {
            public string title;
            public float score;
        }

        [Serializable]
        private class ResponseMetadataDto
        {
            public int latency_ms;
            public string model;
            public int rag_hits;
        }

        [Serializable]
        private class ChatResponseDto
        {
            public string session_id;
            public string response_text;
            public string emotion;
            public List<string> intent_tags;
            public List<SourceDto> sources;
            public ResponseMetadataDto metadata;
        }
    }
}