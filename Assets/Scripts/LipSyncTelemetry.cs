using System;
using System.Collections.Concurrent;
// using System.Diagnostics; // avoid ambiguity with UnityEngine.Debug
using System.IO;
using System.Text;
using UnityEngine;

namespace MVP.Conversation
{
    public static class LipSyncTelemetry
    {
        public enum EventId : byte
        {
            RequestStarted = 1,
            FirstChunkReceived = 2,
            ChunkReceived = 3,
            AudioWrite = 4,
            AudioStarted = 5,
            LipSyncProcess = 6,
            ProducerCompleted = 7,
            PlaybackFinished = 8,
            Error = 9
            ,
            BufferUnderrun = 10,
            BufferCrossfadeApplied = 11
        }

        private struct Entry
        {
            public long ms;
            public byte evt;
            public int turnId;
            public int genId;
            public int value;
        }

        private static readonly ConcurrentQueue<Entry> queue = new ConcurrentQueue<Entry>();
        private static readonly System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        private static string filePath;
        private static bool initialized = false;

        public static void Init()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                string dir = Application.persistentDataPath;
                Directory.CreateDirectory(dir);
                filePath = Path.Combine(dir, "lipsync_telemetry.csv");
                File.WriteAllText(filePath, "ms,event,turnId,genId,value\n", Encoding.UTF8);
                // Create a hidden GameObject to flush data each frame
                var go = new GameObject("LipSyncTelemetryWriter");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.hideFlags = HideFlags.HideAndDontSave;
                go.AddComponent<TelemetryWriter>();
            }
            catch (Exception e)
            {
                Debug.LogWarning("LipSyncTelemetry.Init failed: " + e.Message);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            Init();
        }

        public static void Enqueue(EventId evt, int turnId = -1, int genId = -1, int value = 0)
        {
            Entry e = new Entry
            {
                ms = sw.ElapsedMilliseconds,
                evt = (byte)evt,
                turnId = turnId,
                genId = genId,
                value = value
            };
            queue.Enqueue(e);
        }

        // Synchronously flush any queued entries to disk. Safe to call from UI or editor.
        public static void Flush()
        {
            if (!initialized) Init();

            try
            {
                var sb = new StringBuilder(1024);
                while (queue.TryDequeue(out Entry e))
                {
                    sb.Append(e.ms).Append(',')
                      .Append(e.evt).Append(',')
                      .Append(e.turnId).Append(',')
                      .Append(e.genId).Append(',')
                      .Append(e.value).Append('\n');
                }

                if (sb.Length > 0)
                    File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("LipSyncTelemetry.Flush failed: " + ex.Message);
            }
        }

        private class TelemetryWriter : MonoBehaviour
        {
            private StringBuilder sb = new StringBuilder(1024);
            private float flushInterval = 0.5f;
            private float since = 0f;

            private void Update()
            {
                since += Time.unscaledDeltaTime;
                if (since < flushInterval) return;
                since = 0f;

                if (queue.IsEmpty) return;

                try
                {
                    while (queue.TryDequeue(out Entry e))
                    {
                        sb.Append(e.ms).Append(',')
                          .Append(e.evt).Append(',')
                          .Append(e.turnId).Append(',')
                          .Append(e.genId).Append(',')
                          .Append(e.value).Append('\n');
                    }

                    File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
                    sb.Length = 0;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("LipSyncTelemetry write failed: " + ex.Message);
                }
            }

            private void OnApplicationQuit()
            {
                // Flush any remaining entries synchronously on quit to avoid losing telemetry
                try
                {
                    while (queue.TryDequeue(out Entry e))
                    {
                        sb.Append(e.ms).Append(',')
                          .Append(e.evt).Append(',')
                          .Append(e.turnId).Append(',')
                          .Append(e.genId).Append(',')
                          .Append(e.value).Append('\n');
                    }

                    if (sb.Length > 0)
                        File.AppendAllText(filePath, sb.ToString(), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("LipSyncTelemetry flush on quit failed: " + ex.Message);
                }
            }
        }
    }
}
