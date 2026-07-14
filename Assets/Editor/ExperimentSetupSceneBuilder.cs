using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ExperimentSetupSceneBuilder
{
    const string CatalogFolder = "Assets/Resources/Experiment";
    const string CatalogPath = CatalogFolder + "/ExperimentSceneCatalog.asset";
    const string SetupScenePath = "Assets/Scenes/ExperimentSetup.unity";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string WorkloadScenePath = "Assets/Scenes/XRWorkloadProbeScene.unity";
    const string QuestionnaireScenePath = "Assets/Scenes/XRQuestionnaireReadScene.unity";

    [MenuItem("CARE-XR/Rebuild Experiment Setup Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Build();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(SetupScenePath);
    }

    public static void BuildFromCommandLine()
    {
        Build();
    }

    static void Build()
    {
        EnsureSceneExists(MainScenePath);
        EnsureSceneExists(WorkloadScenePath);
        EnsureSceneExists(QuestionnaireScenePath);
        EnsureAssetFolder(CatalogFolder);

        ExperimentSceneCatalog catalog = AssetDatabase.LoadAssetAtPath<ExperimentSceneCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<ExperimentSceneCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }

        catalog.ReplaceEntries(new[]
        {
            new ExperimentSceneCatalog.SceneEntry(
                "main-questionnaire",
                "Main questionnaire",
                "MainScene",
                "MainScene_Data"),
            new ExperimentSceneCatalog.SceneEntry(
                "workload-probe",
                "Workload probe",
                "XRWorkloadProbeScene",
                "XRWorkloadProbe_Data"),
            new ExperimentSceneCatalog.SceneEntry(
                "questionnaire-read",
                "Questionnaire with Read stage",
                "XRQuestionnaireReadScene",
                "XRQuestionnaireRead_Data")
        });
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Experiment Setup Controller");
        ExperimentSetupController controller = root.AddComponent<ExperimentSetupController>();
        controller.SetSceneCatalog(catalog);
        EditorUtility.SetDirty(controller);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, SetupScenePath))
            throw new InvalidOperationException($"Could not save {SetupScenePath}.");

        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ExperimentSetup] Scene, catalog, and Build Settings are ready.");
    }

    public static void ValidateFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene(SetupScenePath, OpenSceneMode.Single);
        ExperimentSetupController controller = UnityEngine.Object.FindFirstObjectByType<ExperimentSetupController>();
        if (controller == null)
            throw new InvalidOperationException("ExperimentSetup scene has no setup controller.");

        controller.InitializeForEditorValidation();

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        Dropdown dropdown = UnityEngine.Object.FindFirstObjectByType<Dropdown>();
        InputField[] inputs = UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None);
        Button startButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .FirstOrDefault(button => button.name == "Start Experiment");

        if (canvas == null)
            throw new InvalidOperationException("Researcher setup Canvas was not created.");
        if (dropdown == null || dropdown.options.Count != 3)
            throw new InvalidOperationException("Experiment scene dropdown was not populated with three scenes.");
        if (inputs.Length < 2)
            throw new InvalidOperationException("Participant and output input fields were not created.");
        if (startButton == null)
            throw new InvalidOperationException("Start Experiment button was not created.");
        if (EditorBuildSettings.scenes.Length < 4 ||
            !string.Equals(EditorBuildSettings.scenes[0].path, SetupScenePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ExperimentSetup is not the first Build Settings scene.");

        Debug.Log("[ExperimentSetup] Runtime UI smoke test passed: 3 scenes, 2 inputs, start action, and build order.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    public static void CapturePreviewFromCommandLine()
    {
        EditorSceneManager.OpenScene(SetupScenePath, OpenSceneMode.Single);
        ExperimentSetupController controller = UnityEngine.Object.FindFirstObjectByType<ExperimentSetupController>();
        if (controller == null)
            throw new InvalidOperationException("ExperimentSetup scene has no setup controller.");
        controller.InitializeForEditorValidation();

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        Camera camera = Camera.main;
        if (canvas == null || camera == null)
            throw new InvalidOperationException("Setup preview requires both Canvas and Camera.");

        const int width = 1440;
        const int height = 900;
        var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        RenderMode previousRenderMode = canvas.renderMode;
        Camera previousWorldCamera = canvas.worldCamera;

        try
        {
            camera.targetTexture = renderTexture;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            camera.Render();

            RenderTexture.active = renderTexture;
            screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            screenshot.Apply(false, false);

            string outputPath = Path.GetFullPath("Temp/ExperimentSetupPreview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
            Debug.Log($"[ExperimentSetup] Preview captured: {outputPath}");
        }
        finally
        {
            canvas.renderMode = previousRenderMode;
            canvas.worldCamera = previousWorldCamera;
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(screenshot);
            UnityEngine.Object.DestroyImmediate(renderTexture);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }
    }

    static void UpdateBuildSettings()
    {
        var requiredPaths = new[]
        {
            SetupScenePath,
            MainScenePath,
            WorkloadScenePath,
            QuestionnaireScenePath
        };
        var required = new HashSet<string>(requiredPaths, StringComparer.OrdinalIgnoreCase);
        var scenes = new List<EditorBuildSettingsScene>(requiredPaths.Length + EditorBuildSettings.scenes.Length);

        for (int i = 0; i < requiredPaths.Length; i++)
            scenes.Add(new EditorBuildSettingsScene(requiredPaths[i], true));

        EditorBuildSettingsScene[] existing = EditorBuildSettings.scenes;
        for (int i = 0; i < existing.Length; i++)
        {
            EditorBuildSettingsScene entry = existing[i];
            if (entry == null || required.Contains(entry.path))
                continue;
            scenes.Add(entry);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    static void EnsureSceneExists(string path)
    {
        if (File.Exists(Path.GetFullPath(path)))
            return;
        throw new FileNotFoundException($"Required experiment scene was not found: {path}");
    }

    static void EnsureAssetFolder(string path)
    {
        string[] parts = path.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
