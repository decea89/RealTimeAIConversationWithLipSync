using System;
using System.Collections.Generic;
using UnityEngine;

namespace MVP.Conversation
{
    public enum ConversationState
    {
        Idle,
        Processing,
        Speaking,
        Error
    }

    [Serializable]
    public class ConversationTiming
    {
        public double requestStartTime;
        public double sttStartTime;
        public double chatStartTime;
        public double ttsStartTime;
        public double llmResponseTime;
        public double ttsReadyTime;
        public double playbackStartTime;
        public double turnCompleteTime;

        public void StartTotal() => requestStartTime = Time.realtimeSinceStartupAsDouble;
        public void MarkPlaybackStart() => playbackStartTime = Time.realtimeSinceStartupAsDouble;
        public void StopTotal() => turnCompleteTime = Time.realtimeSinceStartupAsDouble;

        public void StartStt() => sttStartTime = Time.realtimeSinceStartupAsDouble;
        public void StopStt() { }

        public void StartChat() => chatStartTime = Time.realtimeSinceStartupAsDouble;
        public void StopChat() => llmResponseTime = Time.realtimeSinceStartupAsDouble;

        public void StartTts() => ttsStartTime = Time.realtimeSinceStartupAsDouble;
        public void StopTts() => ttsReadyTime = Time.realtimeSinceStartupAsDouble;

        public double SttSeconds => chatStartTime > sttStartTime ? chatStartTime - sttStartTime : 0.0;
        public double ChatSeconds => llmResponseTime > chatStartTime ? llmResponseTime - chatStartTime : 0.0;
        public double TtsSeconds => ttsReadyTime > ttsStartTime ? ttsReadyTime - ttsStartTime : 0.0;
        public double TimeToFirstAudioSeconds => playbackStartTime > requestStartTime ? playbackStartTime - requestStartTime : 0.0;
        public double TurnCompleteSeconds => turnCompleteTime > requestStartTime ? turnCompleteTime - requestStartTime : 0.0;
    }

    [Serializable]
    public class ConversationResult
    {
        public string userText;
        public string assistantText;
        public CharacterEmotion emotion;
        public List<IntentTag> intentTags = new List<IntentTag>();
        public string error;
        public ConversationTiming timing = new ConversationTiming();
    }
}