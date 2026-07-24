using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

public static class ExperimentSetupSceneBuilder
{
    const string CatalogFolder = "Assets/Resources/Experiment";
    const string CatalogPath = CatalogFolder + "/ExperimentSceneCatalog.asset";
    const string SetupScenePath = "Assets/Scenes/ExperimentSetup.unity";
    const string MainScenePath = "Assets/Scenes/MainScene.unity";
    const string WorkloadScenePath = "Assets/Scenes/XRWorkloadProbeScene.unity";
    const string CombinedScenePath = "Assets/Scenes/XRCombinedProbeScene.unity";
    const string QuestionnaireScenePath = "Assets/Scenes/XRQuestionnaireReadScene.unity";
    const string ComparisonScenePath = "Assets/Scenes/PAXSMComparisonScene.unity";
    const string XrOriginPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.2.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
    const string LeftControllerPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.2.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
    const string RightControllerPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.2.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";
    const string HeadsetCaptureSessionKey = "CAREXR.ExperimentSetup.CaptureHeadsetView";
    const string HeadsetCaptureRequestFile = "CAREXR_ExperimentSetupHeadsetCapture.request";
    const string RebuildRequestFile = "CAREXR_ExperimentSetupRebuild.request";
    static double _nextHeadsetCaptureRequestCheck;

    [InitializeOnLoadMethod]
    static void RegisterHeadsetCaptureRunner()
    {
        EditorApplication.playModeStateChanged -= HandleHeadsetCapturePlayMode;
        EditorApplication.playModeStateChanged += HandleHeadsetCapturePlayMode;
        EditorApplication.update -= CheckForHeadsetCaptureRequest;
        EditorApplication.update += CheckForHeadsetCaptureRequest;
        EditorApplication.update -= CheckForRebuildRequest;
        EditorApplication.update += CheckForRebuildRequest;
    }

    static void CheckForRebuildRequest()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string requestPath = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? ".",
            "Temp",
            RebuildRequestFile);
        if (!File.Exists(requestPath))
            return;

        File.Delete(requestPath);
        Build();
    }

    static void CheckForHeadsetCaptureRequest()
    {
        if (EditorApplication.timeSinceStartup < _nextHeadsetCaptureRequestCheck)
            return;
        _nextHeadsetCaptureRequestCheck = EditorApplication.timeSinceStartup + 0.5d;

        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        string requestPath = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? ".",
            "Temp",
            HeadsetCaptureRequestFile);
        if (!File.Exists(requestPath))
            return;

        File.Delete(requestPath);
        CaptureHeadsetWaitingView();
    }

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
        EnsureSceneExists(CombinedScenePath);
        EnsureSceneExists(QuestionnaireScenePath);
        EnsureSceneExists(ComparisonScenePath);
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
                "combined-probe",
                "Combined probe repetition study",
                "XRCombinedProbeScene",
                "XRCombinedProbe_Data"),
            new ExperimentSceneCatalog.SceneEntry(
                "paxsm-response-calibration",
                "Personal knob reference calibration",
                "XRQuestionnaireReadScene",
                "PAXSMPersonalKnobReference_Data"),
            new ExperimentSceneCatalog.SceneEntry(
                "paxsm-comparison",
                "PAXSM comparison study",
                "PAXSMComparisonScene",
                "PAXSMComparison_Data")
        });
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var root = new GameObject("Experiment Setup Controller");
        ExperimentSetupController controller = root.AddComponent<ExperimentSetupController>();
        controller.SetSceneCatalog(catalog);
        EditorUtility.SetDirty(controller);

        GameObject xrOriginPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(XrOriginPrefabPath);
        if (xrOriginPrefab == null)
            throw new FileNotFoundException("The configured XR Origin prefab is missing.", XrOriginPrefabPath);

        GameObject xrOrigin = PrefabUtility.InstantiatePrefab(xrOriginPrefab, scene) as GameObject;
        if (xrOrigin == null)
            throw new InvalidOperationException("Could not instantiate the configured XR Origin prefab.");
        xrOrigin.name = "XR Origin (XR Rig)";
        DisableWaitingRigInteraction(xrOrigin);

        Camera headsetCamera = xrOrigin.GetComponentInChildren<Camera>(true);
        if (headsetCamera == null)
            throw new InvalidOperationException("The configured XR Origin has no headset Camera.");
        headsetCamera.gameObject.tag = "MainCamera";
        if (headsetCamera.GetComponent<UniversalAdditionalCameraData>() == null)
            headsetCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();

        ExperimentSetupHeadsetWaitingView waitingView =
            headsetCamera.gameObject.AddComponent<ExperimentSetupHeadsetWaitingView>();
        waitingView.SetControllerVisualPrefabs(
            AssetDatabase.LoadAssetAtPath<GameObject>(LeftControllerPrefabPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(RightControllerPrefabPath));
        EditorUtility.SetDirty(waitingView);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, SetupScenePath))
            throw new InvalidOperationException($"Could not save {SetupScenePath}.");

        UpdateBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ExperimentSetup] Scene, catalog, and Build Settings are ready.");
    }

    static void DisableWaitingRigInteraction(GameObject xrOrigin)
    {
        MonoBehaviour[] behaviours = xrOrigin.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            string typeName = behaviour.GetType().Name;
            if (typeName.Contains("MoveProvider", StringComparison.Ordinal) ||
                typeName.Contains("TurnProvider", StringComparison.Ordinal) ||
                typeName.Contains("TeleportationProvider", StringComparison.Ordinal) ||
                typeName.Contains("ClimbProvider", StringComparison.Ordinal) ||
                string.Equals(typeName, "XRInputModalityManager", StringComparison.Ordinal))
                behaviour.enabled = false;
        }

        Transform[] transforms = xrOrigin.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null)
                continue;
            if (string.Equals(candidate.name, "Left Controller", StringComparison.Ordinal) ||
                string.Equals(candidate.name, "Right Controller", StringComparison.Ordinal))
                candidate.gameObject.SetActive(false);
        }
    }

    public static void ValidateFromCommandLine()
    {
        Scene scene = EditorSceneManager.OpenScene(SetupScenePath, OpenSceneMode.Single);
        ExperimentSetupController controller = UnityEngine.Object.FindFirstObjectByType<ExperimentSetupController>();
        if (controller == null)
            throw new InvalidOperationException("ExperimentSetup scene has no setup controller.");
        if (GameObject.Find("XR Origin (XR Rig)") == null)
            throw new InvalidOperationException("ExperimentSetup scene has no configured XR Origin.");

        controller.InitializeForEditorValidation();

        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        Dropdown dropdown = UnityEngine.Object.FindFirstObjectByType<Dropdown>();
        InputField[] inputs = UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsSortMode.None);
        Button startButton = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .FirstOrDefault(button => button.name == "Start Experiment");

        if (canvas == null)
            throw new InvalidOperationException("Researcher setup Canvas was not created.");
        if (canvas.renderMode != RenderMode.ScreenSpaceCamera || canvas.worldCamera == null)
            throw new InvalidOperationException("Researcher setup UI is not routed through its desktop-only Camera.");
        UniversalAdditionalCameraData researcherCameraData =
            canvas.worldCamera.GetComponent<UniversalAdditionalCameraData>();
        if (canvas.worldCamera.stereoTargetEye != StereoTargetEyeMask.None ||
            researcherCameraData == null || researcherCameraData.allowXRRendering)
            throw new InvalidOperationException("Researcher setup UI Camera is still allowed to render in the headset.");
        if (dropdown == null || dropdown.options.Count != 5)
            throw new InvalidOperationException("Experiment scene dropdown was not populated with five scenes.");
        if (inputs.Length < 2)
            throw new InvalidOperationException("Participant and output input fields were not created.");
        if (startButton == null)
            throw new InvalidOperationException("Start Experiment button was not created.");
        if (EditorBuildSettings.scenes.Length < 6 ||
            !string.Equals(EditorBuildSettings.scenes[0].path, SetupScenePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ExperimentSetup is not the first Build Settings scene.");

        Debug.Log("[ExperimentSetup] Runtime UI smoke test passed: 5 scenes, 2 inputs, start action, and build order.");
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

    [MenuItem("CARE-XR/Testing/Capture Experiment Setup Headset View _F8")]
    public static void CaptureHeadsetWaitingView()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (!string.Equals(activeScene.path, SetupScenePath, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogError("[ExperimentSetup] Open ExperimentSetup.unity before capturing the headset waiting view.");
            return;
        }

        SessionState.SetBool(HeadsetCaptureSessionKey, true);
        EditorApplication.isPlaying = true;
    }

    static void HandleHeadsetCapturePlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode ||
            !SessionState.GetBool(HeadsetCaptureSessionKey, false))
            return;

        EditorApplication.delayCall += CaptureHeadsetWaitingViewInPlayMode;
    }

    static void CaptureHeadsetWaitingViewInPlayMode()
    {
        RenderTexture renderTexture = null;
        Texture2D screenshot = null;

        try
        {
            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("The headset preview did not create a Camera.");
            if (camera.GetComponent<ExperimentSetupHeadsetWaitingView>() == null)
                throw new InvalidOperationException("The headset waiting-view component was not attached.");

            Canvas researcherCanvas = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.name == "Researcher Setup Canvas");
            if (researcherCanvas == null || researcherCanvas.renderMode != RenderMode.ScreenSpaceCamera ||
                researcherCanvas.worldCamera == null ||
                researcherCanvas.worldCamera.stereoTargetEye != StereoTargetEyeMask.None)
                throw new InvalidOperationException("The researcher interface is not isolated from headset rendering.");

            MeshRenderer skyRenderer = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .FirstOrDefault(candidate =>
                    candidate.gameObject.name == "Participant Waiting Sky");
            if (skyRenderer == null || skyRenderer.sharedMaterial == null)
                throw new InvalidOperationException("The participant waiting sky was not created.");

            GameObject platform = GameObject.Find("Participant Waiting Platform");
            if (platform == null)
                throw new InvalidOperationException("The participant waiting platform was not created.");

            GameObject leftController = GameObject.Find("Tracked Left Controller");
            GameObject rightController = GameObject.Find("Tracked Right Controller");
            if (leftController == null || rightController == null)
                throw new InvalidOperationException("The tracked controller visuals were not created.");
            if (leftController.GetComponentsInChildren<MeshRenderer>(true).Length == 0 ||
                rightController.GetComponentsInChildren<MeshRenderer>(true).Length == 0)
                throw new InvalidOperationException("A tracked controller visual has no renderable model.");

            const int width = 1280;
            const int height = 720;
            renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            StereoTargetEyeMask previousStereoTarget = camera.stereoTargetEye;
            Vector3 previousCameraPosition = camera.transform.position;
            Quaternion previousCameraRotation = camera.transform.rotation;
            Vector3 previousLeftPosition = leftController.transform.position;
            Quaternion previousLeftRotation = leftController.transform.rotation;
            Vector3 previousRightPosition = rightController.transform.position;
            Quaternion previousRightRotation = rightController.transform.rotation;
            bool previousLeftActive = leftController.activeSelf;
            bool previousRightActive = rightController.activeSelf;

            try
            {
                // Use a deterministic neutral pose for visual QA. Runtime tracking
                // takes over again immediately after this one-frame capture.
                camera.transform.SetPositionAndRotation(
                    new Vector3(0f, 1.58f, -0.42f),
                    Quaternion.Euler(10f, 0f, 0f));
                leftController.SetActive(true);
                rightController.SetActive(true);
                leftController.transform.SetPositionAndRotation(
                    new Vector3(-0.25f, 1.12f, 0.48f),
                    Quaternion.Euler(35f, 15f, -10f));
                rightController.transform.SetPositionAndRotation(
                    new Vector3(0.25f, 1.12f, 0.48f),
                    Quaternion.Euler(35f, -15f, 10f));
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                screenshot.Apply(false, false);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.stereoTargetEye = previousStereoTarget;
                camera.transform.SetPositionAndRotation(previousCameraPosition, previousCameraRotation);
                leftController.transform.SetPositionAndRotation(previousLeftPosition, previousLeftRotation);
                rightController.transform.SetPositionAndRotation(previousRightPosition, previousRightRotation);
                leftController.SetActive(previousLeftActive);
                rightController.SetActive(previousRightActive);
                RenderTexture.active = previousActive;
            }

            Color32[] pixels = screenshot.GetPixels32();
            double luminanceSum = 0d;
            double luminanceSquaredSum = 0d;
            int sampleCount = 0;
            for (int i = 0; i < pixels.Length; i += 32)
            {
                Color32 pixel = pixels[i];
                double luminance = (0.2126d * pixel.r + 0.7152d * pixel.g + 0.0722d * pixel.b) / 255d;
                luminanceSum += luminance;
                luminanceSquaredSum += luminance * luminance;
                sampleCount++;
            }

            double mean = luminanceSum / Math.Max(1, sampleCount);
            double variance = luminanceSquaredSum / Math.Max(1, sampleCount) - mean * mean;
            if (mean > 0.92d)
                throw new InvalidOperationException($"The headset waiting view is too bright (mean luminance {mean:F3}).");
            if (variance < 0.0004d)
                throw new InvalidOperationException($"The headset waiting view appears blank or flat (variance {variance:F5}).");

            string outputPath = Path.GetFullPath("Temp/ExperimentSetupHeadsetWaitingView.png");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, screenshot.EncodeToPNG());
            Debug.Log($"[ExperimentSetup] Headset waiting view passed: mean={mean:F3}, variance={variance:F5}, screenshot={outputPath}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            if (screenshot != null)
                UnityEngine.Object.DestroyImmediate(screenshot);
            if (renderTexture != null)
                UnityEngine.Object.DestroyImmediate(renderTexture);
            SessionState.EraseBool(HeadsetCaptureSessionKey);
            EditorApplication.isPlaying = false;
        }
    }

    static void UpdateBuildSettings()
    {
        var requiredPaths = new[]
        {
            SetupScenePath,
            MainScenePath,
            WorkloadScenePath,
            CombinedScenePath,
            QuestionnaireScenePath,
            ComparisonScenePath
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
