using System;
using System.Collections;
using UnityEngine;

namespace MVP.Conversation
{
    public class BufferedTtsRealtimeAdapter : MonoBehaviour, IRealtimeTTSService
    {
        [SerializeField] private MonoBehaviour innerTtsServiceBehaviour; // ITTSService
        [SerializeField] private AudioSource targetAudioSource;
        [SerializeField] [Min(5f)] private float maxPlaybackSeconds = 60f;

        private ITTSService innerTtsService;
        private BufferedHandle currentHandle;
        private Coroutine currentRoutine;
        private int generationId;

        private void Awake()
        {
            innerTtsService = innerTtsServiceBehaviour as ITTSService;

            if (innerTtsService == null)
                Debug.LogError("[BufferedTtsRealtimeAdapter] innerTtsServiceBehaviour no implementa ITTSService.");

            if (targetAudioSource == null)
                Debug.LogError("[BufferedTtsRealtimeAdapter] targetAudioSource no asignado.");
        }

        public IRealtimeTTSHandle StartStream(
            string text,
            int turnId,
            Action onAudioBegan,
            Action<string> onError)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onError?.Invoke("BufferedTtsRealtimeAdapter: text vacío.");
                return null;
            }

            if (innerTtsService == null || targetAudioSource == null)
            {
                onError?.Invoke("BufferedTtsRealtimeAdapter: dependencias no configuradas.");
                return null;
            }

            CancelAll();

            generationId++;
            currentHandle = new BufferedHandle(turnId, generationId);
            currentRoutine = StartCoroutine(RunBuffered(text, currentHandle, onAudioBegan, onError));
            return currentHandle;
        }

        public void CancelAll()
        {
            generationId++;

            if (currentRoutine != null)
            {
                StopCoroutine(currentRoutine);
                currentRoutine = null;
            }

            if (targetAudioSource != null)
            {
                targetAudioSource.Stop();
                targetAudioSource.clip = null;
            }

            currentHandle?.MarkCanceled();
            currentHandle = null;
        }

        private IEnumerator RunBuffered(
            string text,
            BufferedHandle handle,
            Action onAudioBegan,
            Action<string> onError)
        {
            AudioClip clip = null;
            string error = null;

            yield return innerTtsService.RequestSpeech(text, (c, e) =>
            {
                clip = c;
                error = e;
            });

            if (handle == null || handle.IsCanceled || handle.GenerationId != generationId)
                yield break;

            if (!string.IsNullOrEmpty(error))
            {
                onError?.Invoke(error);
                handle.MarkCompleted();
                currentRoutine = null;
                yield break;
            }

            if (clip == null)
            {
                onError?.Invoke("BufferedTtsRealtimeAdapter: clip nulo.");
                handle.MarkCompleted();
                currentRoutine = null;
                yield break;
            }

            targetAudioSource.Stop();
            targetAudioSource.loop = false;
            targetAudioSource.clip = clip;
            targetAudioSource.volume = 1f;
            targetAudioSource.mute = false;
            targetAudioSource.spatialBlend = 0f;
            targetAudioSource.Play();

            Debug.Log($"IsPlaying after Play: {targetAudioSource.isPlaying}");
            onAudioBegan?.Invoke();

            float playbackDeadline = Time.realtimeSinceStartup + Mathf.Max(5f, maxPlaybackSeconds);
            while (targetAudioSource != null &&
                   targetAudioSource.isPlaying &&
                   !handle.IsCanceled &&
                   handle.GenerationId == generationId)
            {
                if (Time.realtimeSinceStartup >= playbackDeadline)
                {
                    Debug.LogWarning("[BufferedTtsRealtimeAdapter] Timeout de reproducción alcanzado; deteniendo AudioSource.");
                    targetAudioSource.Stop();
                    break;
                }

                yield return null;
            }

            handle.MarkCompleted();
            currentRoutine = null;
        }

        private class BufferedHandle : IRealtimeTTSHandle
        {
            private bool canceled;
            private bool completed;

            public int TurnId { get; }
            public int GenerationId { get; }
            public bool IsCompleted => completed || canceled;
            public bool IsCanceled => canceled;

            public BufferedHandle(int turnId, int generationId)
            {
                TurnId = turnId;
                GenerationId = generationId;
            }

            public void Cancel()
            {
                canceled = true;
            }

            public void MarkCanceled()
            {
                canceled = true;
            }

            public void MarkCompleted()
            {
                completed = true;
            }
        }
    }
}