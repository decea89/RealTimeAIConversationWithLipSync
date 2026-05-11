// OpenAIConversationController.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
#if PLATFORM_ANDROID
using UnityEngine.Android;
#endif

namespace MVP.Conversation
{
    public class OpenAIConversationController : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private MonoBehaviour chatServiceBehaviour;
        [SerializeField] private MonoBehaviour sttServiceBehaviour;
        [SerializeField] private MonoBehaviour ttsServiceBehaviour;
        [SerializeField] private MicrophoneRecorder microphoneRecorder;
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private AvatarEmotionController emotionController;
        [SerializeField] private WorldSpaceDebugPanelController worldSpaceDebugPanel;

        [Header("Push To Talk")]
        [SerializeField] private bool useKeyboardDebugShortcut = true;
        [SerializeField] private KeyCode keyboardDebugKey = KeyCode.Space;
        [SerializeField] private bool useXriPushToTalk = true;
        [SerializeField] private InputActionReference pushToTalkAction;
        [SerializeField] private float minimumHoldSeconds = 0.12f;
        [SerializeField] private bool allowBargeInWhileSpeaking = true;

        [Header("Debug Output")]
        [SerializeField] private bool includeAssistantTextInMetrics = true;
        [SerializeField] private bool includeSourcesInBackendInfo = true;

        private IChatService chatService;
        private ISTTService sttService;
        private ITTSService ttsService;
        private IStreamingTTSService streamingTtsService;
        private IInterruptibleTTSService interruptibleTtsService;

        private bool isRunningConversation;
        private bool isHoldingToTalk;
        private bool isAssistantSpeaking;
        private float holdStartTime;
        private int activeTurnId;
        private Coroutine currentConversationRoutine;

        public bool IsHoldingToTalk => isHoldingToTalk;
        public bool IsRunningConversation => isRunningConversation;
        public bool IsAssistantSpeaking => isAssistantSpeaking;
        public int ActiveTurnId => activeTurnId;

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
                interruptibleTtsService = ttsServiceBehaviour as IInterruptibleTTSService;
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
                Debug.LogWarning("[OpenAIConversationController] MicrophoneRecorder is not assigned. Text mode can still work.");
        }

        private void OnEnable()
        {
            if (pushToTalkAction != null && pushToTalkAction.action != null)
                pushToTalkAction.action.Enable();
        }

        private void OnDisable()
        {
            if (pushToTalkAction != null && pushToTalkAction.action != null)
                pushToTalkAction.action.Disable();
        }

        private void Start()
        {
#if PLATFORM_ANDROID
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
#endif
            if (!SessionManager.HasActiveSession)
            {
                SessionManager.StartNewSession();
                UpdatePanelState("Idle");
                UpdatePanelBackendInfo($"Session initialized: {SessionManager.CurrentSessionId}");
            }
            else
            {
                UpdatePanelState("Idle");
            }
        }

        private void Update()
        {
            if (microphoneRecorder == null)
                return;

            bool pttDown = false;
            bool pttUp = false;

#if UNITY_EDITOR
            if (useKeyboardDebugShortcut && Input.GetKeyDown(keyboardDebugKey))
                pttDown = true;

            if (useKeyboardDebugShortcut && Input.GetKeyUp(keyboardDebugKey))
                pttUp = true;
#endif

            if (useXriPushToTalk && pushToTalkAction != null && pushToTalkAction.action != null)
            {
                if (pushToTalkAction.action.WasPressedThisFrame())
                    pttDown = true;

                if (pushToTalkAction.action.WasReleasedThisFrame())
                    pttUp = true;
            }

            if (pttDown)
                BeginPushToTalk();

            if (pttUp)
                EndPushToTalkAndSend();
        }

        public void StartTextConversation(string userText)
        {
            if (string.IsNullOrWhiteSpace(userText) || isHoldingToTalk)
                return;

            InterruptCurrentAssistantOutput(stopConversationCoroutine: true, cancelPendingWork: true, reason: "StartTextConversation");

            int nextTurnId = ++activeTurnId;
            worldSpaceDebugPanel?.AppendTelemetryEvent($"Text conversation start turn={nextTurnId}");
            currentConversationRoutine = StartCoroutine(RunTextConversation(userText, nextTurnId));
        }

        public void BeginPushToTalk()
        {
            if (isHoldingToTalk || microphoneRecorder == null)
                return;

            if (isRunningConversation)
            {
                if (!allowBargeInWhileSpeaking)
                    return;

                InterruptCurrentAssistantOutput(stopConversationCoroutine: true, cancelPendingWork: true, reason: "PTT barge-in");
            }

            microphoneRecorder.StartRecording();

            if (!microphoneRecorder.IsRecording)
            {
                UpdatePanelState("Error");
                UpdatePanelBackendInfo("No se pudo iniciar la grabación del micrófono.");
                return;
            }

            isHoldingToTalk = true;
            holdStartTime = Time.realtimeSinceStartup;
            UpdatePanelState("Recording");
            UpdatePanelBackendInfo("-");
            worldSpaceDebugPanel?.AppendTelemetryEvent($"PTT begin turn={activeTurnId} speakingInterrupted={allowBargeInWhileSpeaking}");
        }

        public void EndPushToTalkAndSend()
        {
            if (!isHoldingToTalk || microphoneRecorder == null)
                return;

            isHoldingToTalk = false;
            float heldSeconds = Time.realtimeSinceStartup - holdStartTime;

            if (heldSeconds < minimumHoldSeconds)
            {
                if (microphoneRecorder.IsRecording)
                    microphoneRecorder.StopRecording();

                UpdatePanelState("Idle");
                UpdatePanelBackendInfo("Pulsa y mantén un poco más para grabar.");
                return;
            }

            AudioClip clip = microphoneRecorder.StopRecording();
            if (clip == null)
            {
                UpdatePanelState("Error");
                UpdatePanelBackendInfo("No se pudo obtener un AudioClip válido del micrófono.");
                return;
            }

            InterruptCurrentAssistantOutput(stopConversationCoroutine: true, cancelPendingWork: true, reason: "PTT send");
            int nextTurnId = ++activeTurnId;
            worldSpaceDebugPanel?.AppendTelemetryEvent($"PTT end -> send voice turn={nextTurnId} held={heldSeconds:F2}s");
            currentConversationRoutine = StartCoroutine(RunVoiceConversationFromClip(clip, nextTurnId));
        }

        public void StartNewAnonymousUserSession()
        {
            InterruptCurrentAssistantOutput(stopConversationCoroutine: true, cancelPendingWork: true, reason: "New anonymous session");

            if (chatService is IConversationResettable resettable)
                resettable.ResetConversationContext();

            SessionManager.StartNewSession();

            UpdatePanelState("Idle");
            worldSpaceDebugPanel?.SetUserTranscript("-");
            worldSpaceDebugPanel?.SetAssistantTranscript("-");
            worldSpaceDebugPanel?.SetMetrics("-");
            UpdatePanelBackendInfo($"Nuevo usuario activo. Session ID: {SessionManager.CurrentSessionId}");
        }

        private void InterruptCurrentAssistantOutput(bool stopConversationCoroutine, bool cancelPendingWork, string reason)
        {
            if (cancelPendingWork)
                activeTurnId++;

            if (stopConversationCoroutine && currentConversationRoutine != null)
            {
                StopCoroutine(currentConversationRoutine);
                currentConversationRoutine = null;
            }

            isRunningConversation = false;
            isAssistantSpeaking = false;

            interruptibleTtsService?.InterruptPlayback();

            if (avatarAudioSource != null)
            {
                avatarAudioSource.Stop();
                avatarAudioSource.clip = null;
            }

            if (microphoneRecorder != null && microphoneRecorder.IsRecording)
                microphoneRecorder.StopRecording();

            Debug.Log($"[OpenAIConversationController] Interrupted current assistant output. reason={reason}, activeTurnId={activeTurnId}");

            worldSpaceDebugPanel?.AppendTelemetryEvent($"Interrupt reason={reason} turn={activeTurnId}");
        }

        private IEnumerator RunTextConversation(string userText, int turnId)
        {
            isRunningConversation = true;
            isAssistantSpeaking = false;

            var result = new ConversationResult
            {
                userText = userText
            };

            result.timing.StartTotal();

            UpdatePanelState("Chat");
            worldSpaceDebugPanel?.SetUserTranscript(userText);
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

            if (!IsTurnCurrent(turnId))
                yield break;

            result.timing.StopChat();

            if (!string.IsNullOrEmpty(chatError) || chatResult == null || string.IsNullOrWhiteSpace(chatResult.responseText))
            {
                result.error = string.IsNullOrEmpty(chatError) ? "Respuesta de chat vacía." : chatError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            PopulateResultFromChat(result, chatResult);

            worldSpaceDebugPanel?.SetAssistantTranscript(result.assistantText);
            emotionController?.ApplyEmotion(result.emotion, result.intentTags);

            yield return PlayAssistantReplyWithTts(result, turnId);

            if (!IsTurnCurrent(turnId))
                yield break;

            result.timing.StopTotal();
            isRunningConversation = false;
            isAssistantSpeaking = false;
            currentConversationRoutine = null;

            if (SessionManager.HasActiveSession)
                SessionManager.RegisterTurn();

            LogTiming(result);
            UpdatePanelState("Idle");
        }

        private IEnumerator RunVoiceConversationFromClip(AudioClip clip, int turnId)
        {
            isRunningConversation = true;
            isAssistantSpeaking = false;

            var result = new ConversationResult();
            result.timing.StartTotal();

            if (clip == null)
            {
                result.error = "No se grabó audio desde el micrófono.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                isRunningConversation = false;
                yield break;
            }

            UpdatePanelState("STT");
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

            if (!IsTurnCurrent(turnId))
                yield break;

            result.timing.StopStt();

            if (!string.IsNullOrEmpty(sttError) || string.IsNullOrWhiteSpace(userText))
            {
                result.error = string.IsNullOrEmpty(sttError) ? "La transcripción de STT está vacía." : sttError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            result.userText = userText;
            worldSpaceDebugPanel?.SetUserTranscript(userText);

            UpdatePanelState("Chat");
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

            if (!IsTurnCurrent(turnId))
                yield break;

            result.timing.StopChat();

            if (!string.IsNullOrEmpty(chatError) || chatResult == null || string.IsNullOrWhiteSpace(chatResult.responseText))
            {
                result.error = string.IsNullOrEmpty(chatError) ? "Respuesta de chat vacía." : chatError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            PopulateResultFromChat(result, chatResult);

            worldSpaceDebugPanel?.SetAssistantTranscript(result.assistantText);
            emotionController?.ApplyEmotion(result.emotion, result.intentTags);

            yield return PlayAssistantReplyWithTts(result, turnId);

            if (!IsTurnCurrent(turnId))
                yield break;

            result.timing.StopTotal();
            isRunningConversation = false;
            isAssistantSpeaking = false;
            currentConversationRoutine = null;

            if (SessionManager.HasActiveSession)
                SessionManager.RegisterTurn();

            LogTiming(result);
            UpdatePanelState("Idle");
        }

        private IEnumerator PlayAssistantReplyWithTts(ConversationResult result, int turnId)
        {
            if (string.IsNullOrWhiteSpace(result.assistantText))
            {
                result.error = "assistantText está vacío, no hay nada que sintetizar.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            if (streamingTtsService != null)
            {
                UpdatePanelState("TTS");
                result.timing.StartTts();

                string streamingError = null;
                bool playbackMarked = false;

                yield return streamingTtsService.RequestSpeechStreamed(
                    result.assistantText,
                    avatarAudioSource,
                    onPlaybackStarted: () =>
                    {
                        if (!IsTurnCurrent(turnId) || playbackMarked)
                            return;

                        playbackMarked = true;
                        isAssistantSpeaking = true;
                        result.timing.StopTts();
                        result.timing.MarkPlaybackStart();
                        UpdatePanelState("Speaking");
                        worldSpaceDebugPanel?.AppendTelemetryEvent($"Playback started turn={turnId}");
                    },
                    onError: err =>
                    {
                        if (!IsTurnCurrent(turnId))
                            return;

                        streamingError = err;
                    },
                    onCompleted: () =>
                    {
                        if (!IsTurnCurrent(turnId))
                            return;

                        isAssistantSpeaking = false;
                        result.timing.MarkPlaybackEnd();
                        worldSpaceDebugPanel?.AppendTelemetryEvent($"Playback completed turn={turnId}");
                    });

                if (!IsTurnCurrent(turnId))
                    yield break;

                if (!string.IsNullOrEmpty(streamingError))
                {
                    result.error = streamingError;
                    isAssistantSpeaking = false;
                    UpdatePanelState("Error");
                    UpdatePanelBackendInfo(streamingError);
                    yield break;
                }

                if (!playbackMarked)
                {
                    result.timing.StopTts();
                    result.timing.MarkPlaybackStart();
                }

                if (result.timing.playbackEndTime <= 0.0)
                    result.timing.MarkPlaybackEnd();

                isAssistantSpeaking = false;
                yield break;
            }

            if (ttsService == null)
            {
                result.error = "TTS service not configured.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            UpdatePanelState("TTS");
            result.timing.StartTts();

            AudioClip ttsClip = null;
            string ttsError = null;

            yield return ttsService.RequestSpeech(result.assistantText, (clip, err) =>
            {
                ttsClip = clip;
                ttsError = err;
            });

            if (!IsTurnCurrent(turnId))
                yield break;

            result.timing.StopTts();

            if (!string.IsNullOrEmpty(ttsError))
            {
                result.error = ttsError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(ttsError);
                yield break;
            }

            if (ttsClip == null)
            {
                result.error = "TTS devolvió un clip nulo.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            UpdatePanelState("Speaking");
            isAssistantSpeaking = true;
            worldSpaceDebugPanel?.AppendTelemetryEvent($"Non-streaming playback started turn={turnId}");
            avatarAudioSource.Stop();
            avatarAudioSource.clip = ttsClip;
            avatarAudioSource.Play();

            result.timing.MarkPlaybackStart();
            yield return new WaitForSeconds(ttsClip.length);

            if (!IsTurnCurrent(turnId))
                yield break;

            isAssistantSpeaking = false;
            result.timing.MarkPlaybackEnd();
            worldSpaceDebugPanel?.AppendTelemetryEvent($"Non-streaming playback completed turn={turnId}");
        }

        private bool IsTurnCurrent(int turnId)
        {
            return turnId == activeTurnId;
        }

        private void PopulateResultFromChat(ConversationResult result, ChatServiceResult chatResult)
        {
            result.assistantText = chatResult.responseText;
            result.emotion = chatResult.emotion;
            result.intentTags = chatResult.intentTags ?? new List<IntentTag>();
            result.backendLatencyMs = chatResult.latencyMs;
            result.backendModel = chatResult.model;
            result.backendRagHits = chatResult.ragHits;
            result.backendSourceTitles = chatResult.sourceTitles ?? new List<string>();
        }

        private void LogTiming(ConversationResult result)
        {
            if (result == null)
                return;

            string metricsText =
                $"STT: {result.timing.SttSeconds:F2}s | Chat: {result.timing.ChatSeconds:F2}s | TTS: {result.timing.TtsSeconds:F2}s\n" +
                $"Time to first audio: {result.timing.TimeToFirstAudioSeconds:F2}s | " +
                $"Playback duration: {result.timing.PlaybackDurationSeconds:F2}s | " +
                $"Time to playback end: {result.timing.TimeToPlaybackEndSeconds:F2}s | " +
                $"Turn coroutine complete: {result.timing.TurnCompleteSeconds:F2}s";

            if (includeAssistantTextInMetrics && !string.IsNullOrWhiteSpace(result.assistantText))
                metricsText += $"\n\nAI: {result.assistantText}";

            string backendInfo = BuildBackendInfo(result);

            Debug.Log($"[OpenAIConversationController] {metricsText.Replace("\n", " | ")}");

            worldSpaceDebugPanel?.SetMetrics(metricsText);
            worldSpaceDebugPanel?.SetBackendInfo(backendInfo);
        }

        private string BuildBackendInfo(ConversationResult result)
        {
            var lines = new List<string>();

            if (result.backendLatencyMs > 0 || !string.IsNullOrWhiteSpace(result.backendModel))
            {
                lines.Add(
                    $"Backend latency: {result.backendLatencyMs} ms | " +
                    $"Backend model: {result.backendModel ?? "n/a"} | " +
                    $"RAG hits: {result.backendRagHits}");
            }

            if (includeSourcesInBackendInfo &&
                result.backendSourceTitles != null &&
                result.backendSourceTitles.Count > 0)
            {
                lines.Add("Sources: " + string.Join(", ", result.backendSourceTitles));
            }

            if (!string.IsNullOrWhiteSpace(result.error))
            {
                lines.Add("Error: " + result.error);
            }

            return lines.Count > 0 ? string.Join("\n", lines) : "-";
        }

        private void UpdatePanelState(string state)
        {
            worldSpaceDebugPanel?.SetState(state);
        }

        private void UpdatePanelBackendInfo(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                worldSpaceDebugPanel?.SetBackendInfo(message);
        }
    }
}