using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace MVP.Conversation
{
    public class SegmentedBufferedTTSClient : MonoBehaviour, ITTSService, IStreamingTTSService
    {
        [Header("Dependencies")]
        [SerializeField] private BufferedOpenAITTSClientWav innerTtsClientWav;
        [SerializeField] private BufferedOpenAITTSClientPcm innerTtsClient;

        [Header("Segmentation")]
        [SerializeField] private int maxSegmentChars = 140;
        [SerializeField] private float silenceBetweenSegmentsSeconds = 0.06f;
        [SerializeField] private bool logSegments = true;

        public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
        {
            onComplete?.Invoke(null, "SegmentedBufferedTTSClient: Ruta ITTSService (clip fusionado) no implementada en esta versión. Usa IStreamingTTSService.");
            yield break;
        }

        public IEnumerator RequestSpeechStreamed(
            string text,
            AudioSource targetAudioSource,
            Action onPlaybackStarted,
            Action<string> onError,
            Action onCompleted)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("SegmentedBufferedTTSClient: text vacío.");
                yield break;
            }

            if (innerTtsClient == null)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: innerTtsClient no asignado.");
                yield break;
            }

            if (targetAudioSource == null)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: targetAudioSource nulo.");
                yield break;
            }

            List<string> segments = SplitIntoSegments(text, maxSegmentChars);
            if (segments.Count == 0)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: no hay segmentos válidos.");
                yield break;
            }

            segments = PostProcessSegments(segments);

            if (logSegments)
                Debug.Log($"[SegmentedBufferedTTSClient] Segmentos={segments.Count}");

            bool playbackStarted = false;

            for (int i = 0; i < segments.Count; i++)
            {
                string segment = segments[i];

                if (logSegments)
                    Debug.Log($"[SegmentedBufferedTTSClient] [{i + 1}/{segments.Count}] {segment}");

                AudioClip segmentClip = null;
                string segmentError = null;

                yield return innerTtsClient.RequestSpeech(segment, (clip, err) =>
                {
                    segmentClip = clip;
                    segmentError = err;
                });

                if (!string.IsNullOrEmpty(segmentError))
                {
                    onError?.Invoke($"SegmentedBufferedTTSClient: error en segmento {i + 1}: {segmentError}");
                    yield break;
                }

                if (segmentClip == null)
                {
                    onError?.Invoke($"SegmentedBufferedTTSClient: clip nulo en segmento {i + 1}.");
                    yield break;
                }

                targetAudioSource.Stop();
                targetAudioSource.clip = segmentClip;
                targetAudioSource.Play();

                if (!playbackStarted)
                {
                    playbackStarted = true;
                    onPlaybackStarted?.Invoke();
                }

                float segmentLength = segmentClip.length;
                float endTime = Time.time + segmentLength;

                while (Time.time < endTime)
                {
                    yield return null;
                }

                if (i < segments.Count - 1)
                {
                    // Pausa proporcional a la duración, limitada a un rango.
                    float baseGap = silenceBetweenSegmentsSeconds; // tu valor "medio".
                    float dynamicGap = Mathf.Clamp(segmentLength * 0.15f, 0.03f, baseGap * 1.5f);

                    float gapEnd = Time.time + dynamicGap;
                    while (Time.time < gapEnd)
                    {
                        yield return null;
                    }
                }
            }

            onCompleted?.Invoke();
        }

private List<string> SplitIntoSegments(string text, int maxChars)
{
    string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
    normalized = Regex.Replace(normalized, @"\s+", " ");

    // Cortamos por frases (., !, ?, :, ;) pero sin perder los delimitadores.
    string[] rawPieces = Regex.Split(normalized, @"(?<=[\.!\?:;])\s+");

    List<string> sentences = new List<string>();
    foreach (string piece in rawPieces)
    {
        string trimmed = piece.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
            sentences.Add(trimmed);
    }

    if (sentences.Count == 0 && !string.IsNullOrWhiteSpace(normalized))
        sentences.Add(normalized);

    List<string> segments = new List<string>();
    string current = "";

    foreach (string sentence in sentences)
    {
        // Si cabe entero y el acumulado está razonable, lo añadimos al actual.
        string candidate = string.IsNullOrEmpty(current)
            ? sentence
            : current + " " + sentence;

        if (candidate.Length <= maxChars)
        {
            current = candidate;
            continue;
        }

        // Si lo nuevo revienta el límite, cerramos el segmento actual.
        if (!string.IsNullOrEmpty(current))
            segments.Add(current.Trim());

        // Ahora tratamos esta frase: si también es muy larga, la troceamos por palabras.
        if (sentence.Length <= maxChars)
        {
            current = sentence;
        }
        else
        {
            string[] words = sentence.Split(' ');
            current = "";

            foreach (string word in words)
            {
                string wordCandidate = string.IsNullOrEmpty(current)
                    ? word
                    : current + " " + word;

                if (wordCandidate.Length > maxChars && !string.IsNullOrEmpty(current))
                {
                    segments.Add(current.Trim());
                    current = word;
                }
                else
                {
                    current = wordCandidate;
                }
            }
        }
    }

    if (!string.IsNullOrWhiteSpace(current))
        segments.Add(current.Trim());

    return segments;
}

private List<string> PostProcessSegments(List<string> segments, int shortThreshold = 18)
{
    if (segments == null || segments.Count == 0)
        return segments;

    List<string> merged = new List<string>();
    string current = segments[0];

    for (int i = 1; i < segments.Count; i++)
    {
        string next = segments[i];

        if (current.Length < shortThreshold && next.Length < shortThreshold)
        {
            current = current + " " + next;
        }
        else
        {
            merged.Add(current);
            current = next;
        }
    }

    if (!string.IsNullOrWhiteSpace(current))
        merged.Add(current);

    return merged;
}

    }
}