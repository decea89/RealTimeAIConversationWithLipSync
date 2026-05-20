using System;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace MVP.Conversation.Editor
{
    [InitializeOnLoad]
    internal static class LipSyncTelemetryEditor
    {
        // Adds menu item Tools/LipSync/Flush Telemetry (shortcut: Ctrl/Cmd+Shift+F)
        [MenuItem("Tools/LipSync/Flush Telemetry %#f")]
        private static void FlushTelemetryMenu()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("LipSyncTelemetry.Flush: enter Play mode to flush runtime telemetry.");
                return;
            }

            try
            {
                MVP.Conversation.LipSyncTelemetry.Flush();
                Debug.Log($"LipSyncTelemetry flushed to: {Application.persistentDataPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError("LipSyncTelemetry.Flush failed: " + ex.Message);
            }
        }
    }
}
#endif
