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
        private int available;

        public int AvailableSamples
        {
            get
            {
                lock (locker)
                    return available;
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
                available = 0;
                Array.Clear(buffer, 0, buffer.Length);
            }
        }

        public void Write(float[] samples, int count)
        {
            if (samples == null || count <= 0)
                return;

            lock (locker)
            {
                for (int i = 0; i < count; i++)
                {
                    buffer[writePos] = samples[i];
                    writePos = (writePos + 1) % capacity;

                    if (available < capacity)
                    {
                        available++;
                    }
                    else
                    {
                        readPos = (readPos + 1) % capacity;
                    }
                }
            }
        }

        public void Read(float[] dst)
        {
            if (dst == null)
                return;

            lock (locker)
            {
                for (int i = 0; i < dst.Length; i++)
                {
                    if (available > 0)
                    {
                        dst[i] = buffer[readPos];
                        readPos = (readPos + 1) % capacity;
                        available--;
                    }
                    else
                    {
                        dst[i] = 0f;
                    }
                }
            }
        }
    }
}