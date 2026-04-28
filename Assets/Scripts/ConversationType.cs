using System;
using System.Collections.Generic;

namespace MVP.Conversation
{
    [Serializable]
    public enum CharacterEmotion
    {
        Neutral,
        Happy,
        Thinking,
        Concerned,
        Angry,
        Sad
    }

    [Serializable]
    public enum IntentTag
    {
        Unknown,
        Greeting,
        KnowledgeAnswer,
        Fallback,
        OutOfScope
    }

    [Serializable]
    public class ChatServiceResult
    {
        public string responseText;
        public CharacterEmotion emotion;
        public List<IntentTag> intentTags = new List<IntentTag>();
        public string rawJson;
        public int latencyMs;
        public string model;
        public int ragHits;

        public List<string> sourceTitles = new List<string>();
    }
}