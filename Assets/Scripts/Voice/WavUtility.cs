using System;
using System.IO;
using UnityEngine;

namespace MVP.Conversation
{
    public static class WavUtility
    {
        public static byte[] FromAudioClip(AudioClip clip)
        {
            if (clip == null)
                return null;

            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            short[] intData = new short[samples.Length];
            byte[] bytesData = new byte[samples.Length * 2];

            const float rescaleFactor = 32767f;
            for (int i = 0; i < samples.Length; i++)
            {
                float v = Mathf.Clamp(samples[i] * rescaleFactor, short.MinValue, short.MaxValue);
                intData[i] = (short)v;
                byte[] byteArr = BitConverter.GetBytes(intData[i]);
                byteArr.CopyTo(bytesData, i * 2);
            }

            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            int byteRate = clip.frequency * clip.channels * 2;
            int subChunk2Size = bytesData.Length;
            int chunkSize = 36 + subChunk2Size;

            writer.Write(System.Text.Encoding.UTF8.GetBytes("RIFF"));
            writer.Write(chunkSize);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("WAVE"));
            writer.Write(System.Text.Encoding.UTF8.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(byteRate);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write(System.Text.Encoding.UTF8.GetBytes("data"));
            writer.Write(subChunk2Size);
            writer.Write(bytesData);

            writer.Flush();
            return stream.ToArray();
        }
    }
}