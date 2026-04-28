using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{
    public class CharacterBackendChatClient : MonoBehaviour, IChatService, IConversationResettable
    {
        [Header("Backend Chat API")]
        [SerializeField] private string baseUrl = "https://your-backend.example.com";
        [SerializeField] private string chatPath = "chat";
        [SerializeField] private string sessionResetPath = "session/reset";
        [SerializeField] private string bearerToken = "YOUR_BACKEND_TOKEN";
        [SerializeField] private string characterId = "default-character";
        [SerializeField] private string locale = "es-ES";
        [SerializeField] private string optionalUserId = null;

        [Header("Debug")]
        [SerializeField] private bool logRequests = false;
        [SerializeField] private bool logResponses = false;
        [SerializeField] private bool callResetEndpointOnNewUser = false;

        private string ChatUrl => $"{baseUrl.TrimEnd('/')}/{chatPath.TrimStart('/')}";
        private string SessionResetUrl => $"{baseUrl.TrimEnd('/')}/{sessionResetPath.TrimStart('/')}";

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
            if (!SessionManager.HasActiveSession)
            {
                SessionManager.StartNewSession();
            }

            var requestBody = new ClientChatRequestDto
            {
                session_id = SessionManager.CurrentSessionId,
                user_text = userMessage,
                character_id = characterId,
                metadata = new ClientChatMetadataRequestDto
                {
                    locale = locale,
                    user_id = string.IsNullOrWhiteSpace(optionalUserId) ? null : optionalUserId
                }
            };

            string json = JsonConvert.SerializeObject(requestBody, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(ChatUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

            if (logRequests)
            {
                Debug.Log($"CharacterBackendChatClient POST {ChatUrl}\n{json}");
            }

            yield return request.SendWebRequest();

            string raw = request.downloadHandler.text;

            if (request.result != UnityWebRequest.Result.Success)
            {
                onComplete?.Invoke(null, request.error ?? raw);
                yield break;
            }

            if (logResponses)
            {
                Debug.Log($"CharacterBackendChatClient RESPONSE\n{raw}");
            }

            ClientChatResponseDto dto;
            try
            {
                dto = JsonConvert.DeserializeObject<ClientChatResponseDto>(raw);
            }
            catch (Exception e)
            {
                onComplete?.Invoke(null, $"Error parseando JSON de backend chat: {e.Message}\n{raw}");
                yield break;
            }

            if (dto == null || string.IsNullOrWhiteSpace(dto.response_text))
            {
                onComplete?.Invoke(null, $"Respuesta vacía o inválida del backend chat.\n{raw}");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(dto.session_id))
            {
                // mantenemos sincronizado el session_id por si el backend lo devuelve normalizado
                if (SessionManager.CurrentSessionId != dto.session_id)
                {
                    Debug.Log($"CharacterBackendChatClient: session_id actualizado por backend: {dto.session_id}");
                }
            }

            var result = new ChatServiceResult
            {
                responseText = dto.response_text.Trim(),
                emotion = MapEmotion(dto.emotion),
                intentTags = MapIntentTags(dto.intent_tags),
                rawJson = raw,
                latencyMs = dto.metadata != null ? dto.metadata.latency_ms : 0,
                model = dto.metadata != null ? dto.metadata.model : null,
                ragHits = dto.metadata != null ? dto.metadata.rag_hits : 0,
                sourceTitles = dto.sources != null
                    ? dto.sources.ConvertAll(s => s.title)
                    : new List<string>()
            };

            SessionManager.RegisterTurn();
            onComplete?.Invoke(result, null);
        }

        public void ResetConversationContext()
        {
            StartCoroutine(ResetConversationContextRoutine());
        }

        private IEnumerator ResetConversationContextRoutine()
        {
            if (!callResetEndpointOnNewUser)
            {
                yield break;
            }

            if (!SessionManager.HasActiveSession)
            {
                yield break;
            }

            var body = new
            {
                session_id = SessionManager.CurrentSessionId,
                character_id = characterId
            };

            string json = JsonConvert.SerializeObject(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(SessionResetUrl, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");

            if (logRequests)
            {
                Debug.Log($"CharacterBackendChatClient POST {SessionResetUrl}\n{json}");
            }

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"CharacterBackendChatClient reset endpoint error: {request.error}\n{request.downloadHandler.text}");
            }
            else if (logResponses)
            {
                Debug.Log($"CharacterBackendChatClient RESET RESPONSE\n{request.downloadHandler.text}");
            }
        }

        private CharacterEmotion MapEmotion(string emotion)
        {
            if (string.IsNullOrEmpty(emotion)) return CharacterEmotion.Neutral;

            switch (emotion.ToLowerInvariant())
            {
                case "happy": return CharacterEmotion.Happy;
                case "thinking": return CharacterEmotion.Thinking;
                case "concerned": return CharacterEmotion.Concerned;
                case "angry": return CharacterEmotion.Angry;
                case "sad": return CharacterEmotion.Sad;
                case "neutral":
                default: return CharacterEmotion.Neutral;
            }
        }

        private List<IntentTag> MapIntentTags(List<string> tags)
        {
            var list = new List<IntentTag>();
            if (tags == null || tags.Count == 0) return list;

            foreach (var t in tags)
            {
                if (string.IsNullOrEmpty(t)) continue;

                switch (t.ToLowerInvariant())
                {
                    case "greeting": list.Add(IntentTag.Greeting); break;
                    case "knowledge_answer": list.Add(IntentTag.KnowledgeAnswer); break;
                    case "fallback": list.Add(IntentTag.Fallback); break;
                    case "out_of_scope": list.Add(IntentTag.OutOfScope); break;
                    default: list.Add(IntentTag.Unknown); break;
                }
            }

            return list;
        }
    }
}
