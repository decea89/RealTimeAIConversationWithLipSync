using UnityEngine;

namespace MVP.Conversation
{
    public static class AudioTrimmingUtility
    {
        // levelThreshold: amplitud mínima para considerar que hay voz (0-1)
        // minSegmentSeconds: si la ventana con voz es menor, no se recorta
        // extraPaddingSeconds: margen extra antes y después de la ventana con voz
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

            // Buscar primer sample “con voz”
            for (int i = 0; i < totalSamples; i++)
            {
                if (Mathf.Abs(data[i]) >= levelThreshold)
                {
                    firstSample = i;
                    break;
                }
            }

            if (firstSample == -1)
                return null; // todo silencio

            // Buscar último sample “con voz”
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
                return null; // segmento muy corto, no recortamos

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