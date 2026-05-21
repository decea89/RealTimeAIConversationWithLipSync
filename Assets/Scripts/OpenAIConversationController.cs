// OpenAIConversationController.cs (rama realtime)
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
        private static ConversationSettings Settings => ConversationSettings.Instance;

        [Header("Dependencies")]
        [SerializeField]
        [Tooltip("Componente con IChatService (OpenAIChatClient). Genera respuestas de IA.")]
        private MonoBehaviour chatServiceBehaviour;
        
        [SerializeField]
        [Tooltip("Componente con ISTTService (OpenAISTTClient). Transcribe audio del usuario.")]
        private MonoBehaviour sttServiceBehaviour;
        
        [SerializeField]
        [Tooltip("Component with IRealtimeTTSService (RealtimeOpenAITTSClient). Converts the response into streamed audio.")]
        private MonoBehaviour realtimeTtsServiceBehaviour;
        
        [SerializeField]
        [Tooltip("MicrophoneRecorder component. Captures user audio.")]
        private MicrophoneRecorder microphoneRecorder;
        
        [SerializeField]
        [Tooltip("Playback AudioSource. Reused for all responses.")]
        private AudioSource avatarAudioSource;
        
        [SerializeField]
        [Tooltip("AvatarEmotionController component (optional). Synchronizes emotions with audio.")]
        private AvatarEmotionController emotionController;
        
        [SerializeField]
        [Tooltip("World-space debug panel. Shows logs and conversation timing.")]
        private WorldSpaceDebugPanelController worldSpaceDebugPanel;

        private bool useKeyboardDebugShortcut
        {
            get => Settings.ConversationController.useKeyboardDebugShortcut;
            set => Settings.ConversationController.useKeyboardDebugShortcut = value;
        }

        private KeyCode keyboardDebugKey
        {
            get => Settings.ConversationController.keyboardDebugKey;
            set => Settings.ConversationController.keyboardDebugKey = value;
        }

        private bool useXriPushToTalk
        {
            get => Settings.ConversationController.useXriPushToTalk;
            set => Settings.ConversationController.useXriPushToTalk = value;
        }

        [SerializeField]
        [Tooltip("Input action for VR push-to-talk. Map it in the Input System.")]
        private InputActionReference pushToTalkAction;

        private float minimumHoldSeconds
        {
            get => Settings.ConversationController.minimumHoldSeconds;
            set => Settings.ConversationController.minimumHoldSeconds = value;
        }

        private bool allowBargeInWhileSpeaking
        {
            get => Settings.ConversationController.allowBargeInWhileSpeaking;
            set => Settings.ConversationController.allowBargeInWhileSpeaking = value;
        }

        private bool includeAssistantTextInMetrics
        {
            get => Settings.ConversationController.includeAssistantTextInMetrics;
            set => Settings.ConversationController.includeAssistantTextInMetrics = value;
        }

        private bool includeSourcesInBackendInfo
        {
            get => Settings.ConversationController.includeSourcesInBackendInfo;
            set => Settings.ConversationController.includeSourcesInBackendInfo = value;
        }

        private IChatService chatService;
        private ISTTService sttService;
        private IRealtimeTTSService realtimeTtsService;

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

            if (realtimeTtsServiceBehaviour != null)
                realtimeTtsService = realtimeTtsServiceBehaviour as IRealtimeTTSService;

            if (chatService == null)
                Debug.LogError("[OpenAIConversationController] Chat service behaviour is not set or does not implement IChatService.");

            if (sttServiceBehaviour != null && sttService == null)
                Debug.LogError("[OpenAIConversationController] STT service behaviour is set but does not implement ISTTService.");

            if (realtimeTtsService == null)
                Debug.LogError("[OpenAIConversationController] realtimeTtsServiceBehaviour is not set or does not implement IRealtimeTTSService.");

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

            InterruptAudioAndConversation("StartTextConversation");

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

                InterruptAudioAndConversation("PTT barge-in");
            }

            microphoneRecorder.StartRecording();

            if (!microphoneRecorder.IsRecording)
            {
                UpdatePanelState("Error");
                UpdatePanelBackendInfo("Could not start microphone recording.");
                return;
            }

            isHoldingToTalk = true;
            holdStartTime = Time.realtimeSinceStartup;
            UpdatePanelState("Recording");
            UpdatePanelBackendInfo("-");
            worldSpaceDebugPanel?.AppendTelemetryEvent($"PTT begin turn={activeTurnId} bargeIn={allowBargeInWhileSpeaking}");
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
                UpdatePanelBackendInfo("Press and hold a little longer to record.");
                return;
            }

            AudioClip clip = microphoneRecorder.StopRecording();
            if (clip == null)
            {
                UpdatePanelState("Error");
                UpdatePanelBackendInfo("Could not obtain a valid AudioClip from the microphone.");
                return;
            }

            InterruptAudioAndConversation("PTT send");

            int nextTurnId = ++activeTurnId;
            worldSpaceDebugPanel?.AppendTelemetryEvent($"PTT end -> send voice turn={nextTurnId} held={heldSeconds:F2}s");
            currentConversationRoutine = StartCoroutine(RunVoiceConversationFromClip(clip, nextTurnId));
        }

        public void StartNewAnonymousUserSession()
        {
            InterruptAudioAndConversation("New anonymous session");

            if (chatService is IConversationResettable resettable)
                resettable.ResetConversationContext();

            SessionManager.StartNewSession();

            UpdatePanelState("Idle");
            worldSpaceDebugPanel?.SetUserTranscript("-");
            worldSpaceDebugPanel?.SetAssistantTranscript("-");
            worldSpaceDebugPanel?.SetMetrics("-");
                UpdatePanelBackendInfo($"New active user. Session ID: {SessionManager.CurrentSessionId}");
        }

        private void InterruptAudioAndConversation(string reason)
        {
            activeTurnId++;

            if (currentConversationRoutine != null)
            {
                StopCoroutine(currentConversationRoutine);
                currentConversationRoutine = null;
            }

            isRunningConversation = false;
            isAssistantSpeaking = false;

            realtimeTtsService?.CancelAll();

            if (avatarAudioSource != null)
            {
                avatarAudioSource.Stop();
                avatarAudioSource.clip = null;
            }

            if (microphoneRecorder != null && microphoneRecorder.IsRecording)
                microphoneRecorder.StopRecording();

            Debug.Log($"[OpenAIConversationController] InterruptAudioAndConversation reason={reason}, activeTurnId={activeTurnId}");
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
                result.error = string.IsNullOrEmpty(chatError) ? "Empty chat response." : chatError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            PopulateResultFromChat(result, chatResult);

            worldSpaceDebugPanel?.SetAssistantTranscript(result.assistantText);
            emotionController?.ApplyEmotion(result.emotion, result.intentTags);

            yield return PlayAssistantReplyRealtime(result, turnId);

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
                result.error = "No audio was recorded from the microphone.";
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
                result.error = string.IsNullOrEmpty(sttError) ? "The STT transcription is empty." : sttError;
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
                result.error = string.IsNullOrEmpty(chatError) ? "Empty chat response." : chatError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                isRunningConversation = false;
                LogTiming(result);
                yield break;
            }

            PopulateResultFromChat(result, chatResult);

            worldSpaceDebugPanel?.SetAssistantTranscript(result.assistantText);
            emotionController?.ApplyEmotion(result.emotion, result.intentTags);

            yield return PlayAssistantReplyRealtime(result, turnId);

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

        private IEnumerator PlayAssistantReplyRealtime(ConversationResult result, int turnId)
        {
            if (string.IsNullOrWhiteSpace(result.assistantText))
            {
                result.error = "assistantText is empty, there is nothing to synthesize.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            if (realtimeTtsService == null)
            {
                result.error = "Realtime TTS service not configured.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            UpdatePanelState("TTS");
            Debug.Log($"[OpenAIConversationController] State -> TTS turn={turnId}");
            result.timing.StartTts();

            bool playbackStarted = false;
            string streamError = null;

            var handle = realtimeTtsService.StartStream(
                result.assistantText,
                turnId,
                onAudioBegan: () =>
                {
                    if (!IsTurnCurrent(turnId) || playbackStarted)
                        return;

                    playbackStarted = true;
                    isAssistantSpeaking = true;

                    result.timing.StopTts();
                    result.timing.MarkPlaybackStart();

                    UpdatePanelState("Speaking");
                    Debug.Log($"[OpenAIConversationController] State -> Speaking turn={turnId}");
                    worldSpaceDebugPanel?.AppendTelemetryEvent($"Realtime playback started turn={turnId}");
                },
                onError: err =>
                {
                    if (!IsTurnCurrent(turnId))
                        return;

                    streamError = err;
                });

            if (handle == null)
            {
                result.error = "Could not start the TTS stream.";
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            float safetyTimeout = 120f;
            float endTime = Time.realtimeSinceStartup + safetyTimeout;

            // Only wait for audio production (enqueue) to finish,
            // not for the entire buffer to drain.
            while (!handle.IsCompleted && Time.realtimeSinceStartup < endTime)
            {
                if (!IsTurnCurrent(turnId))
                    yield break;

                if (!string.IsNullOrEmpty(streamError))
                    break;

                yield return null;
            }

            if (!IsTurnCurrent(turnId))
                yield break;

            if (!string.IsNullOrEmpty(streamError))
            {
                realtimeTtsService.CancelAll();
                result.error = streamError;
                isAssistantSpeaking = false;
                Debug.Log($"[OpenAIConversationController] TTS error turn={turnId}: {streamError}");
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(streamError);
                yield break;
            }

            if (!handle.IsCompleted)
            {
                realtimeTtsService.CancelAll();
                result.error = $"Timeout en TTS realtime tras {safetyTimeout:F0}s.";
                isAssistantSpeaking = false;
                Debug.Log($"[OpenAIConversationController] TTS wait timeout turn={turnId}, handleCompleted={handle.IsCompleted}");
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(result.error);
                yield break;
            }

            isAssistantSpeaking = false;

            if (!string.IsNullOrEmpty(streamError))
            {
                result.error = streamError;
                UpdatePanelState("Error");
                UpdatePanelBackendInfo(streamError);
                yield break;
            }

            // PlaybackEnd here means "we have finished this TTS turn"
            result.timing.MarkPlaybackEnd();
            Debug.Log($"[OpenAIConversationController] TTS end turn={turnId}, playbackStarted={playbackStarted}, handleCompleted={handle.IsCompleted}");
            worldSpaceDebugPanel?.AppendTelemetryEvent($"Realtime playback completed turn={turnId}");
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