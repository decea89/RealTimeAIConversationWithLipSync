using System;
using System.Collections.Generic;

namespace MVP.Conversation
{
    [Serializable]
    public class ClientChatRequestDto
    {
        public string session_id;
        public string user_text;
        public string character_id;
        public ClientChatMetadataRequestDto metadata;
    }

    [Serializable]
    public class ClientChatMetadataRequestDto
    {
        public string locale;
        public string user_id;
    }

    [Serializable]
    public class ClientChatResponseDto
    {
        public string session_id;
        public string response_text;
        public string emotion;
        public List<string> intent_tags;
        public List<ClientSourceDto> sources;
        public ClientChatMetadataResponseDto metadata;

    }

    [Serializable]
    public class ClientSourceDto
    {
        public string title;
        public float score;
    }

    [Serializable]
    public class ClientChatMetadataResponseDto
    {
        public int latency_ms;
        public string model;
        public int rag_hits;
    }
}
