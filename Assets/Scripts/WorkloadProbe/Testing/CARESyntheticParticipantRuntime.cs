#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// Editor-only state used by the deterministic end-to-end data-pipeline test.
/// It is never compiled into a participant build.
/// </summary>
public static class CAREXRSyntheticParticipantRuntime
{
    public const string ScenarioId = "hesitant_first_time_user_v1";

    public static bool Active { get; private set; }
    public static string ParticipantId { get; private set; } = "P100";
    public static bool QuestionnaireConfirmHeld { get; private set; }
    public static bool QuestionnaireReadHeld { get; private set; }
    public static bool KnobGrabbing { get; private set; }
    public static float KnobTwistDegrees { get; private set; }

    static Vector3 _headPosition = new Vector3(0f, 1.62f, 0f);
    static Quaternion _headRotation = Quaternion.identity;
    static Vector3 _leftHandPosition = new Vector3(-0.24f, 1.12f, 0.38f);
    static Quaternion _leftHandRotation = Quaternion.identity;
    static Vector3 _rightHandPosition = new Vector3(0.24f, 1.12f, 0.38f);
    static Quaternion _rightHandRotation = Quaternion.identity;
    static Vector3 _rayOrigin = new Vector3(0.24f, 1.12f, 0.38f);
    static Vector3 _rayDirection = Vector3.forward;

    public static void Activate(string participantId)
    {
        ParticipantId = string.IsNullOrWhiteSpace(participantId) ? "P100" : participantId.Trim();
        Active = true;
        QuestionnaireConfirmHeld = false;
        QuestionnaireReadHeld = false;
        KnobGrabbing = false;
        KnobTwistDegrees = 0f;
    }

    public static void Deactivate()
    {
        QuestionnaireConfirmHeld = false;
        QuestionnaireReadHeld = false;
        KnobGrabbing = false;
        KnobTwistDegrees = 0f;
        Active = false;
    }

    public static void SetQuestionnaireState(
        bool grabbing,
        float twistDegrees,
        bool confirmHeld)
    {
        KnobGrabbing = grabbing;
        KnobTwistDegrees = twistDegrees;
        QuestionnaireConfirmHeld = confirmHeld;
    }

    public static void SetReadHeld(bool held)
    {
        QuestionnaireReadHeld = held;
    }

    public static void SetTrackedPose(
        Vector3 headPosition,
        Quaternion headRotation,
        Vector3 leftHandPosition,
        Quaternion leftHandRotation,
        Vector3 rightHandPosition,
        Quaternion rightHandRotation,
        Vector3 rayOrigin,
        Vector3 rayDirection)
    {
        _headPosition = headPosition;
        _headRotation = headRotation;
        _leftHandPosition = leftHandPosition;
        _leftHandRotation = leftHandRotation;
        _rightHandPosition = rightHandPosition;
        _rightHandRotation = rightHandRotation;
        _rayOrigin = rayOrigin;
        _rayDirection = rayDirection.sqrMagnitude > 0.000001f
            ? rayDirection.normalized
            : Vector3.forward;
    }

    public static bool TryGetPose(XRNode node, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;
        if (!Active)
            return false;

        switch (node)
        {
            case XRNode.Head:
                position = _headPosition;
                rotation = _headRotation;
                return true;
            case XRNode.LeftHand:
                position = _leftHandPosition;
                rotation = _leftHandRotation;
                return true;
            case XRNode.RightHand:
                position = _rightHandPosition;
                rotation = _rightHandRotation;
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetRay(out Vector3 origin, out Vector3 direction)
    {
        origin = _rayOrigin;
        direction = _rayDirection;
        return Active;
    }
}

/// <summary>
/// Runs Comparison followed by Workload Probe through their real scene controllers.
/// Output is marked as synthetic and is intended only for regression testing.
/// </summary>
public sealed class CAREXRSyntheticParticipantOrchestrator : MonoBehaviour
{
    [Serializable]
    sealed class SyntheticRunSummary
    {
        public string schemaVersion = "CAREXR_SyntheticRun_v1.0";
        public string participantId = "";
        public string scenario = "";
        public string startedUtc = "";
        public string completedUtc = "";
        public bool completed;
        public string comparisonRunDirectory = "";
        public string workloadRunDirectory = "";
        public bool workloadRequested = true;
        public bool combinedProbeOnly;
        public bool enteredThroughExperimentSetup;
        public bool comparisonExportCompleted;
        public bool workloadExportCompleted;
        public bool workloadIntegrityPassed;
        public string workloadIntegrityManifest = "";
        public string error = "";
    }

    static CAREXRSyntheticParticipantOrchestrator _instance;
    string _participantId;
    string _outputRoot;
    bool _runWorkloadProbe = true;
    bool _useExperimentSetup;
    bool _runCombinedProbeOnly;
    SyntheticRunSummary _summary;

    public static void Begin(string participantId, string outputRoot, bool runWorkloadProbe = true)
    {
        if (_instance != null)
            return;

        CAREXRSyntheticParticipantRuntime.Activate(participantId);
        var go = new GameObject("CAREXR Synthetic Participant Orchestrator");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CAREXRSyntheticParticipantOrchestrator>();
        _instance._participantId = CAREXRSyntheticParticipantRuntime.ParticipantId;
        _instance._outputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? ExperimentRunContext.GetDefaultOutputRoot()
            : outputRoot;
        _instance._runWorkloadProbe = runWorkloadProbe;
        _instance.StartCoroutine(_instance.Run());
    }

    public static void BeginFromExperimentSetup(
        string participantId,
        string outputRoot,
        bool runWorkloadProbe = false)
    {
        if (_instance != null)
            return;

        CAREXRSyntheticParticipantRuntime.Activate(participantId);
        var go = new GameObject("CAREXR Synthetic Participant Orchestrator");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CAREXRSyntheticParticipantOrchestrator>();
        _instance._participantId = CAREXRSyntheticParticipantRuntime.ParticipantId;
        _instance._outputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? ExperimentRunContext.GetDefaultOutputRoot()
            : outputRoot;
        _instance._runWorkloadProbe = runWorkloadProbe;
        _instance._useExperimentSetup = true;
        _instance.StartCoroutine(_instance.Run());
    }

    public static void BeginCombinedProbeOnly(string participantId, string outputRoot)
    {
        if (_instance != null)
            return;

        CAREXRSyntheticParticipantRuntime.Activate(participantId);
        var go = new GameObject("CAREXR Synthetic Combined-Probe Orchestrator");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<CAREXRSyntheticParticipantOrchestrator>();
        _instance._participantId = CAREXRSyntheticParticipantRuntime.ParticipantId;
        _instance._outputRoot = string.IsNullOrWhiteSpace(outputRoot)
            ? ExperimentRunContext.GetDefaultOutputRoot()
            : outputRoot;
        _instance._runWorkloadProbe = true;
        _instance._runCombinedProbeOnly = true;
        _instance.StartCoroutine(_instance.Run());
    }

    IEnumerator Run()
    {
        UnityEngine.Random.InitState(100);
        _summary = new SyntheticRunSummary
        {
            participantId = _participantId,
            scenario = CAREXRSyntheticParticipantRuntime.ScenarioId,
            startedUtc = DateTime.UtcNow.ToString("O"),
            workloadRequested = _runWorkloadProbe,
            combinedProbeOnly = _runCombinedProbeOnly,
            enteredThroughExperimentSetup = _useExperimentSetup
        };

        if (_runCombinedProbeOnly)
        {
            yield return RunScene(
                new ExperimentSceneCatalog.SceneEntry(
                    "combined-probe",
                    "Combined probe repetition study",
                    "XRCombinedProbeScene",
                    "XRCombinedProbe_Data"),
                isWorkload: true);

            _summary.completed = string.IsNullOrEmpty(_summary.error) &&
                                 _summary.workloadExportCompleted &&
                                 _summary.workloadIntegrityPassed;
            _summary.completedUtc = DateTime.UtcNow.ToString("O");
            WriteOverallSummary();
            CAREXRSyntheticParticipantRuntime.Deactivate();
            Debug.Log(_summary.completed
                ? $"[CARE-XR Synthetic] {_participantId} combined-probe run completed and passed integrity checks."
                : $"[CARE-XR Synthetic] Combined-probe run failed: {_summary.error}");
            yield return new WaitForSecondsRealtime(0.5f);
            UnityEditor.EditorApplication.isPlaying = false;
            yield break;
        }

        if (_useExperimentSetup)
        {
            yield return RunComparisonThroughExperimentSetup();
        }
        else
        {
            yield return RunScene(
                new ExperimentSceneCatalog.SceneEntry(
                    "paxsm-comparison",
                    "PAXSM comparison study",
                    "PAXSMComparisonScene",
                    "PAXSMComparison_Data"),
                isWorkload: false);
        }

        if (_runWorkloadProbe && string.IsNullOrEmpty(_summary.error))
        {
            yield return RunScene(
                new ExperimentSceneCatalog.SceneEntry(
                    "workload",
                    "Workload probe",
                    "XRWorkloadProbeScene",
                    "XRWorkloadProbe_Data"),
                isWorkload: true);
        }

        _summary.completed = string.IsNullOrEmpty(_summary.error) &&
                             _summary.comparisonExportCompleted &&
                             (!_runWorkloadProbe ||
                              (_summary.workloadExportCompleted && _summary.workloadIntegrityPassed));
        _summary.completedUtc = DateTime.UtcNow.ToString("O");
        WriteOverallSummary();
        CAREXRSyntheticParticipantRuntime.Deactivate();

        Debug.Log(_summary.completed
            ? $"[CARE-XR Synthetic] {_participantId} run completed and passed integrity checks."
            : $"[CARE-XR Synthetic] End-to-end run failed: {_summary.error}");

        yield return new WaitForSecondsRealtime(0.5f);
        UnityEditor.EditorApplication.isPlaying = false;
    }

    IEnumerator RunComparisonThroughExperimentSetup()
    {
        const string setupSceneName = "ExperimentSetup";
        const string comparisonSceneName = "PAXSMComparisonScene";
        if (!string.Equals(SceneManager.GetActiveScene().name, setupSceneName, StringComparison.Ordinal))
        {
            _summary.error = $"Expected {setupSceneName} to be active before the setup-path test.";
            yield break;
        }

        ExperimentSetupController setup = null;
        float setupDeadline = Time.realtimeSinceStartup + 20f;
        while (setup == null && Time.realtimeSinceStartup < setupDeadline)
        {
            setup = FindFirstObjectByType<ExperimentSetupController>();
            yield return null;
        }
        if (setup == null)
        {
            _summary.error = "Experiment Setup did not create its controller.";
            yield break;
        }

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        InputField participantInput = typeof(ExperimentSetupController)
            .GetField("_participantInput", flags)?.GetValue(setup) as InputField;
        InputField outputInput = typeof(ExperimentSetupController)
            .GetField("_outputInput", flags)?.GetValue(setup) as InputField;
        Dropdown sceneDropdown = typeof(ExperimentSetupController)
            .GetField("_sceneDropdown", flags)?.GetValue(setup) as Dropdown;
        Button startButton = typeof(ExperimentSetupController)
            .GetField("_startButton", flags)?.GetValue(setup) as Button;
        var availableScenes = typeof(ExperimentSetupController)
            .GetField("_availableScenes", flags)?.GetValue(setup) as IList;

        if (participantInput == null || outputInput == null || sceneDropdown == null ||
            startButton == null || availableScenes == null)
        {
            _summary.error = "Experiment Setup form controls could not be resolved.";
            yield break;
        }

        int comparisonIndex = -1;
        for (int i = 0; i < availableScenes.Count; i++)
        {
            var entry = availableScenes[i] as ExperimentSceneCatalog.SceneEntry;
            if (entry != null && string.Equals(entry.sceneName, comparisonSceneName, StringComparison.Ordinal))
            {
                comparisonIndex = i;
                break;
            }
        }
        if (comparisonIndex < 0)
        {
            _summary.error = "PAXSM comparison is not available in the Experiment Setup catalog.";
            yield break;
        }

        sceneDropdown.value = comparisonIndex;
        participantInput.text = _participantId;
        outputInput.text = _outputRoot;
        yield return null;

        if (!startButton.interactable)
        {
            _summary.error = "Experiment Setup rejected the synthetic P888 configuration.";
            yield break;
        }

        startButton.onClick.Invoke();
        yield return null;
        if (!ExperimentRunContext.IsConfigured)
        {
            _summary.error = "Experiment Setup did not create an active run after Start experiment.";
            yield break;
        }

        _summary.comparisonRunDirectory = ExperimentRunContext.RunDirectory;
        WriteSyntheticMarker(_summary.comparisonRunDirectory, comparisonSceneName);

        float sceneDeadline = Time.realtimeSinceStartup + 20f;
        while (!string.Equals(SceneManager.GetActiveScene().name, comparisonSceneName, StringComparison.Ordinal) &&
               Time.realtimeSinceStartup < sceneDeadline)
            yield return null;
        if (!string.Equals(SceneManager.GetActiveScene().name, comparisonSceneName, StringComparison.Ordinal))
        {
            _summary.error = $"Experiment Setup did not load {comparisonSceneName}.";
            yield break;
        }

        yield return WaitForSceneCompletion(comparisonSceneName, isWorkload: false);
    }

    IEnumerator RunScene(ExperimentSceneCatalog.SceneEntry scene, bool isWorkload)
    {
        ExperimentRunContext.ClearActiveRun();
        if (!ExperimentRunContext.Configure(scene, _participantId, _outputRoot, out string error))
        {
            _summary.error = $"Could not configure {scene.sceneName}: {error}";
            yield break;
        }

        string runDirectory = ExperimentRunContext.RunDirectory;
        if (isWorkload)
            _summary.workloadRunDirectory = runDirectory;
        else
            _summary.comparisonRunDirectory = runDirectory;
        WriteSyntheticMarker(runDirectory, scene.sceneName);

        AsyncOperation load = SceneManager.LoadSceneAsync(scene.sceneName, LoadSceneMode.Single);
        if (load == null)
        {
            _summary.error = $"Could not start loading {scene.sceneName}.";
            yield break;
        }
        while (!load.isDone)
            yield return null;

        yield return WaitForSceneCompletion(scene.sceneName, isWorkload);
    }

    IEnumerator WaitForSceneCompletion(string sceneName, bool isWorkload)
    {
        XRWorkloadProbeSceneController controller = null;
        float controllerDeadline = Time.realtimeSinceStartup + 20f;
        while (controller == null && Time.realtimeSinceStartup < controllerDeadline)
        {
            controller = FindFirstObjectByType<XRWorkloadProbeSceneController>();
            yield return null;
        }
        if (controller == null)
        {
            _summary.error = $"{sceneName} did not create its controller.";
            yield break;
        }

        FieldInfo completedField = typeof(XRWorkloadProbeSceneController).GetField(
            "_completedExportWritten",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo integrityPassedField = typeof(XRWorkloadProbeSceneController).GetField(
            "_lastDataIntegrityPassed",
            BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo integrityPathField = typeof(XRWorkloadProbeSceneController).GetField(
            "_lastDataIntegrityPath",
            BindingFlags.Instance | BindingFlags.NonPublic);

        float deadline = Time.realtimeSinceStartup + 720f;
        bool completed = false;
        while (Time.realtimeSinceStartup < deadline)
        {
            completed = completedField != null &&
                        completedField.GetValue(controller) is bool value && value;
            if (completed)
                break;
            yield return null;
        }

        if (!completed)
        {
            _summary.error = $"{sceneName} did not finish within 12 minutes.";
            yield break;
        }

        if (isWorkload)
        {
            _summary.workloadExportCompleted = true;
            _summary.workloadIntegrityPassed = integrityPassedField != null &&
                                               integrityPassedField.GetValue(controller) is bool passed && passed;
            _summary.workloadIntegrityManifest = integrityPathField?.GetValue(controller) as string ?? "";
            if (!_summary.workloadIntegrityPassed)
                _summary.error = "Workload Probe completed, but its data-integrity manifest failed.";
        }
        else
        {
            _summary.comparisonExportCompleted = true;
        }

        yield return new WaitForSecondsRealtime(0.5f);
    }

    void WriteSyntheticMarker(string runDirectory, string sceneName)
    {
        Directory.CreateDirectory(runDirectory);
        string text =
            "SYNTHETIC TEST DATA - DO NOT INCLUDE IN HUMAN-PARTICIPANT ANALYSIS\n" +
            $"participantId={_participantId}\n" +
            $"scenario={CAREXRSyntheticParticipantRuntime.ScenarioId}\n" +
            $"scene={sceneName}\n" +
            $"createdUtc={DateTime.UtcNow:O}\n";
        File.WriteAllText(Path.Combine(runDirectory, "SYNTHETIC_TEST_RUN.txt"), text, Encoding.UTF8);
    }

    void WriteOverallSummary()
    {
        try
        {
            string participantDirectory = Path.Combine(_outputRoot, _participantId);
            Directory.CreateDirectory(participantDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path = Path.Combine(
                participantDirectory,
                $"SYNTHETIC_{_participantId}_EndToEnd_{stamp}.json");
            File.WriteAllText(path, JsonUtility.ToJson(_summary, true), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[CARE-XR Synthetic] Could not write overall summary: {exception}");
        }
    }
}
#endif
