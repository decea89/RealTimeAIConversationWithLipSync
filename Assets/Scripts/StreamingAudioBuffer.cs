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
        private float lastSample = 0f;
        // Crossfade duration in milliseconds to smooth transitions after underruns
        private int underrunCrossfadeMs = 12;

        public int AvailableSamples
        {
            get
            {
                lock (locker)
                    return availableSamples;
            }
        }

        public int FreeSamples
        {
            get
            {
                lock (locker)
                    return capacity - availableSamples;
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
                lastSample = 0f;
            }
        }

        public int WriteSome(float[] samples, int offset, int sampleCount)
        {
            if (samples == null || sampleCount <= 0 || offset < 0 || offset >= samples.Length)
                return 0;

            lock (locker)
            {
                int writable = Mathf.Min(sampleCount, capacity - availableSamples);

                // If buffer was empty and we have a lastSample, apply a short crossfade
                int rampSamples = 0;
                bool crossfadeApplied = false;
                if (availableSamples == 0 && lastSample != 0f && writable > 0)
                {
                    int sr = AudioSettings.outputSampleRate;
                    rampSamples = Mathf.CeilToInt(sr * (underrunCrossfadeMs / 1000f));
                    rampSamples = Mathf.Min(rampSamples, writable);
                }

                for (int i = 0; i < writable; i++)
                {
                    float val = 0f;
                    if (i < rampSamples)
                    {
                        float f = (float)(i + 1) / (float)rampSamples; // ramp from lastSample -> sample
                        float src = samples[offset + i];
                        val = lastSample * (1f - f) + src * f;
                    }
                    if (!crossfadeApplied && rampSamples > 0 && i == rampSamples - 1)
                        crossfadeApplied = true;
                    else
                    {
                        val = samples[offset + i];
                    }

                    buffer[writePos] = val;
                    writePos = (writePos + 1) % capacity;
                    availableSamples++;
                    totalSamplesWritten++;
                    lastSample = val;
                }

                if (crossfadeApplied)
                {
                    MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.BufferCrossfadeApplied, -1, -1, rampSamples);
                }

                return writable;
            }
        }

        public void Read(float[] destination)
        {
            if (destination == null)
                return;

            lock (locker)
            {
                bool underrunOccurred = false;
                for (int i = 0; i < destination.Length; i++)
                {
                    if (availableSamples > 0)
                    {
                        destination[i] = buffer[readPos];
                        readPos = (readPos + 1) % capacity;
                        availableSamples--;
                        totalSamplesRead++;
                        lastSample = destination[i];
                    }
                    else
                    {
                        // Buffer underrun: repeat last known sample to smooth gaps
                        destination[i] = lastSample;
                        underrunOccurred = true;
                    }
                }

                if (underrunOccurred)
                {
                    MVP.Conversation.LipSyncTelemetry.Enqueue(MVP.Conversation.LipSyncTelemetry.EventId.BufferUnderrun, -1, -1, destination.Length);
                }
            }
        }
    }
}