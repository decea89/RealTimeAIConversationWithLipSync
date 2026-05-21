using System;
using UnityEngine;

namespace MVP.Conversation
{
    internal static class ApiKeyProvider
    {
        public static string Resolve(string serializedValue, string environmentVariableName, string serviceName)
        {
            if (HasUsableValue(serializedValue))
                return serializedValue.Trim();

            string envValue = Environment.GetEnvironmentVariable(environmentVariableName);
            if (HasUsableValue(envValue))
                return envValue.Trim();

            Debug.LogWarning($"[{serviceName}] API key no configurada. Define {environmentVariableName} en tu entorno local.");
            return string.Empty;
        }

        public static bool HasUsableValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return !value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase);
        }
    }
}