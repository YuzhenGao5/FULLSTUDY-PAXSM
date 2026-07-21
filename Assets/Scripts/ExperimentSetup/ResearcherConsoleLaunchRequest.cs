using System;
using System.Globalization;
using System.IO;
using UnityEngine;

public static class ResearcherConsoleLaunchRequest
{
    public const string SchemaVersion = "PAXSM_ResearcherConsole_LaunchRequest_v1";

    [Serializable]
    public sealed class Payload
    {
        public string schemaVersion = "";
        public string requestId = "";
        public string projectRoot = "";
        public string participantId = "";
        public int sessionNumber = 1;
        public string sceneId = "";
        public string sceneName = "";
        public string dataNamespace = "";
        public string outputRoot = "";
        public bool autoStart = true;
        public string requestedAtUtc = "";
    }

    public static string RequestPath
    {
        get
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "PAXSM", "ResearcherConsole", "launch_request.json");
        }
    }

    public static bool TryPeek(out Payload payload)
    {
        payload = null;
#if !UNITY_EDITOR_WIN && !UNITY_STANDALONE_WIN
        return false;
#else
        try
        {
            string path = RequestPath;
            if (!File.Exists(path))
                return false;
            payload = JsonUtility.FromJson<Payload>(File.ReadAllText(path));
            return IsValid(payload);
        }
        catch
        {
            payload = null;
            return false;
        }
#endif
    }

    public static bool TryConsume(out Payload payload)
    {
        if (!TryPeek(out payload))
            return false;

        try
        {
            File.Delete(RequestPath);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ResearcherConsole] Could not consume launch request: {exception.Message}");
            payload = null;
            return false;
        }

        return true;
    }

    public static bool MatchesCurrentProject(Payload payload)
    {
        if (payload == null || string.IsNullOrWhiteSpace(payload.projectRoot))
            return false;
        try
        {
            string currentRoot = Directory.GetParent(Application.dataPath)?.FullName ?? "";
            return string.Equals(
                Path.GetFullPath(currentRoot).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(payload.projectRoot).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static bool IsValid(Payload payload)
    {
        if (payload == null ||
            !string.Equals(payload.schemaVersion, SchemaVersion, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(payload.participantId) ||
            string.IsNullOrWhiteSpace(payload.sceneName) ||
            string.IsNullOrWhiteSpace(payload.outputRoot))
            return false;

        if (!DateTimeOffset.TryParse(
                payload.requestedAtUtc,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset requestedAt))
            return false;
        TimeSpan age = DateTimeOffset.UtcNow - requestedAt.ToUniversalTime();
        return age >= TimeSpan.FromMinutes(-1) && age <= TimeSpan.FromMinutes(20);
    }
}
