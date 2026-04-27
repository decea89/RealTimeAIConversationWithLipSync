using System;
using UnityEngine;

namespace MVP.Conversation
{
    public class StreamingAudioBuffer
    {
        private readonly float[] buffer;
        private readonly int capacity;
        private readonly object locker = new object();

        private int readPos;
        private int writePos;
        private int availableSamples;
        private long totalSamplesWritten;
        private long totalSamplesRead;

        public int AvailableSamples
        {
            get
            {
                lock (locker)
                    return availableSamples;
            }
        }

        public long TotalSamplesWritten
        {
            get
            {
                lock (locker)
                    return totalSamplesWritten;
            }
        }

        public long TotalSamplesRead
        {
            get
            {
                lock (locker)
                    return totalSamplesRead;
            }
        }

        public StreamingAudioBuffer(int capacitySamples)
        {
            capacity = Mathf.Max(4096, capacitySamples);
            buffer = new float[capacity];
        }

        public void Clear()
        {
            lock (locker)
            {
                readPos = 0;
                writePos = 0;
                availableSamples = 0;
                totalSamplesWritten = 0;
                totalSamplesRead = 0;
                Array.Clear(buffer, 0, buffer.Length);
            }
        }

        public void Write(float[] samples, int sampleCount)
        {
            if (samples == null || sampleCount <= 0)
                return;

            lock (locker)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    buffer[writePos] = samples[i];
                    writePos = (writePos + 1) % capacity;

                    if (availableSamples < capacity)
                    {
                        availableSamples++;
                    }
                    else
                    {
                        readPos = (readPos + 1) % capacity;
                    }

                    totalSamplesWritten++;
                }
            }
        }

        public void Read(float[] destination)
        {
            if (destination == null)
                return;

            lock (locker)
            {
                for (int i = 0; i < destination.Length; i++)
                {
                    if (availableSamples > 0)
                    {
                        destination[i] = buffer[readPos];
                        readPos = (readPos + 1) % capacity;
                        availableSamples--;
                        totalSamplesRead++;
                    }
                    else
                    {
                        destination[i] = 0f;
                    }
                }
            }
        }
    }
}