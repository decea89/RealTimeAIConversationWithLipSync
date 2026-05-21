// WorldSpaceDebugPanelController.cs
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MVP.Conversation
{
    public class WorldSpaceDebugPanelController : MonoBehaviour
    {
        private static ConversationSettings Settings => ConversationSettings.Instance;

        [Header("Core References")]
        [SerializeField]
        [Tooltip("Main conversation controller. Required to monitor state.")]
        private OpenAIConversationController conversationController;
        
        [SerializeField]
        [Tooltip("Avatar AudioSource. Used for volume control.")]
        private AudioSource avatarAudioSource;
        
        [SerializeField]
        [Tooltip("Realtime TTS client. Used to monitor timings and streaming.")]
        private RealtimeOpenAITTSClient realtimeTtsClient;
        
        [SerializeField]
        [Tooltip("Lip Sync behaviour (OVRLipSyncContext). Optional for synchronizing mouth movement.")]
        private MonoBehaviour lipSyncBehaviour;
        
        [SerializeField]
        [Tooltip("Emotion controller. Optional for synchronizing expressions.")]
        private MonoBehaviour emotionBehaviour;

        [Header("Text UI")]
        [SerializeField] private TMP_Text stateText;
        [SerializeField] private TMP_Text sessionText;
        [SerializeField] private TMP_Text userTranscriptText;
        [SerializeField] private TMP_Text assistantTranscriptText;
        [SerializeField] private TMP_Text metricsText;
        [SerializeField] private TMP_Text backendText;
        [SerializeField] private TMP_Text logText;

        [Header("Toggles")]
        [SerializeField] private Toggle lipSyncToggle;
        [SerializeField] private Toggle emotionToggle;
        [SerializeField] private Toggle showAssistantTextToggle;
        [SerializeField] private Toggle showBackendInfoToggle;
        [SerializeField] private Toggle showSourcesToggle;

        [Header("Sliders")]
        [SerializeField] private Slider volumeSlider;
        [SerializeField] private TMP_Text volumeValueText;

        [SerializeField] private Slider maxChunkCharsSlider;
        [SerializeField] private TMP_Text maxChunkCharsValueText;

        [SerializeField] private Slider interChunksGapSlider;
        [SerializeField] private TMP_Text interChunksGapValueText;

        // Minimal controls for non-technical testing
        // RealtimeAudioPlayer controls (kept minimal)
        [SerializeField] private Slider prebufferSlider;
        [SerializeField] private TMP_Text prebufferValueText;

        [SerializeField] private Slider startSafetySlider;
        [SerializeField] private TMP_Text startSafetyValueText;

        [Header("Buttons")]
        [SerializeField] private Button newUserButton;
        [SerializeField] private Button clearTranscriptButton;
        [SerializeField] private Button clearLogsButton;
        [SerializeField] private Button applyLowLatencyButton;
        [SerializeField] private Button applySafeButton;
        [SerializeField] private Button applyDefaultButton;
        [SerializeField] private Button flushTelemetryButton;

        private bool captureUnityLogs
        {
            get => Settings.WorldSpaceDebugPanel.captureUnityLogs;
            set => Settings.WorldSpaceDebugPanel.captureUnityLogs = value;
        }

        private int maxLogLines
        {
            get => Settings.WorldSpaceDebugPanel.maxLogLines;
            set => Settings.WorldSpaceDebugPanel.maxLogLines = value;
        }

        private bool includeControllerHeartbeat
        {
            get => Settings.WorldSpaceDebugPanel.includeControllerHeartbeat;
            set => Settings.WorldSpaceDebugPanel.includeControllerHeartbeat = value;
        }

        private readonly StringBuilder runtimeLogs = new StringBuilder();
        private readonly Queue<string> localLogQueue = new Queue<string>();

        private string lastKnownState = "Idle";
        private string lastUserTranscript = "-";
        private string lastAssistantTranscript = "-";
        private string lastMetrics = "-";
        private string lastBackendInfo = "-";
        private string lastTelemetrySummary = "-";

        private bool previousHoldingToTalk;
        private bool previousRunningConversation;
        private bool previousAssistantSpeaking;
        private bool previousAudioPlaying;
        private int previousTurnId = -1;

        private void Awake()
        {
            WireUI();
            RefreshAll();
        }

        private void OnEnable()
        {
            if (captureUnityLogs)
                Application.logMessageReceived += OnUnityLogReceived;

            SnapshotControllerFlags();
        }

        private void OnDisable()
        {
            if (captureUnityLogs)
                Application.logMessageReceived -= OnUnityLogReceived;

            UnwireUI();
        }

        private void Update()
        {
            RefreshSessionInfo();
            RefreshRuntimeValues();
            RefreshTelemetrySummary();
            PollControllerTelemetry();
        }

        private void WireUI()
        {
            if (lipSyncToggle != null)
                lipSyncToggle.onValueChanged.AddListener(OnLipSyncToggleChanged);

            if (emotionToggle != null)
                emotionToggle.onValueChanged.AddListener(OnEmotionToggleChanged);

            if (showAssistantTextToggle != null)
                showAssistantTextToggle.onValueChanged.AddListener(_ => RefreshTranscriptTexts());

            if (showBackendInfoToggle != null)
                showBackendInfoToggle.onValueChanged.AddListener(_ => RefreshBackendText());

            if (showSourcesToggle != null)
                showSourcesToggle.onValueChanged.AddListener(_ => RefreshBackendText());

            if (volumeSlider != null)
                volumeSlider.onValueChanged.AddListener(OnVolumeSliderChanged);

            if (maxChunkCharsSlider != null)
                maxChunkCharsSlider.onValueChanged.AddListener(OnMaxChunkCharsSliderChanged);

            if (interChunksGapSlider != null)
                interChunksGapSlider.onValueChanged.AddListener(OnInterChunksGapSliderChanged);

            if (prebufferSlider != null)
                prebufferSlider.onValueChanged.AddListener(OnPrebufferChanged);

            if (startSafetySlider != null)
                startSafetySlider.onValueChanged.AddListener(OnStartSafetyChanged);

            if (newUserButton != null)
                newUserButton.onClick.AddListener(OnNewUserClicked);

            if (clearTranscriptButton != null)
                clearTranscriptButton.onClick.AddListener(OnClearTranscriptClicked);

            if (clearLogsButton != null)
                clearLogsButton.onClick.AddListener(OnClearLogsClicked);

            if (applyLowLatencyButton != null)
                applyLowLatencyButton.onClick.AddListener(ApplyLowLatencyPreset);

            if (applySafeButton != null)
                applySafeButton.onClick.AddListener(ApplySafePreset);

            if (applyDefaultButton != null)
                applyDefaultButton.onClick.AddListener(ApplyDefaultPreset);

            if (flushTelemetryButton != null)
                flushTelemetryButton.onClick.AddListener(OnFlushTelemetryClicked);
        }

        private void UnwireUI()
        {
            if (lipSyncToggle != null)
                lipSyncToggle.onValueChanged.RemoveListener(OnLipSyncToggleChanged);

            if (emotionToggle != null)
                emotionToggle.onValueChanged.RemoveListener(OnEmotionToggleChanged);

            if (showAssistantTextToggle != null)
                showAssistantTextToggle.onValueChanged.RemoveAllListeners();

            if (showBackendInfoToggle != null)
                showBackendInfoToggle.onValueChanged.RemoveAllListeners();

            if (showSourcesToggle != null)
                showSourcesToggle.onValueChanged.RemoveAllListeners();

            if (volumeSlider != null)
                volumeSlider.onValueChanged.RemoveListener(OnVolumeSliderChanged);

            if (maxChunkCharsSlider != null)
                maxChunkCharsSlider.onValueChanged.RemoveListener(OnMaxChunkCharsSliderChanged);

            if (interChunksGapSlider != null)
                interChunksGapSlider.onValueChanged.RemoveListener(OnInterChunksGapSliderChanged);

            if (prebufferSlider != null)
                prebufferSlider.onValueChanged.RemoveListener(OnPrebufferChanged);

            if (startSafetySlider != null)
                startSafetySlider.onValueChanged.RemoveListener(OnStartSafetyChanged);

            if (newUserButton != null)
                newUserButton.onClick.RemoveListener(OnNewUserClicked);

            if (clearTranscriptButton != null)
                clearTranscriptButton.onClick.RemoveListener(OnClearTranscriptClicked);

            if (clearLogsButton != null)
                clearLogsButton.onClick.RemoveListener(OnClearLogsClicked);

            if (applyLowLatencyButton != null)
                applyLowLatencyButton.onClick.RemoveListener(ApplyLowLatencyPreset);

            if (applySafeButton != null)
                applySafeButton.onClick.RemoveListener(ApplySafePreset);

            if (applyDefaultButton != null)
                applyDefaultButton.onClick.RemoveListener(ApplyDefaultPreset);

            if (flushTelemetryButton != null)
                flushTelemetryButton.onClick.RemoveListener(OnFlushTelemetryClicked);
        }

        public void SetConversationSnapshot(
            string state,
            string userTranscript,
            string assistantTranscript,
            string metrics,
            string backendInfo)
        {
            lastKnownState = string.IsNullOrWhiteSpace(state) ? "-" : state;
            lastUserTranscript = string.IsNullOrWhiteSpace(userTranscript) ? "-" : userTranscript;
            lastAssistantTranscript = string.IsNullOrWhiteSpace(assistantTranscript) ? "-" : assistantTranscript;
            lastMetrics = string.IsNullOrWhiteSpace(metrics) ? "-" : metrics;
            lastBackendInfo = string.IsNullOrWhiteSpace(backendInfo) ? "-" : backendInfo;

            RefreshAll();
        }

        public void SetState(string value)
        {
            lastKnownState = string.IsNullOrWhiteSpace(value) ? "-" : value;

            if (stateText != null)
                stateText.text = BuildStateText();
        }

        public void SetUserTranscript(string value)
        {
            lastUserTranscript = string.IsNullOrWhiteSpace(value) ? "-" : value;
            RefreshTranscriptTexts();
        }

        public void SetAssistantTranscript(string value)
        {
            lastAssistantTranscript = string.IsNullOrWhiteSpace(value) ? "-" : value;
            RefreshTranscriptTexts();
        }

        public void SetMetrics(string value)
        {
            lastMetrics = string.IsNullOrWhiteSpace(value) ? "-" : value;

            if (metricsText != null)
                metricsText.text = BuildMetricsText();
        }

        public void SetBackendInfo(string value)
        {
            lastBackendInfo = string.IsNullOrWhiteSpace(value) ? "-" : value;
            RefreshBackendText();
        }

        public void AppendTelemetryEvent(string message)
        {
            AppendLocalLog($"[TEL] {message}");
        }

        private void RefreshAll()
        {
            RefreshSessionInfo();
            RefreshTranscriptTexts();
            RefreshBackendText();
            RefreshRuntimeValues();
            RefreshTelemetrySummary();

            // Ensure labels show an initial value even before runtime targets are available.
            InitializeSliderLabelsFromUI();

            if (stateText != null)
                stateText.text = BuildStateText();

            if (metricsText != null)
                metricsText.text = BuildMetricsText();

            if (lipSyncToggle != null)
                lipSyncToggle.SetIsOnWithoutNotify(true);

            if (emotionToggle != null)
                emotionToggle.SetIsOnWithoutNotify(false);

            if (showAssistantTextToggle != null)
                showAssistantTextToggle.SetIsOnWithoutNotify(true);

            if (showBackendInfoToggle != null)
                showBackendInfoToggle.SetIsOnWithoutNotify(true);

            if (showSourcesToggle != null)
                showSourcesToggle.SetIsOnWithoutNotify(true);

            if (lipSyncBehaviour != null)
                lipSyncBehaviour.enabled = true;

            if (emotionBehaviour != null)
                emotionBehaviour.enabled = false;

            // Re-apply view-dependent text after forcing initial toggle states.
            RefreshTranscriptTexts();
            RefreshBackendText();

            if (volumeSlider != null && avatarAudioSource != null)
            {
                volumeSlider.SetValueWithoutNotify(avatarAudioSource.volume);
                UpdateSliderLabel(volumeValueText, avatarAudioSource.volume, "0.00");
            }
        }

        private void InitializeSliderLabelsFromUI()
        {
            if (volumeSlider != null)
                UpdateSliderLabel(volumeValueText, volumeSlider.value, "0.00");

            if (maxChunkCharsSlider != null)
                UpdateSliderLabel(maxChunkCharsValueText, Mathf.RoundToInt(maxChunkCharsSlider.value), "0");

            if (interChunksGapSlider != null)
                UpdateSliderLabel(interChunksGapValueText, interChunksGapSlider.value, "0.000");

            if (prebufferSlider != null)
                UpdateSliderLabel(prebufferValueText, Mathf.RoundToInt(prebufferSlider.value), "0");

            if (startSafetySlider != null)
                UpdateSliderLabel(startSafetyValueText, Mathf.RoundToInt(startSafetySlider.value), "0");
        }

        private void RefreshSessionInfo()
        {
            if (sessionText == null)
                return;

            string sessionId = SessionManager.HasActiveSession ? SessionManager.CurrentSessionId : "-";
            string shortSession = sessionId;

            if (!string.IsNullOrWhiteSpace(sessionId) && sessionId.Length > 8)
                shortSession = sessionId.Substring(0, 8);

            sessionText.text =
                $"Session: {shortSession}\n" +
                $"User Index: {SessionManager.CurrentUserIndex}\n" +
                $"Turn Index: {SessionManager.CurrentTurnIndex}";
        }

        private void RefreshRuntimeValues()
        {
            if (volumeValueText != null && avatarAudioSource != null)
                UpdateSliderLabel(volumeValueText, avatarAudioSource.volume, "0.00");

            if (maxChunkCharsSlider != null && realtimeTtsClient != null)
            {
                float value = realtimeTtsClient.MaxChunkChars;
                maxChunkCharsSlider.SetValueWithoutNotify(value);
                UpdateSliderLabel(maxChunkCharsValueText, value, "0");
            }

            if (interChunksGapSlider != null && realtimeTtsClient != null)
            {
                float value = realtimeTtsClient.InterChunkGapSeconds;
                interChunksGapSlider.SetValueWithoutNotify(value);
                UpdateSliderLabel(interChunksGapValueText, value, "0.000");
            }

            // Sync RealtimeAudioPlayer parameters (minimal)
            if (prebufferSlider != null)
            {
                var player = FindObjectOfType<RealtimeAudioPlayer>();
                if (player != null)
                {
                    prebufferSlider.SetValueWithoutNotify(player.PrebufferSamples);
                    UpdateSliderLabel(prebufferValueText, player.PrebufferSamples, "0");

                    startSafetySlider.SetValueWithoutNotify(player.StartSafetySamples);
                    UpdateSliderLabel(startSafetyValueText, player.StartSafetySamples, "0");
                }
            }
        }

        private void OnPrebufferChanged(float value)
        {
            int v = Mathf.RoundToInt(value);
            UpdateSliderLabel(prebufferValueText, v, "0");

            var player = FindObjectOfType<RealtimeAudioPlayer>();
            if (player == null) return;

            player.PrebufferSamples = v;
            AppendLocalLog($"prebufferSamples -> {v}");
        }

        private void OnStartSafetyChanged(float value)
        {
            int v = Mathf.RoundToInt(value);
            UpdateSliderLabel(startSafetyValueText, v, "0");

            var player = FindObjectOfType<RealtimeAudioPlayer>();
            if (player == null) return;

            player.StartSafetySamples = v;
            AppendLocalLog($"startSafetySamples -> {v}");
        }

        private void RefreshTelemetrySummary()
        {
            if (conversationController == null)
            {
                lastTelemetrySummary = "Controller: n/a";
                return;
            }

            bool audioPlaying = avatarAudioSource != null && avatarAudioSource.isPlaying;
            float audioTime = (avatarAudioSource != null && avatarAudioSource.clip != null) ? avatarAudioSource.time : 0f;
            float clipLength = (avatarAudioSource != null && avatarAudioSource.clip != null) ? avatarAudioSource.clip.length : 0f;

            lastTelemetrySummary =
                $"TurnId: {conversationController.ActiveTurnId} | " +
                $"Running: {conversationController.IsRunningConversation} | " +
                $"Holding: {conversationController.IsHoldingToTalk} | " +
                $"Speaking: {conversationController.IsAssistantSpeaking} | " +
                $"AudioPlaying: {audioPlaying} | " +
                $"Audio: {audioTime:0.00}/{clipLength:0.00}";
        }

        private void PollControllerTelemetry()
        {
            if (!includeControllerHeartbeat || conversationController == null)
                return;

            bool holding = conversationController.IsHoldingToTalk;
            bool running = conversationController.IsRunningConversation;
            bool speaking = conversationController.IsAssistantSpeaking;
            int turnId = conversationController.ActiveTurnId;
            bool audioPlaying = avatarAudioSource != null && avatarAudioSource.isPlaying;

            if (turnId != previousTurnId)
            {
                AppendLocalLog($"[CTRL] ActiveTurnId -> {turnId}");
                previousTurnId = turnId;
            }

            if (holding != previousHoldingToTalk)
            {
                AppendLocalLog($"[CTRL] HoldingToTalk -> {holding}");
                previousHoldingToTalk = holding;
            }

            if (running != previousRunningConversation)
            {
                AppendLocalLog($"[CTRL] RunningConversation -> {running}");
                previousRunningConversation = running;
            }

            if (speaking != previousAssistantSpeaking)
            {
                AppendLocalLog($"[CTRL] AssistantSpeaking -> {speaking}");
                previousAssistantSpeaking = speaking;
            }

            if (audioPlaying != previousAudioPlaying)
            {
                AppendLocalLog($"[AUDIO] AvatarAudioSource.isPlaying -> {audioPlaying}");
                previousAudioPlaying = audioPlaying;
            }

            if (stateText != null)
                stateText.text = BuildStateText();

            if (metricsText != null)
                metricsText.text = BuildMetricsText();
        }

        private void SnapshotControllerFlags()
        {
            if (conversationController == null)
                return;

            previousHoldingToTalk = conversationController.IsHoldingToTalk;
            previousRunningConversation = conversationController.IsRunningConversation;
            previousAssistantSpeaking = conversationController.IsAssistantSpeaking;
            previousTurnId = conversationController.ActiveTurnId;
            previousAudioPlaying = avatarAudioSource != null && avatarAudioSource.isPlaying;
        }

        private void RefreshTranscriptTexts()
        {
            if (userTranscriptText != null)
                userTranscriptText.text = $"User:\n{lastUserTranscript}";

            if (assistantTranscriptText != null)
            {
                bool showAssistant = showAssistantTextToggle == null || showAssistantTextToggle.isOn;
                assistantTranscriptText.text = showAssistant
                    ? $"AI:\n{lastAssistantTranscript}"
                    : "AI:\n(hidden)";
            }
        }

        private void RefreshBackendText()
        {
            if (backendText == null)
                return;

            bool showBackend = showBackendInfoToggle == null || showBackendInfoToggle.isOn;
            backendText.text = showBackend ? lastBackendInfo : "Backend info hidden";
        }

        private string BuildStateText()
        {
            return $"State: {lastKnownState}\n{lastTelemetrySummary}";
        }

        private string BuildMetricsText()
        {
            return string.IsNullOrWhiteSpace(lastMetrics) || lastMetrics == "-"
                ? $"-\n\nTelemetry:\n{lastTelemetrySummary}"
                : $"{lastMetrics}\n\nTelemetry:\n{lastTelemetrySummary}";
        }

        private void OnLipSyncToggleChanged(bool isOn)
        {
            return;
            if (lipSyncBehaviour != null)
            {
                lipSyncBehaviour.enabled = isOn;
                AppendLocalLog($"LipSync {(isOn ? "enabled" : "disabled")}");
            }
        }

        private void OnEmotionToggleChanged(bool isOn)
        {
            if (emotionBehaviour != null)
            {
                emotionBehaviour.enabled = isOn;
                AppendLocalLog($"Emotion controller {(isOn ? "enabled" : "disabled")}");
            }
        }

        private void OnVolumeSliderChanged(float value)
        {
            if (avatarAudioSource != null)
                avatarAudioSource.volume = value;

            UpdateSliderLabel(volumeValueText, value, "0.00");
        }

        private void OnMaxChunkCharsSliderChanged(float value)
        {
            int intValue = Mathf.RoundToInt(value);
            UpdateSliderLabel(maxChunkCharsValueText, intValue, "0");

            if (realtimeTtsClient == null)
                return;

            realtimeTtsClient.MaxChunkChars = intValue;
            AppendLocalLog($"Max chunk chars -> {intValue}");
        }

        private void OnInterChunksGapSliderChanged(float value)
        {
            UpdateSliderLabel(interChunksGapValueText, value, "0.000");

            if (realtimeTtsClient == null)
                return;

            realtimeTtsClient.InterChunkGapSeconds = value;
            AppendLocalLog($"Inter-chunk gap -> {value:0.000}");
        }

        // Streaming handlers
        // Streaming handlers removed (simplified UI)

        // Realtime client handlers removed (simplified UI)

        // Bridge handlers removed (simplified UI)

        // Preset application
        private void ApplyLowLatencyPreset()
        {
            // RealtimeAudioPlayer
            var player = FindObjectOfType<RealtimeAudioPlayer>();
            if (player != null)
            {
                player.PrebufferSamples = 1024;
                player.StartSafetySamples = 512;
            }
            AppendLocalLog("Applied preset: Low Latency (player-only)");
        }

        private void ApplySafePreset()
        {
            var player = FindObjectOfType<RealtimeAudioPlayer>();
            if (player != null)
            {
                player.PrebufferSamples = 4096;
                player.StartSafetySamples = 8192;
            }
            AppendLocalLog("Applied preset: Safe (player-only)");
        }

        private void ApplyDefaultPreset()
        {
            var player = FindObjectOfType<RealtimeAudioPlayer>();
            if (player != null)
            {
                player.PrebufferSamples = 2048;
                player.StartSafetySamples = 4096;
            }
            AppendLocalLog("Applied preset: Default (player-only)");
        }

        private void OnFlushTelemetryClicked()
        {
            MVP.Conversation.LipSyncTelemetry.Flush();
            AppendLocalLog("Telemetry flushed to disk");
        }

        private void OnNewUserClicked()
        {
            if (conversationController != null)
            {
                conversationController.StartNewAnonymousUserSession();
                AppendLocalLog($"New user session started: {SessionManager.CurrentSessionId}");
                RefreshSessionInfo();
            }
        }

        private void OnClearTranscriptClicked()
        {
            lastUserTranscript = "-";
            lastAssistantTranscript = "-";
            lastMetrics = "-";
            lastBackendInfo = "-";

            RefreshTranscriptTexts();

            if (metricsText != null)
                metricsText.text = BuildMetricsText();

            RefreshBackendText();
            AppendLocalLog("Transcript and metrics cleared");
        }

        private void OnClearLogsClicked()
        {
            runtimeLogs.Clear();
            localLogQueue.Clear();

            if (logText != null)
                logText.text = string.Empty;
        }

        private void OnUnityLogReceived(string condition, string stackTrace, LogType type)
        {
            string prefix = type switch
            {
                LogType.Warning => "[W]",
                LogType.Error => "[E]",
                LogType.Exception => "[EX]",
                _ => "[I]"
            };

            AppendLocalLog($"{prefix} {condition}");
        }

        private void AppendLocalLog(string message)
        {
            if (logText == null)
                return;

            string timestamped = $"{DateTime.Now:HH:mm:ss} {message}";
            localLogQueue.Enqueue(timestamped);

            while (localLogQueue.Count > Mathf.Max(1, maxLogLines))
                localLogQueue.Dequeue();

            runtimeLogs.Clear();
            foreach (string line in localLogQueue)
                runtimeLogs.AppendLine(line);

            logText.text = runtimeLogs.ToString();
        }

        private void UpdateSliderLabel(TMP_Text label, float value, string format)
        {
            if (label != null)
                label.text = value.ToString(format);
        }

        private void UpdateSliderLabel(TMP_Text label, int value, string format)
        {
            if (label != null)
                label.text = value.ToString(format);
        }
    }
}