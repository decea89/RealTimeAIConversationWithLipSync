using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MVP.Conversation
{
    public class OpenAIConversationController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MonoBehaviour chatServiceBehaviour;   // IChatService
        [SerializeField] private MonoBehaviour sttServiceBehaviour;    // ISTTService
        [SerializeField] private MonoBehaviour ttsServiceBehaviour;    // ITTSService o IStreamingTTSService
        [SerializeField] private MicrophoneRecorder microphoneRecorder;
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private ConversationDebugView debugView;
        [SerializeField] private AvatarEmotionController emotionController;

        [Header("Push To Talk")]
        [SerializeField] private bool useKeyboardDebugShortcut = true;
        [SerializeField] private KeyCode keyboardDebugKey = KeyCode.Space;
        [SerializeField] private float minimumHoldSeconds = 0.12f;

        [Header("Latency Debug")]
        [SerializeField] private bool showTimingInDebugView = true;
        [SerializeField] private bool includeAssistantTextInTimingView = true;

        [Header("Optional UI")]
        [SerializeField] private TMP_InputField debugInputField;
        [SerializeField] private Button debugSendButton;

        [SerializeField] private KeyCode playDebugCapturedClipKey = KeyCode.P;
        [SerializeField] private StreamingOpenAITTSClient streamingDebugClient;

        private IChatService chatService;
        private ISTTService sttService;
        private ITTSService ttsService;
        private IStreamingTTSService streamingTtsService;

        private bool isRunningConversation;
        private bool isHoldingToTalk;
        private float holdStartTime;
        private Coroutine currentConversationRoutine;

        private void Awake()
        {
            if (chatServiceBehaviour != null)
                chatService = chatServiceBehaviour as IChatService;

            if (sttServiceBehaviour != null)
                sttService = sttServiceBehaviour as ISTTService;

            if (ttsServiceBehaviour != null)
            {
                ttsService = ttsServiceBehaviour as ITTSService;
                streamingTtsService = ttsServiceBehaviour as IStreamingTTSService;
            }

            if (chatService == null)
                Debug.LogError("[OpenAIConversationController] Chat service behaviour is not set or does not implement IChatService.");

            if (sttServiceBehaviour != null && sttService == null)
                Debug.LogError("[OpenAIConversationController] STT service behaviour is set but does not implement ISTTService.");

            if (ttsService == null && streamingTtsService == null)
                Debug.LogError("[OpenAIConversationController] TTS service behaviour is not set or does not implement ITTSService or IStreamingTTSService.");

            if (avatarAudioSource == null)
                Debug.LogError("[OpenAIConversationController] Avatar AudioSource is not assigned.");

            if (microphoneRecorder == null)
                Debug.LogError("[OpenAIConversationController] MicrophoneRecorder is not assigned.");

            if (debugSendButton != null && debugInputField != null)
            {
                debugSendButton.onClick.AddListener(() =>
                {
                    if (!string.IsNullOrWhiteSpace(debugInputField.text))
                        StartTextConversation(debugInputField.text);
                });
            }
        }

        private void Update()
        {
            if (!useKeyboardDebugShortcut || microphoneRecorder == null)
                return;

            if (Input.GetKeyDown(keyboardDebugKey))
                BeginPushToTalk();

            if (Input.GetKeyUp(keyboardDebugKey))
                EndPushToTalkAndSend();

            if (Input.GetKeyDown(playDebugCapturedClipKey) && streamingDebugClient != null)
            {
                streamingDebugClient.PlayDebugCapturedClip(avatarAudioSource);
            }
        }

        public void StartTextConversation(string userText)
        {
            if (isRunningConversation || string.IsNullOrWhiteSpace(userText))
                return;

            if (currentConversationRoutine != null)
                StopCoroutine(currentConversationRoutine);

            currentConversationRoutine = StartCoroutine(RunTextConversation(userText));
        }

        public void BeginPushToTalk()
        {
            if (isRunningConversation || isHoldingToTalk || microphoneRecorder == null)
                return;

            microphoneRecorder.StartRecording();

            if (!microphoneRecorder.IsRecording)
            {
                UpdateDebugState("Error", "No se pudo iniciar la grabación del micrófono.");
                return;
            }

            isHoldingToTalk = true;
            holdStartTime = Time.realtimeSinceStartup;
            UpdateDebugState("Recording", "Grabando... suelta para enviar.");
        }

        public void EndPushToTalkAndSend()
        {
            if (!isHoldingToTalk || isRunningConversation || microphoneRecorder == null)
                return;

            isHoldingToTalk = false;
            float heldSeconds = Time.realtimeSinceStartup - holdStartTime;

            if (heldSeconds < minimumHoldSeconds)
            {
                if (microphoneRecorder.IsRecording)
                    microphoneRecorder.StopRecording();

                UpdateDebugState("Idle", "Pulsa y mantén un poco más para grabar.");
                return;
            }

            AudioClip clip = microphoneRecorder.StopRecording();
            if (clip == null)
            {
                UpdateDebugState("Error", "No se pudo obtener un AudioClip válido del micrófono.");
                return;
            }

            if (currentConversationRoutine != null)
                StopCoroutine(currentConversationRoutine);

            currentConversationRoutine = StartCoroutine(RunVoiceConversationFromClip(clip));
        }

        private IEnumerator RunTextConversation(string userText)
        {
            isRunningConversation = true;

            var result = new ConversationResult
            {
                userText = userText
            };
            result.timing.StartTotal();

            UpdateDebugState("Chat", "Procesando entrada de texto...");
            result.timing.StartChat();

            ChatServiceResult chatResult = null;
            string chatError = null;

            if (chatService == null)
            {
                chatError = "Chat service not configured.";
            }
            else
            {
                yield return chatService.RequestChatRich(userText, (r, err) =>
                {
                    chatResult = r;
                    chatError = err;
                });
            }

            result.timing.StopChat();

            if (!string.IsNullOrEmpty(chatError) || chatResult == null || string.IsNullOrWhiteSpace(chatResult.responseText))
            {
                result.error = string.IsNullOrEmpty(chatError) ? "Respuesta de chat vacía." : chatError;
                UpdateDebugState("Error", result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            result.assistantText = chatResult.responseText;
            result.emotion = chatResult.emotion;
            result.intentTags = chatResult.intentTags ?? new List<IntentTag>();

            emotionController?.ApplyEmotion(result.emotion, result.intentTags);

            yield return PlayAssistantReplyWithTts(result);

            result.timing.StopTotal();
            isRunningConversation = false;
            LogTiming(result);
        }

        private IEnumerator RunVoiceConversationFromClip(AudioClip clip)
        {
            isRunningConversation = true;

            var result = new ConversationResult();
            result.timing.StartTotal();

            if (clip == null)
            {
                result.error = "No se grabó audio desde el micrófono.";
                UpdateDebugState("Error", result.error);
                isRunningConversation = false;
                yield break;
            }

            UpdateDebugState("STT", "Transcribiendo audio del usuario...");
            result.timing.StartStt();

            AudioClip trimmedClip = AudioTrimmingUtility.TrimSilence(
                clip,
                levelThreshold: 0.01f,
                minSegmentSeconds: 0.08f,
                extraPaddingSeconds: 0.12f
            );

            AudioClip clipToSend = trimmedClip != null ? trimmedClip : clip;
            byte[] wavBytes = WavUtility.FromAudioClip(clipToSend);

            string userText = null;
            string sttError = null;

            if (sttService != null)
            {
                yield return sttService.Transcribe(wavBytes, (text, err) =>
                {
                    userText = text;
                    sttError = err;
                });
            }
            else
            {
                sttError = "No STT service configured.";
            }

            result.timing.StopStt();

            if (!string.IsNullOrEmpty(sttError) || string.IsNullOrWhiteSpace(userText))
            {
                result.error = string.IsNullOrEmpty(sttError) ? "La transcripción de STT está vacía." : sttError;
                UpdateDebugState("Error", result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            result.userText = userText;

            UpdateDebugState("Chat", "Consultando servicio de chat...");
            result.timing.StartChat();

            ChatServiceResult chatResult = null;
            string chatError = null;

            if (chatService == null)
            {
                chatError = "Chat service not configured.";
            }
            else
            {
                yield return chatService.RequestChatRich(userText, (r, err) =>
                {
                    chatResult = r;
                    chatError = err;
                });
            }

            result.timing.StopChat();

            if (!string.IsNullOrEmpty(chatError) || chatResult == null || string.IsNullOrWhiteSpace(chatResult.responseText))
            {
                result.error = string.IsNullOrEmpty(chatError) ? "Respuesta de chat vacía." : chatError;
                UpdateDebugState("Error", result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            result.assistantText = chatResult.responseText;
            result.emotion = chatResult.emotion;
            result.intentTags = chatResult.intentTags ?? new List<IntentTag>();

            emotionController?.ApplyEmotion(result.emotion, result.intentTags);

            yield return PlayAssistantReplyWithTts(result);

            result.timing.StopTotal();
            isRunningConversation = false;
            LogTiming(result);
        }

        private IEnumerator PlayAssistantReplyWithTts(ConversationResult result)
        {
            if (string.IsNullOrWhiteSpace(result.assistantText))
            {
                result.error = "assistantText está vacío, no hay nada que sintetizar.";
                UpdateDebugState("Error", result.error);
                yield break;
            }

            if (streamingTtsService != null)
            {
                UpdateDebugState("TTS", "Generando audio streaming...");
                result.timing.StartTts();

                string streamingError = null;
                bool playbackMarked = false;

                yield return streamingTtsService.RequestSpeechStreamed(
                    result.assistantText,
                    avatarAudioSource,
                    onPlaybackStarted: () =>
                    {
                        if (playbackMarked)
                            return;

                        playbackMarked = true;
                        result.timing.StopTts();
                        result.timing.MarkPlaybackStart();
                        UpdateDebugState("Speaking", "Reproduciendo audio streaming...");
                    },
                    onError: err =>
                    {
                        streamingError = err;
                    },
                    onCompleted: () =>
                    {
                        result.timing.MarkPlaybackEnd();
                    });

                if (!string.IsNullOrEmpty(streamingError))
                {
                    result.error = streamingError;
                    UpdateDebugState("Error", streamingError);
                    yield break;
                }

                if (!playbackMarked)
                {
                    result.timing.StopTts();
                    result.timing.MarkPlaybackStart();
                }

                if (result.timing.playbackEndTime <= 0.0)
                    result.timing.MarkPlaybackEnd();

                yield break;
            }

            if (ttsService == null)
            {
                result.error = "TTS service not configured.";
                UpdateDebugState("Error", result.error);
                yield break;
            }

            UpdateDebugState("TTS", "Generando audio de la respuesta...");
            result.timing.StartTts();

            AudioClip ttsClip = null;
            string ttsError = null;

            yield return ttsService.RequestSpeech(result.assistantText, (clip, err) =>
            {
                ttsClip = clip;
                ttsError = err;
            });

            result.timing.StopTts();

            if (!string.IsNullOrEmpty(ttsError))
            {
                result.error = ttsError;
                UpdateDebugState("Error", ttsError);
                yield break;
            }

            if (ttsClip == null)
            {
                result.error = "TTS devolvió un clip nulo.";
                UpdateDebugState("Error", result.error);
                yield break;
            }

            UpdateDebugState("Speaking", "Reproduciendo audio en el avatar...");
            avatarAudioSource.Stop();
            avatarAudioSource.clip = ttsClip;
            avatarAudioSource.Play();

            result.timing.MarkPlaybackStart();

            yield return new WaitForSeconds(ttsClip.length);

            result.timing.MarkPlaybackEnd();
        }

        private void LogTiming(ConversationResult result)
        {
            if (result == null)
                return;

            string timingText =
                $"STT: {result.timing.SttSeconds:F2}s | Chat: {result.timing.ChatSeconds:F2}s | TTS: {result.timing.TtsSeconds:F2}s\n" +
                $"Time to first audio: {result.timing.TimeToFirstAudioSeconds:F2}s | " +
                $"Playback duration: {result.timing.PlaybackDurationSeconds:F2}s | " +
                $"Time to playback end: {result.timing.TimeToPlaybackEndSeconds:F2}s | " +
                $"Turn coroutine complete: {result.timing.TurnCompleteSeconds:F2}s";

            if (includeAssistantTextInTimingView && !string.IsNullOrWhiteSpace(result.assistantText))
                timingText += $"\n\nAI: {result.assistantText}";

            Debug.Log("[OpenAIConversationController] " + timingText.Replace("\n", " | "));

            if (showTimingInDebugView && debugView != null)
            {
                debugView.SetState("Idle");
                debugView.SetMessage(timingText);
            }
        }

        private void UpdateDebugState(string state, string message)
        {
            if (debugView != null)
            {
                debugView.SetState(state);
                debugView.SetMessage(message);
            }
        }
    }
}