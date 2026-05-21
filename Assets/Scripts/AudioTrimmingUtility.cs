using UnityEngine;

namespace MVP.Conversation
{
    public static class AudioTrimmingUtility
    {
        // levelThreshold: minimum amplitude to consider a sample voiced (0-1)
        // minSegmentSeconds: if the voiced window is shorter than this, do not trim
        // extraPaddingSeconds: extra padding before and after the voiced window
        public static AudioClip TrimSilence(
            AudioClip source,
            float levelThreshold = 0.01f,
            float minSegmentSeconds = 0.08f,
            float extraPaddingSeconds = 0.12f)
        {
            if (source == null)
                return null;

            int channels = source.channels;
            int frequency = source.frequency;
            int totalSamples = source.samples * channels;

            float[] data = new float[totalSamples];
            source.GetData(data, 0);

            int firstSample = -1;
            int lastSample = -1;

            // Find the first voiced sample
            for (int i = 0; i < totalSamples; i++)
            {
                if (Mathf.Abs(data[i]) >= levelThreshold)
                {
                    firstSample = i;
                    break;
                }
            }

            if (firstSample == -1)
                return null; // all silence

            // Find the last voiced sample
            for (int i = totalSamples - 1; i >= 0; i--)
            {
                if (Mathf.Abs(data[i]) >= levelThreshold)
                {
                    lastSample = i;
                    break;
                }
            }

            if (lastSample <= firstSample)
                return null;

            int samplesPerSecond = frequency * channels;
            int minSegmentSamples = Mathf.CeilToInt(minSegmentSeconds * samplesPerSecond);
            int segmentLength = lastSample - firstSample;

            if (segmentLength < minSegmentSamples)
                return null; // segment too short, do not trim

            int paddingSamples = Mathf.CeilToInt(extraPaddingSeconds * samplesPerSecond);
            int trimmedStart = Mathf.Max(0, firstSample - paddingSamples);
            int trimmedEnd = Mathf.Min(totalSamples - 1, lastSample + paddingSamples);
            int trimmedLength = trimmedEnd - trimmedStart + 1;

            float[] trimmedData = new float[trimmedLength];
            System.Array.Copy(data, trimmedStart, trimmedData, 0, trimmedLength);

            int trimmedSamplesPerChannel = trimmedLength / channels;
            AudioClip trimmedClip = AudioClip.Create(
                source.name + "_trimmed_silence",
                trimmedSamplesPerChannel,
                channels,
                frequency,
                false);

            trimmedClip.SetData(trimmedData, 0);

            return trimmedClip;
        }
    }
}