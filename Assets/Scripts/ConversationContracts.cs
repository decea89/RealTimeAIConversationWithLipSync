using System;
using System.Collections;
using UnityEngine;

namespace MVP.Conversation
{
    public interface ITTSService
    {
        IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete);
    }

    public interface ISTTService
    {
        IEnumerator Transcribe(byte[] audioBytes, Action<string, string> onComplete);
    }

    public interface IChatService
    {
        IEnumerator RequestChat(string userMessage, Action<string, string> onComplete);
        IEnumerator RequestChatRich(string userMessage, Action<ChatServiceResult, string> onComplete);
    }

    public interface IStreamingTTSService
    {
        IEnumerator RequestSpeechStreamed(
            string text,
            AudioSource targetAudioSource,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted);
    }

}