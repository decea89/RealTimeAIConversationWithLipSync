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
        [SerializeField] private MonoBehaviour innerTtsServiceBehaviour;

        [Header("Segmentation")]
        [SerializeField] private int maxSegmentChars = 140;
        [SerializeField] private bool logSegments = true;

        [Header("Playback")]
        [SerializeField] private float transitionPaddingSeconds = 0.015f;
        [SerializeField] private float maxWaitForNextSegmentSeconds = 2.5f;

        private ITTSService innerTtsService;

        private void Awake()
        {
            if (innerTtsServiceBehaviour != null)
                innerTtsService = innerTtsServiceBehaviour as ITTSService;

            if (innerTtsService == null)
                Debug.LogError("[SegmentedBufferedTTSClient] innerTtsServiceBehaviour no implementa ITTSService.");
        }

        public IEnumerator RequestSpeech(string text, Action<AudioClip, string> onComplete)
        {
            onComplete?.Invoke(null, "SegmentedBufferedTTSClient: esta versión está pensada para IStreamingTTSService, no devuelve clip fusionado.");
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

            if (targetAudioSource == null)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: targetAudioSource nulo.");
                yield break;
            }

            if (innerTtsService == null)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: innerTtsService no configurado.");
                yield break;
            }

            List<string> segments = SplitIntoSegments(text, maxSegmentChars);
            if (segments == null || segments.Count == 0)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: no se generaron segmentos válidos.");
                yield break;
            }

            if (logSegments)
            {
                Debug.Log($"[SegmentedBufferedTTSClient] Segmentos={segments.Count}");
                for (int i = 0; i < segments.Count; i++)
                    Debug.Log($"[SegmentedBufferedTTSClient] [{i + 1}/{segments.Count}] {segments[i]}");
            }

            bool playbackStarted = false;

            SegmentRequest current = new SegmentRequest(0, segments[0]);
            yield return StartCoroutine(RequestSegmentCoroutine(current));

            if (!string.IsNullOrEmpty(current.error))
            {
                onError?.Invoke(current.error);
                yield break;
            }

            if (current.clip == null)
            {
                onError?.Invoke("SegmentedBufferedTTSClient: el primer segmento devolvió clip nulo.");
                yield break;
            }

            SegmentRequest next = null;
            Coroutine nextRequestCoroutine = null;

            for (int i = 0; i < segments.Count; i++)
            {
                current.index = i;
                current.text = segments[i];

                if (current.clip == null)
                {
                    onError?.Invoke($"SegmentedBufferedTTSClient: clip nulo en segmento {i + 1}.");
                    yield break;
                }

                if (i + 1 < segments.Count)
                {
                    next = new SegmentRequest(i + 1, segments[i + 1]);
                    nextRequestCoroutine = StartCoroutine(RequestSegmentCoroutine(next));
                }
                else
                {
                    next = null;
                    nextRequestCoroutine = null;
                }

                targetAudioSource.Stop();
                targetAudioSource.clip = current.clip;
                targetAudioSource.Play();

                if (!playbackStarted)
                {
                    playbackStarted = true;
                    onPlaybackStarted?.Invoke();
                }

                float waitTime = Mathf.Max(0f, current.clip.length - transitionPaddingSeconds);
                float playEnd = Time.time + waitTime;

                while (Time.time < playEnd)
                    yield return null;

                if (next != null)
                {
                    float waitDeadline = Time.time + maxWaitForNextSegmentSeconds;

                    while (!next.isDone && Time.time < waitDeadline)
                        yield return null;

                    if (!next.isDone)
                    {
                        if (nextRequestCoroutine != null)
                            StopCoroutine(nextRequestCoroutine);

                        onError?.Invoke($"SegmentedBufferedTTSClient: timeout esperando el segmento {next.index + 1}.");
                        yield break;
                    }

                    if (!string.IsNullOrEmpty(next.error))
                    {
                        onError?.Invoke(next.error);
                        yield break;
                    }

                    if (next.clip == null)
                    {
                        onError?.Invoke($"SegmentedBufferedTTSClient: clip nulo en segmento {next.index + 1}.");
                        yield break;
                    }
                }

                current = next;
            }

            while (targetAudioSource.isPlaying)
                yield return null;

            onCompleted?.Invoke();
        }

        private IEnumerator RequestSegmentCoroutine(SegmentRequest request)
        {
            if (request == null)
                yield break;

            AudioClip segmentClip = null;
            string segmentError = null;

            yield return innerTtsService.RequestSpeech(request.text, (clip, err) =>
            {
                segmentClip = clip;
                segmentError = err;
            });

            request.clip = segmentClip;
            request.error = segmentError;
            request.isDone = true;
        }

        private List<string> SplitIntoSegments(string text, int maxChars)
        {
            string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            normalized = Regex.Replace(normalized, @"\s+", " ");

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
                string candidate = string.IsNullOrEmpty(current)
                    ? sentence
                    : current + " " + sentence;

                if (candidate.Length <= maxChars)
                {
                    current = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(current))
                    segments.Add(current.Trim());

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

        [Serializable]
        private class SegmentRequest
        {
            public int index;
            public string text;
            public AudioClip clip;
            public string error;
            public bool isDone;

            public SegmentRequest(int index, string text)
            {
                this.index = index;
                this.text = text;
                clip = null;
                error = null;
                isDone = false;
            }
        }
    }
}