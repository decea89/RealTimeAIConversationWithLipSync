using System;
using UnityEngine;

namespace MVP.Conversation
{
    [CreateAssetMenu(fileName = "ConversationSettings", menuName = "MVP/Conversation Settings")]
    public class ConversationSettings : ScriptableObject
    {
        private const string ResourceName = "ConversationSettings";
        private static ConversationSettings instance;

        public static ConversationSettings Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<ConversationSettings>(ResourceName);

                    if (instance == null)
                    {
                        instance = CreateInstance<ConversationSettings>();
                        instance.hideFlags = HideFlags.DontSave;
                        Debug.LogWarning("[ConversationSettings] Resources/ConversationSettings.asset not found. Using in-memory defaults.");
                    }
                }

                return instance;
            }
        }

        [Serializable]
        public class OpenAIChatSettings
        {
            [Tooltip("OpenAI chat completion endpoint used for conversational responses.")]
            public string endpoint = "https://api.openai.com/v1/chat/completions";

            [Tooltip("OpenAI chat model used to generate responses.")]
            public string model = "gpt-4.1-nano";

            [Tooltip("Request timeout in seconds for chat completion calls.")]
            public int requestTimeoutSeconds = 90;

            [Tooltip("System prompt that defines the persona, tone, and speaking style.")]
            [TextArea(4, 8)]
            public string systemPrompt = @"You are Francisco de Vitoria, a historical figure in a VR experience. Answer briefly and clearly (fewer than 60 words, unless otherwise requested). Speak in European Spanish, Castilian Spanish, with a natural pronunciation from Castile and Leon. Keep a smooth rhythm with short pauses.
Avoid a Latin American accent, avoid voseo, and avoid Latin American seseo.
Use a cultured tone, appropriate for a Spanish historical figure. Keep responses short: 1-2 short sentences.";

            [Tooltip("Sampling temperature used for chat completion generation.")]
            public float temperature = 0.6f;

            [Tooltip("Maximum number of completion tokens to request from the model.")]
            public int maxCompletionTokens = 180;
        }

        [Serializable]
        public class OpenAISTTSettings
        {
            [Tooltip("OpenAI transcription endpoint used for speech-to-text requests.")]
            public string endpoint = "https://api.openai.com/v1/audio/transcriptions";

            [Tooltip("OpenAI transcription model used for speech recognition.")]
            public string model = "gpt-4o-mini-transcribe";

            [Tooltip("Response format returned by the transcription endpoint.")]
            public string responseFormat = "json";

            [Tooltip("Preferred language code for transcription requests.")]
            public string language = "es";

            [Tooltip("Request timeout in seconds for transcription calls.")]
            public int requestTimeoutSeconds = 90;

            [Tooltip("Enable detailed logging of transcription requests and responses.")]
            public bool logRequestDetails = false;
        }

        [Serializable]
        public class StreamingOpenAITTSSettings
        {
            [Tooltip("OpenAI speech endpoint used for text-to-speech streaming.")]
            public string endpoint = "https://api.openai.com/v1/audio/speech";

            [Tooltip("OpenAI text-to-speech model used for streamed audio generation.")]
            public string model = "gpt-4o-mini-tts";

            [Tooltip("Voice name used for OpenAI text-to-speech output.")]
            public string voice = "onyx";

            [Tooltip("Instruction prompt that controls pronunciation, pacing, and delivery.")]
            [TextArea(4, 8)]
            public string instructions = @"Speak in European Spanish, Castilian Spanish, with a natural pronunciation from Castile and Leon. Keep a fast, fluid rhythm with short pauses.
Avoid a Latin American accent, avoid voseo, and avoid Latin American seseo.
Use a cultured tone, appropriate for a Spanish historical figure. Keep responses short: 1-2 short sentences.";

            [Tooltip("Playback speed multiplier used by the streaming TTS voice.")]
            public float speed = 1.25f;

            [Tooltip("Sample rate in Hz used when decoding streamed audio.")]
            public int sampleRate = 24000;

            [Tooltip("Number of audio channels expected in the streamed output.")]
            public int channels = 1;

            [Tooltip("Maximum clip length in seconds used for debug buffering.")]
            public int maxClipSeconds = 60;

            [Tooltip("Request timeout in seconds for streaming TTS calls.")]
            public int requestTimeoutSeconds = 90;

            [Tooltip("Maximum time to wait for the first streamed audio chunk.")]
            public float firstChunkTimeoutSeconds = 6f;

            [Tooltip("Maximum silence gap allowed between streamed chunks before timing out.")]
            public float chunkSilenceTimeoutSeconds = 10f;

            [Tooltip("Amount of audio to buffer before starting playback.")]
            public float prebufferSeconds = 0.6f;

            [Tooltip("Extra time to keep draining audio after the last chunk arrives.")]
            public float drainGraceSeconds = 4f;

            [Tooltip("Enable per-chunk debug logging for streamed audio.")]
            public bool logChunks = false;

            [Tooltip("Minimum chunk gap in seconds that triggers a warning in logs.")]
            public float largeChunkGapWarningSeconds = 0.25f;

            [Tooltip("Capture the full PCM stream for debug inspection.")]
            public bool captureFullPcmForDebug = false;

            [Tooltip("Log the expected duration of the generated audio clip.")]
            public bool logExpectedDuration = false;

            [Tooltip("Build a debug AudioClip when the stream completes.")]
            public bool buildDebugClipOnComplete = false;
        }

        [Serializable]
        public class StreamingElevenLabsTTSSettings
        {
            [Tooltip("ElevenLabs voice identifier used for text-to-speech output.")]
            public string voiceId = "orF2qy9215xjwqqxqsWW";

            [Tooltip("ElevenLabs model identifier used for synthesis.")]
            public string modelId = "eleven_turbo_v2.5";

            [Tooltip("Optional language code passed to ElevenLabs for synthesis.")]
            public string languageCode = string.Empty;

            [Tooltip("Enable verbose logging for ElevenLabs requests and playback.")]
            public bool verboseLogging = true;

            [Tooltip("Latency optimization level requested from the ElevenLabs API.")]
            public int optimizeStreamingLatency = -1;

            [Tooltip("Speaking speed multiplier used by ElevenLabs.")]
            public float speed = 1f;

            [Tooltip("Voice stability value used by ElevenLabs voice settings.")]
            public float stability = 0.5f;

            [Tooltip("Similarity boost value used by ElevenLabs voice settings.")]
            public float similarityBoost = 0.8f;

            [Tooltip("Style value used by ElevenLabs voice settings.")]
            public float style = 0f;

            [Tooltip("Enable speaker boost when supported by the selected voice.")]
            public bool useSpeakerBoost = true;

            [Tooltip("Output audio format requested from the ElevenLabs API.")]
            public ElevenLabsOutputFormat outputFormat = ElevenLabsOutputFormat.Pcm_22050;

            [Tooltip("Request timeout in seconds for ElevenLabs synthesis calls.")]
            public int requestTimeoutSeconds = 90;

            [Tooltip("Maximum time to wait for the first ElevenLabs audio chunk.")]
            public float firstChunkTimeoutSeconds = 4f;

            [Tooltip("Maximum silence gap allowed between ElevenLabs audio chunks.")]
            public float chunkSilenceTimeoutSeconds = 2.5f;

            [Tooltip("Amount of audio to buffer before starting ElevenLabs playback.")]
            public float prebufferSeconds = 0.35f;

            [Tooltip("Extra time to keep draining audio after the last ElevenLabs chunk arrives.")]
            public float drainGraceSeconds = 2f;

            [Tooltip("Capture the full PCM stream for debug inspection.")]
            public bool captureFullPcmForDebug = true;

            [Tooltip("Log the expected duration of the generated ElevenLabs clip.")]
            public bool logExpectedDuration = true;

            [Tooltip("Build a debug AudioClip when the ElevenLabs stream completes.")]
            public bool buildDebugClipOnComplete = false;

            [Tooltip("Minimum chunk gap in seconds that triggers a warning in logs.")]
            public float largeChunkGapWarningSeconds = 0.25f;

            [Tooltip("Maximum clip length in seconds used for debug buffering.")]
            public int maxClipSeconds = 60;
        }

        [Serializable]
        public class RealtimeOpenAITTSSettings
        {
            [Tooltip("Maximum number of characters sent per pseudo-streaming chunk.")]
            public int maxChunkChars = 30;

            [Tooltip("Delay in seconds inserted between pseudo-streaming chunks.")]
            public float interChunkGapSeconds = 0f;

            [Tooltip("Enable verbose logging for realtime OpenAI streaming.")]
            public bool verboseLogging = false;

            [Tooltip("Enable telemetry collection for realtime streaming diagnostics.")]
            public bool enableTelemetry = false;

            [Tooltip("Maximum time in seconds to wait before a queued chunk is considered stalled.")]
            public float enqueueStallTimeoutSeconds = 15f;

            [Tooltip("Crossfade duration in milliseconds used between streamed chunks.")]
            public float chunkCrossfadeMs = 60f;
        }

        [Serializable]
        public class RealtimeAudioPlayerSettings
        {
            [Tooltip("Total sample capacity of the realtime audio buffer.")]
            public int bufferCapacitySamples = 705600;

            [Tooltip("Number of samples required before playback starts.")]
            public int prebufferSamples = 4096;

            [Tooltip("Safety margin in samples kept before the playback cursor.")]
            public int startSafetySamples = 4096;

            [Tooltip("Maximum time in milliseconds to wait for adaptive playback start.")]
            public int adaptiveStartMaxWaitMs = 80;

            [Tooltip("Number of samples to keep draining after the stream ends.")]
            public int drainGraceSamples = 1024;
        }

        [Serializable]
        public class ConversationControllerSettings
        {
            [Tooltip("Enable the keyboard shortcut used for debugging the conversation flow.")]
            public bool useKeyboardDebugShortcut = true;

            [Tooltip("Keyboard key used for the debug shortcut.")]
            public KeyCode keyboardDebugKey = KeyCode.Space;

            [Tooltip("Enable push-to-talk input through the XR Interaction Toolkit.")]
            public bool useXriPushToTalk = true;

            [Tooltip("Minimum time in seconds the push-to-talk button must be held.")]
            public float minimumHoldSeconds = 0.12f;

            [Tooltip("Allow the assistant to be interrupted while speaking.")]
            public bool allowBargeInWhileSpeaking = true;

            [Tooltip("Include assistant text output in conversation metrics.")]
            public bool includeAssistantTextInMetrics = true;

            [Tooltip("Include source information in backend status details.")]
            public bool includeSourcesInBackendInfo = true;
        }

        [Serializable]
        public class WorldSpaceDebugPanelSettings
        {
            [Tooltip("Capture Unity log messages for display in the world-space debug panel.")]
            public bool captureUnityLogs = true;

            [Tooltip("Maximum number of log lines kept in the world-space debug panel.")]
            public int maxLogLines = 8;

            [Tooltip("Show the controller heartbeat in the world-space debug panel.")]
            public bool includeControllerHeartbeat = true;
        }

        [Serializable]
        public class NewUserSessionInputSettings
        {
            [Tooltip("Use the right secondary controller button as a fallback input for starting a session.")]
            public bool useRightSecondaryButtonFallback = true;
        }

        [Header("OpenAI Chat Configuration")]
        [SerializeField, Tooltip("Configuration group for OpenAI chat completions.")] private OpenAIChatSettings chat = new OpenAIChatSettings();

        [Header("OpenAI Speech-to-Text Configuration")]
        [SerializeField, Tooltip("Configuration group for OpenAI transcription requests.")] private OpenAISTTSettings stt = new OpenAISTTSettings();

        [Header("OpenAI Streaming Text-to-Speech Configuration")]
        [SerializeField, Tooltip("Configuration group for streamed OpenAI speech synthesis.")] private StreamingOpenAITTSSettings streamingOpenAiTts = new StreamingOpenAITTSSettings();

        [Header("ElevenLabs Streaming Text-to-Speech Configuration")]
        [SerializeField, Tooltip("Configuration group for streamed ElevenLabs speech synthesis.")] private StreamingElevenLabsTTSSettings streamingElevenLabsTts = new StreamingElevenLabsTTSSettings();

        [Header("Realtime Streaming Orchestration Configuration")]
        [SerializeField, Tooltip("Configuration group for realtime OpenAI streaming orchestration.")] private RealtimeOpenAITTSSettings realtimeOpenAiTts = new RealtimeOpenAITTSSettings();

        [Header("Realtime Audio Playback Configuration")]
        [SerializeField, Tooltip("Configuration group for realtime audio buffering and playback.")] private RealtimeAudioPlayerSettings realtimeAudioPlayer = new RealtimeAudioPlayerSettings();

        [Header("Conversation Controller Configuration")]
        [SerializeField, Tooltip("Configuration group for conversation flow and input control.")] private ConversationControllerSettings conversationController = new ConversationControllerSettings();

        [Header("World-Space Debug Panel Configuration")]
        [SerializeField, Tooltip("Configuration group for the world-space debug panel.")] private WorldSpaceDebugPanelSettings worldSpaceDebugPanel = new WorldSpaceDebugPanelSettings();

        [Header("New User Session Input Configuration")]
        [SerializeField, Tooltip("Configuration group for new user session input handling.")] private NewUserSessionInputSettings newUserSessionInput = new NewUserSessionInputSettings();

        public OpenAIChatSettings Chat => chat;
        public OpenAISTTSettings Stt => stt;
        public StreamingOpenAITTSSettings StreamingOpenAiTts => streamingOpenAiTts;
        public StreamingElevenLabsTTSSettings StreamingElevenLabsTts => streamingElevenLabsTts;
        public RealtimeOpenAITTSSettings RealtimeOpenAiTts => realtimeOpenAiTts;
        public RealtimeAudioPlayerSettings RealtimeAudioPlayer => realtimeAudioPlayer;
        public ConversationControllerSettings ConversationController => conversationController;
        public WorldSpaceDebugPanelSettings WorldSpaceDebugPanel => worldSpaceDebugPanel;
        public NewUserSessionInputSettings NewUserSessionInput => newUserSessionInput;
    }
}
