using System;
using System.IO;
using System.Text;
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

            writer.Write(Encoding.UTF8.GetBytes("RIFF"));
            writer.Write(chunkSize);
            writer.Write(Encoding.UTF8.GetBytes("WAVE"));
            writer.Write(Encoding.UTF8.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)clip.channels);
            writer.Write(clip.frequency);
            writer.Write(byteRate);
            writer.Write((short)(clip.channels * 2));
            writer.Write((short)16);
            writer.Write(Encoding.UTF8.GetBytes("data"));
            writer.Write(subChunk2Size);
            writer.Write(bytesData);

            writer.Flush();
            return stream.ToArray();
        }

        public static AudioClip ToAudioClip(byte[] wavBytes, string clipName = "wav")
        {
            if (wavBytes == null || wavBytes.Length < 44)
                return null;

            using var stream = new MemoryStream(wavBytes);
            using var reader = new BinaryReader(stream);

            string riff = Encoding.UTF8.GetString(reader.ReadBytes(4));
            if (riff != "RIFF")
                return null;

            reader.ReadInt32();

            string wave = Encoding.UTF8.GetString(reader.ReadBytes(4));
            if (wave != "WAVE")
                return null;

            short audioFormat = 0;
            short channels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            byte[] dataChunk = null;

            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
            {
                string chunkId = Encoding.UTF8.GetString(reader.ReadBytes(4));
                int chunkSize = reader.ReadInt32();

                if (chunkId == "fmt ")
                {
                    audioFormat = reader.ReadInt16();
                    channels = reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    reader.ReadInt32();
                    reader.ReadInt16();
                    bitsPerSample = reader.ReadInt16();

                    int remaining = chunkSize - 16;
                    if (remaining > 0)
                        reader.ReadBytes(remaining);
                }
                else if (chunkId == "data")
                {
                    dataChunk = reader.ReadBytes(chunkSize);
                }
                else
                {
                    reader.ReadBytes(chunkSize);
                }

                if ((chunkSize & 1) == 1 && reader.BaseStream.Position < reader.BaseStream.Length)
                    reader.ReadByte();
            }

            if (audioFormat != 1)
            {
                Debug.LogError($"[WavUtility] Solo se soporta WAV PCM sin compresión. audioFormat={audioFormat}");
                return null;
            }

            if (bitsPerSample != 16)
            {
                Debug.LogError($"[WavUtility] Solo se soporta WAV PCM 16-bit. bitsPerSample={bitsPerSample}");
                return null;
            }

            if (channels <= 0 || sampleRate <= 0 || dataChunk == null || dataChunk.Length == 0)
                return null;

            int sampleCount = dataChunk.Length / 2;
            float[] samples = new float[sampleCount];

            int sampleIndex = 0;
            for (int i = 0; i + 1 < dataChunk.Length; i += 2)
            {
                short pcm = BitConverter.ToInt16(dataChunk, i);
                samples[sampleIndex++] = pcm / 32768f;
            }

            int frameCount = sampleCount / channels;
            if (frameCount <= 0)
                return null;

            AudioClip clip = AudioClip.Create(clipName, frameCount, channels, sampleRate, false);
            clip.SetData(samples, 0);

            Debug.Log($"[WavUtility] WAV cargado. channels={channels}, sampleRate={sampleRate}, bits={bitsPerSample}, frames={frameCount}, length={clip.length:F2}s");
            return clip;
        }
    }
}