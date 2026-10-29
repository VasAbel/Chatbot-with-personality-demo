using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Creates one isolated output directory for every Play-mode simulation run.
/// All persistent simulation state and measurement/debug logs should use RunDirectory
/// so different model runs cannot contaminate each other.
/// </summary>
public static class SimulationRunContext
{
    private static string runDirectory;
    private static string runId;
    private static string modelId;

    public static string RunDirectory
    {
        get
        {
            EnsureInitialized();
            return runDirectory;
        }
    }

    public static string RunId
    {
        get
        {
            EnsureInitialized();
            return runId;
        }
    }

    public static string ModelId
    {
        get
        {
            EnsureInitialized();
            return modelId;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlaySession()
    {
        runDirectory = null;
        runId = null;
        modelId = null;
    }

    public static void EnsureInitialized()
    {
        if (!string.IsNullOrWhiteSpace(runDirectory))
            return;

        GptClient client = UnityEngine.Object.FindObjectOfType<GptClient>();
        string selectedModel = client != null
            ? client.SelectedModelId
            : "unknown-model";

        Initialize(selectedModel);
    }

    public static void Initialize(string selectedModelId)
    {
        if (!string.IsNullOrWhiteSpace(runDirectory))
        {
            if (!string.Equals(modelId, selectedModelId, StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"SimulationRunContext is already initialized for model '{modelId}'. " +
                    $"Ignoring later request for '{selectedModelId}'."
                );
            }

            return;
        }

        modelId = string.IsNullOrWhiteSpace(selectedModelId)
            ? "unknown-model"
            : selectedModelId.Trim();

        string safeModel = MakeFileNameSafe(modelId);
        runId = $"{safeModel}_{DateTime.Now:yyyyMMdd_HHmmss_fff}";

        runDirectory = Path.Combine(
            Application.persistentDataPath,
            "SimulationRuns",
            safeModel,
            runId
        );

        Directory.CreateDirectory(runDirectory);
        WriteMetadata();

        Debug.Log(
            $"[Simulation Run] Model: {modelId}\n" +
            $"Run ID: {runId}\n" +
            $"Output: {runDirectory}"
        );
    }

    private static void WriteMetadata()
    {
        string metadataPath = Path.Combine(runDirectory, "run_metadata.txt");

        var sb = new StringBuilder();
        sb.AppendLine($"runId={runId}");
        sb.AppendLine($"modelId={modelId}");
        sb.AppendLine($"realStartTime={DateTime.Now:O}");
        sb.AppendLine($"unityVersion={Application.unityVersion}");
        sb.AppendLine($"platform={Application.platform}");

        File.WriteAllText(metadataPath, sb.ToString());
    }

    private static string MakeFileNameSafe(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');

        return value.Replace('/', '_').Replace('\\', '_');
    }
}
