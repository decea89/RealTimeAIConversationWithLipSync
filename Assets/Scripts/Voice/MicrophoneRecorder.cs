using System;
using UnityEngine;

namespace MVP.Conversation
{
    public class MicrophoneRecorder : MonoBehaviour
    {
        [Header("Microphone")]
        [SerializeField]
        [Tooltip("Selected microphone. If empty, uses the default device.")]
        private string selectedDevice;
        
        [SerializeField]
        [Range(16000, 48000)]
        [Tooltip("Recording frequency. Higher values mean better quality but more data. 48000 is CD quality.")]
        private int sampleRate = 48000;
        
        [SerializeField]
        [Range(5, 60)]
        [Tooltip("Maximum recording duration in seconds. Useful to avoid recording indefinitely by accident.")]
        private int maxLengthSeconds = 10;
        
        [SerializeField]
        [Tooltip("Loop recording. Usually OFF for push-to-talk.")]
        private bool loopRecording = false;
        
        [SerializeField]
        [Tooltip("Show recording logs in the console. Useful for debugging.")]
        private bool logDiagnostics = true;
        
        [SerializeField]
        [Range(0.01f, 1.0f)]
        [Tooltip("Minimum duration required to consider a recording valid. Helps avoid accidental triggers.")]
        private float minRecordedSeconds = 0.08f;

        public bool IsRecording { get; private set; }
        public AudioClip CurrentClip { get; private set; }
        public string ActiveDeviceName => selectedDevice;

        public bool StartRecording()
        {
            if (IsRecording)
                return true;

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                Debug.LogError("[MicrophoneRecorder] No microphones are available on the device.");
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
                Debug.Log("[MicrophoneRecorder] Detected devices: " + string.Join(", ", Microphone.devices));
                Debug.Log($"[MicrophoneRecorder] Device caps min={minFreq} max={maxFreq}. Requested={sampleRate}, final={requestedFrequency}");
                Debug.Log("[MicrophoneRecorder] Using device: '" + selectedDevice + "'");
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
                Debug.LogError("[MicrophoneRecorder] CurrentClip is null while stopping recording.");
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
                    Debug.LogWarning($"[MicrophoneRecorder] Low microphone position. safePosition={safePosition}, minSamplesNeeded={minSamplesNeeded}. The full clip will be used as a fallback.");

                safePosition = clipSamples;
            }

            int sourceSampleCount = clipSamples * channels;
            float[] fullData = new float[sourceSampleCount];
            CurrentClip.GetData(fullData, 0);

            int trimmedSampleCount = safePosition * channels;
            if (trimmedSampleCount <= 0 || trimmedSampleCount > fullData.Length)
            {
                Debug.LogError($"[MicrophoneRecorder] Invalid trimmedSampleCount. trimmed={trimmedSampleCount}, full={fullData.Length}");
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
                Debug.Log("[MicrophoneRecorder] Trimmed clip created successfully. samples=" + trimmedClip.samples +
                          " channels=" + trimmedClip.channels +
                          " frequency=" + trimmedClip.frequency +
                          " length=" + trimmedClip.length);
            }

            return trimmedClip;
        }
    }
}