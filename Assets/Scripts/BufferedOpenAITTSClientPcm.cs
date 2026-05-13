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
        [SerializeField]
        [Tooltip("Tu API key de OpenAI. Mantener privado.")]
        private string apiKey = "YOUR_OPENAI_API_KEY";
        
        [SerializeField]
        [Tooltip("Endpoint de OpenAI para TTS. No cambiar a menos que uses proxy.")]
        private string endpoint = "https://api.openai.com/v1/audio/speech";
        
        [SerializeField]
        [Tooltip("Modelo TTS a usar. 'gpt-4o-mini-tts' es el default rápido.")]
        private string model = "gpt-4o-mini-tts";
        
        [SerializeField]
        [Tooltip("Voz a usar. Opciones: coral/sage/shimmer/echo/alloy. 'echo' es profesional.")]
        private string voice = "echo";
        
        [SerializeField]
        [Range(16000, 48000)]
        [Tooltip("Frecuencia de muestreo (Hz). 24000 por defecto.")]
        private int sampleRate = 24000;
        
        [SerializeField]
        [Range(1, 2)]
        [Tooltip("Canales (1=mono, 2=estéreo). Mantener en 1 para VR.")]
        private int channels = 1;

        [Header("Voice Tuning")]
        [SerializeField]
        [Range(0.25f, 4.0f)]
        [Tooltip("Velocidad de voz. 0.5=lento y profundo. 1.0=normal. 2.0=rápido y agudo.")]
        private float speed = 1.10f;

        [Header("Request Guards")]
        [SerializeField]
        [Range(5, 180)]
        [Tooltip("Timeout TTS (s). Aumentar si respuestas largas se cortan. 90s recomendado.")]
        private int requestTimeoutSeconds = 45;
        
        [SerializeField]
        [Range(40, 2000)]
        [Tooltip("Máximo de caracteres por solicitud TTS. Límite de OpenAI: ~4000. Limitar para latencia.")]
        private int maxInputChars = 500;

        [SerializeField]
        [TextArea(2, 5)]
        [Tooltip("Instrucciones al modelo sobre cómo sonar. Afecta acento, velocidad, emociones.")]
        private string instructions =
            "Speak in Spanish from Spain with a natural Castilian accent. " +
            "Use a fluid, confident, conversational delivery with shorter pauses. " +
            "Keep the speech clear and natural, but not slow or solemn.";

        public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onComplete?.Invoke(null, "BufferedOpenAITTSClientPcm: text vacío.");
                yield break;
            }

            string trimmedText = text.Trim();
            if (trimmedText.Length > maxInputChars)
            {
                Debug.LogWarning($"[BufferedOpenAITTSClientPcm] input recortado de {trimmedText.Length} a {maxInputChars} chars para limitar latencia.");
                trimmedText = trimmedText.Substring(0, maxInputChars);
            }

            var body = new OpenAITtsRequest
            {
                model = model,
                input = trimmedText,
                voice = voice,
                instructions = instructions,
                response_format = "pcm",
                speed = speed
            };

            string json = JsonUtility.ToJson(body);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);

            using var request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(5, requestTimeoutSeconds);
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

            Debug.Log($"[BufferedOpenAITTSClientPcm] speed={speed:0.00}, PCM bytes={pcmBytes.Length}");

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
            public float speed;
        }
    }
}