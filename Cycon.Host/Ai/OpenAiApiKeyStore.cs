using System;
using System.IO;

namespace Cycon.Host.Ai;

public static class OpenAiApiKeyStore
{
    private const string EnvVar = "OPENAI_API_KEY";

    public static string? TryGetApiKey()
    {
        var env = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        return TryLoadFromDisk(out var disk) ? disk : null;
    }

    public static bool TryLoadFromDisk(out string apiKey)
    {
        apiKey = string.Empty;
        try
        {
            var path = GetKeyFilePath();
            if (!File.Exists(path))
            {
                return false;
            }

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            apiKey = text.Trim();
            return apiKey.Length > 0;
        }
        catch
        {
            apiKey = string.Empty;
            return false;
        }
    }

    public static bool TrySaveToDisk(string apiKey, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            error = "API key is empty.";
            return false;
        }

        try
        {
            var trimmed = apiKey.Trim();
            var path = GetKeyFilePath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, trimmed);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string GetKeyFilePath()
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        return Path.Combine(baseDir, "Cycon", "openai_api_key.txt");
    }
}

