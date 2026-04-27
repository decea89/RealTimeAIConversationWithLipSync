using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace MVP.Conversation
{


public class BufferedOpenAITTSClientPcm : MonoBehaviour, ITTSService
{
    [Header("OpenAI TTS")]
    [SerializeField] private string apiKey = "YOUR_OPENAI_API_KEY";
    [SerializeField] private string endpoint = "https://api.openai.com/v1/audio/speech";
    [SerializeField] private string model = "gpt-4o-mini-tts";
    [SerializeField] private string voice = "echo";
    [SerializeField] private int sampleRate = 24000;
    [SerializeField] private int channels = 1;

    [SerializeField] [TextArea(2, 5)]
    private string instructions =
        "Speak in a warm, natural, conversational tone suitable for a VR character. Keep the pacing clear and slightly expressive.";

    public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            onComplete?.Invoke(null, "BufferedOpenAITTSClientPcm: text vacío.");
            yield break;
        }

        var body = new OpenAITtsRequest
        {
            model = model,
            input = text,
            voice = voice,
            instructions = instructions,
            response_format = "pcm"
        };

        string json = JsonUtility.ToJson(body);
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(json);

        using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            string err = request.error;
            if (!string.IsNullOrWhiteSpace(request.downloadHandler?.text))
                err += "\n" + request.downloadHandler.text;

            onComplete?.Invoke(null, err);
            yield break;
        }

        byte[] pcmBytes = request.downloadHandler.data;
        if (pcmBytes == null || pcmBytes.Length == 0)
        {
            onComplete?.Invoke(null, "BufferedOpenAITTSClientPcm: respuesta PCM vacía.");
            yield break;
        }

        Debug.Log($"[BufferedOpenAITTSClientPcm] PCM bytes={pcmBytes.Length}");

        AudioClip clip = Pcm16ToAudioClip(pcmBytes, "OpenAI_TTS_PCM", sampleRate, channels);
        if (clip == null)
        {
            onComplete?.Invoke(null, "BufferedOpenAITTSClientPcm: no se pudo crear AudioClip a partir de PCM.");
            yield break;
        }

        Debug.Log($"[BufferedOpenAITTSClientPcm] clipLength={clip.length:F2}s, frequency={clip.frequency}, channels={clip.channels}");
        onComplete?.Invoke(clip, null);
    }

    private AudioClip Pcm16ToAudioClip(byte[] pcmBytes, string name, int sampleRate, int channels)
    {
        if (pcmBytes == null || pcmBytes.Length < 2)
            return null;

        int sampleCount = pcmBytes.Length / 2;
        float[] samples = new float[sampleCount];

        int sampleIndex = 0;
        for (int i = 0; i + 1 < pcmBytes.Length; i += 2)
        {
            short pcm = BitConverter.ToInt16(pcmBytes, i);
            samples[sampleIndex++] = pcm / 32768f;
        }

        int frameCount = sampleCount / channels;
        if (frameCount <= 0)
            return null;

        AudioClip clip = AudioClip.Create(name, frameCount, channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    [Serializable]
    private class OpenAITtsRequest
    {
        public string model;
        public string input;
        public string voice;
        public string instructions;
        public string response_format;
    }
}

}