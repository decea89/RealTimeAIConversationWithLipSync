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
        [Header("Core References")]
        [SerializeField] private OpenAIConversationController conversationController;
        [SerializeField] private AudioSource avatarAudioSource;
        [SerializeField] private RealtimeOpenAITTSClient realtimeTtsClient;
        [SerializeField] private MonoBehaviour lipSyncBehaviour;
        [SerializeField] private MonoBehaviour emotionBehaviour;

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

        [Header("Buttons")]
        [SerializeField] private Button newUserButton;
        [SerializeField] private Button clearTranscriptButton;
        [SerializeField] private Button clearLogsButton;

        [Header("Optional")]
        [SerializeField] private bool captureUnityLogs = true;
        [SerializeField] private int maxLogLines = 12;
        [SerializeField] private bool includeControllerHeartbeat = true;

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

            if (newUserButton != null)
                newUserButton.onClick.AddListener(OnNewUserClicked);

            if (clearTranscriptButton != null)
                clearTranscriptButton.onClick.AddListener(OnClearTranscriptClicked);

            if (clearLogsButton != null)
                clearLogsButton.onClick.AddListener(OnClearLogsClicked);
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

            if (newUserButton != null)
                newUserButton.onClick.RemoveListener(OnNewUserClicked);

            if (clearTranscriptButton != null)
                clearTranscriptButton.onClick.RemoveListener(OnClearTranscriptClicked);

            if (clearLogsButton != null)
                clearLogsButton.onClick.RemoveListener(OnClearLogsClicked);
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

            if (stateText != null)
                stateText.text = BuildStateText();

            if (metricsText != null)
                metricsText.text = BuildMetricsText();

            if (lipSyncToggle != null && lipSyncBehaviour != null)
                lipSyncToggle.SetIsOnWithoutNotify(lipSyncBehaviour.enabled);

            if (emotionToggle != null && emotionBehaviour != null)
                emotionToggle.SetIsOnWithoutNotify(emotionBehaviour.enabled);

            if (volumeSlider != null && avatarAudioSource != null)
            {
                volumeSlider.SetValueWithoutNotify(avatarAudioSource.volume);
                UpdateSliderLabel(volumeValueText, avatarAudioSource.volume, "0.00");
            }
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

            if (realtimeTtsClient == null)
                return;

            if (maxChunkCharsSlider != null)
            {
                float value = realtimeTtsClient.MaxChunkChars;
                maxChunkCharsSlider.SetValueWithoutNotify(value);
                UpdateSliderLabel(maxChunkCharsValueText, value, "0");
            }

            if (interChunksGapSlider != null)
            {
                float value = realtimeTtsClient.InterChunkGapSeconds;
                interChunksGapSlider.SetValueWithoutNotify(value);
                UpdateSliderLabel(interChunksGapValueText, value, "0.000");
            }
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
            if (realtimeTtsClient == null)
                return;

            int intValue = Mathf.RoundToInt(value);
            realtimeTtsClient.MaxChunkChars = intValue;
            UpdateSliderLabel(maxChunkCharsValueText, intValue, "0");
            AppendLocalLog($"Max chunk chars -> {intValue}");
        }

        private void OnInterChunksGapSliderChanged(float value)
        {
            if (realtimeTtsClient == null)
                return;

            realtimeTtsClient.InterChunkGapSeconds = value;
            UpdateSliderLabel(interChunksGapValueText, value, "0.000");
            AppendLocalLog($"Inter-chunk gap -> {value:0.000}");
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