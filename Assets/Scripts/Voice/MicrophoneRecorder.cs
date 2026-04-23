using System;
using UnityEngine;

namespace MVP.Conversation
{
    public class MicrophoneRecorder : MonoBehaviour
    {
        [Header("Microphone")]
        [SerializeField] private string selectedDevice;
        [SerializeField] private int sampleRate = 48000;
        [SerializeField] private int maxLengthSeconds = 10;
        [SerializeField] private bool loopRecording = false;
        [SerializeField] private bool logDiagnostics = true;
        [SerializeField] private float minRecordedSeconds = 0.08f;

        public bool IsRecording { get; private set; }
        public AudioClip CurrentClip { get; private set; }
        public string ActiveDeviceName => selectedDevice;

        public bool StartRecording()
        {
            if (IsRecording)
                return true;

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogError("[MicrophoneRecorder] No hay micrófonos disponibles en el dispositivo.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(selectedDevice))
                selectedDevice = Microphone.devices[0];

            Microphone.GetDeviceCaps(selectedDevice, out int minFreq, out int maxFreq);
            int requestedFrequency = sampleRate;

            if (!(minFreq == 0 && maxFreq == 0))
            {
                if (maxFreq > 0 && requestedFrequency > maxFreq)
                    requestedFrequency = maxFreq;
                if (minFreq > 0 && requestedFrequency < minFreq)
                    requestedFrequency = minFreq;
            }

            if (logDiagnostics)
            {
                Debug.Log("[MicrophoneRecorder] Dispositivos detectados: " + string.Join(", ", Microphone.devices));
                Debug.Log($"[MicrophoneRecorder] Device caps min={minFreq} max={maxFreq}. Requested={sampleRate}, final={requestedFrequency}");
                Debug.Log("[MicrophoneRecorder] Usando dispositivo: '" + selectedDevice + "'");
            }

            CurrentClip = Microphone.Start(selectedDevice, loopRecording, maxLengthSeconds, requestedFrequency);
            IsRecording = CurrentClip != null;

            if (logDiagnostics)
            {
                Debug.Log("[MicrophoneRecorder] Microphone.Start -> clip null? " + (CurrentClip == null));
                if (CurrentClip != null)
                {
                    Debug.Log("[MicrophoneRecorder] Clip created. lengthSamples=" + CurrentClip.samples +
                              " channels=" + CurrentClip.channels +
                              " frequency=" + CurrentClip.frequency +
                              " length=" + CurrentClip.length);
                }
            }

            return IsRecording;
        }

        public AudioClip StopRecording()
        {
            if (!IsRecording)
                return null;

            if (CurrentClip == null)
            {
                Microphone.End(selectedDevice);
                IsRecording = false;
                Debug.LogError("[MicrophoneRecorder] CurrentClip es null al detener grabación.");
                return null;
            }

            int rawPosition = Microphone.GetPosition(selectedDevice);
            int clipSamples = CurrentClip.samples;
            int channels = Mathf.Max(1, CurrentClip.channels);
            int safePosition = Mathf.Clamp(rawPosition, 0, clipSamples);

            if (logDiagnostics)
                Debug.Log($"[MicrophoneRecorder] rawPosition={rawPosition}, safePosition={safePosition}, clipSamples={clipSamples}, channels={channels}");

            Microphone.End(selectedDevice);
            IsRecording = false;

            int minSamplesNeeded = Mathf.Max(1, Mathf.CeilToInt(CurrentClip.frequency * minRecordedSeconds));
            if (safePosition < minSamplesNeeded)
            {
                if (logDiagnostics)
                    Debug.LogWarning($"[MicrophoneRecorder] Posición baja del micrófono. safePosition={safePosition}, minSamplesNeeded={minSamplesNeeded}. Se usará el clip completo como fallback.");

                safePosition = clipSamples;
            }

            int sourceSampleCount = clipSamples * channels;
            float[] fullData = new float[sourceSampleCount];
            CurrentClip.GetData(fullData, 0);

            int trimmedSampleCount = safePosition * channels;
            if (trimmedSampleCount <= 0 || trimmedSampleCount > fullData.Length)
            {
                Debug.LogError($"[MicrophoneRecorder] trimmedSampleCount inválido. trimmed={trimmedSampleCount}, full={fullData.Length}");
                return null;
            }

            float[] trimmedData = new float[trimmedSampleCount];
            Array.Copy(fullData, trimmedData, trimmedSampleCount);

            AudioClip trimmedClip = AudioClip.Create(
                CurrentClip.name + "_trimmed",
                safePosition,
                channels,
                CurrentClip.frequency,
                false);

            trimmedClip.SetData(trimmedData, 0);

            if (logDiagnostics)
            {
                Debug.Log("[MicrophoneRecorder] Trimmed clip creado correctamente. samples=" + trimmedClip.samples +
                          " channels=" + trimmedClip.channels +
                          " frequency=" + trimmedClip.frequency +
                          " length=" + trimmedClip.length);
            }

            return trimmedClip;
        }
    }
}