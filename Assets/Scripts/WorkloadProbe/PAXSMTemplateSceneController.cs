using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using InputSystemKeyboard = UnityEngine.InputSystem.Keyboard;
#endif

public class PAXSMTemplateSceneController : MonoBehaviour
{
    const string SceneScheduleResourcesPath = "PAXSM/SceneSchedules/Template";

    [Serializable]
    public class QuestionnaireSlot
    {
        public string slotId = "questionnaire_slot";
        public string displayName = "Questionnaire Slot";
        public string insertionPointId = "after_block";
        public string questionnaireId = "";
        public string questionnaireBankResourcesPath = "";
    }

    [Serializable]
    public class EmptyBlock
    {
        public string blockId = "block_01";
        public string displayName = "Empty Block 1";
        public List<QuestionnaireSlot> questionnaireSlots = new List<QuestionnaireSlot>();
    }

    [Serializable]
    class RuntimeSceneSchedule
    {
        public string sceneId = "";
        public List<RuntimeQuestionnaireAssignment> assignments = new List<RuntimeQuestionnaireAssignment>();
    }

    [Serializable]
    class RuntimeQuestionnaireAssignment
    {
        public string slotId = "";
        public string insertionPointId = "";
        public string blockId = "";
        public string questionnaireId = "";
        public string questionnaireBankResourcesPath = "";
        public string construct = "";
        public string instrumentName = "";
        public int scale;
        public string responseMode = "";
        public bool confidenceEnabled;
    }

    public string participantId = "P001";
    public bool startAutomatically = true;
    public List<EmptyBlock> blocks = new List<EmptyBlock>();

    Camera _mainCamera;
    TextMesh _titleText;
    TextMesh _cueText;
    TextMesh _statusText;
    TextMesh _feedbackText;
    readonly List<InputDevice> _rightHandDevices = new List<InputDevice>();
    bool _previousPrimaryButton;
    int _currentBlockIndex = -1;
    int _currentSlotIndex = -1;
    RuntimeSceneSchedule _publishedSchedule = new RuntimeSceneSchedule();

    void Awake()
    {
        EnsureDefaultBlocks();
        LoadPublishedSchedule();
        EnsureCamera();
        AddTrackedPoseDriverIfAvailable(_mainCamera.gameObject);
        BuildDisplay();
    }

    void Start()
    {
        if (startAutomatically)
            StartCoroutine(RunTemplate());
    }

    IEnumerator RunTemplate()
    {
        ShowIntro();
        yield return WaitForAdvance();

        for (_currentBlockIndex = 0; _currentBlockIndex < blocks.Count; _currentBlockIndex++)
        {
            EmptyBlock block = blocks[_currentBlockIndex];
            ShowEmptyBlock(block);
            yield return WaitForAdvance();

            for (_currentSlotIndex = 0; _currentSlotIndex < block.questionnaireSlots.Count; _currentSlotIndex++)
            {
                ShowQuestionnaireSlot(block, block.questionnaireSlots[_currentSlotIndex]);
                yield return WaitForAdvance();
            }
        }

        ShowComplete();
    }

    IEnumerator WaitForAdvance()
    {
        while (!AdvancePressedThisFrame())
            yield return null;
        SendConfirmationHaptic();
        yield return null;
    }

    bool AdvancePressedThisFrame()
    {
        bool pressed = false;
        _rightHandDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right |
            InputDeviceCharacteristics.Controller,
            _rightHandDevices);
        foreach (InputDevice device in _rightHandDevices)
        {
            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary) && primary)
            {
                pressed = true;
                break;
            }
        }

        bool pressedThisFrame = pressed && !_previousPrimaryButton;
        _previousPrimaryButton = pressed;
#if ENABLE_INPUT_SYSTEM
        InputSystemKeyboard keyboard = InputSystemKeyboard.current;
        if (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame))
            pressedThisFrame = true;
#endif
        return pressedThisFrame;
    }

    void SendConfirmationHaptic()
    {
        foreach (InputDevice device in _rightHandDevices)
        {
            if (device.isValid)
                device.SendHapticImpulse(0u, 0.22f, 0.05f);
        }
    }

    void ShowIntro()
    {
        _titleText.text = "PAXSM Task Template";
        _cueText.text = "Empty blocks and questionnaire insertion slots";
        _statusText.text = $"Configured blocks: {blocks.Count}\nNo task logic is attached.";
        _feedbackText.text = "Press A to begin\nDesktop: Enter or Space";
    }

    void ShowEmptyBlock(EmptyBlock block)
    {
        _titleText.text = block.displayName;
        _cueText.text = "Empty task block";
        _statusText.text =
            $"Block {_currentBlockIndex + 1}/{blocks.Count}\n" +
            $"blockId: {block.blockId}\n" +
            $"Questionnaire slots: {block.questionnaireSlots.Count}";
        _feedbackText.text = "No task is assigned.\nPress A to complete this block.";
    }

    void ShowQuestionnaireSlot(EmptyBlock block, QuestionnaireSlot slot)
    {
        RuntimeQuestionnaireAssignment published = FindPublishedAssignment(slot.slotId);
        bool hasPublishedAssignment = published != null;
        bool hasSceneAssignment = !string.IsNullOrWhiteSpace(slot.questionnaireId) ||
                                  !string.IsNullOrWhiteSpace(slot.questionnaireBankResourcesPath);
        bool assigned = hasPublishedAssignment || hasSceneAssignment;
        string questionnaireId = hasPublishedAssignment ? published.questionnaireId : slot.questionnaireId;
        string bankPath = hasPublishedAssignment
            ? published.questionnaireBankResourcesPath
            : slot.questionnaireBankResourcesPath;
        string questionnaireName = hasPublishedAssignment && !string.IsNullOrWhiteSpace(published.instrumentName)
            ? published.instrumentName
            : hasPublishedAssignment && !string.IsNullOrWhiteSpace(published.construct)
                ? published.construct
                : questionnaireId;
        _titleText.text = slot.displayName;
        _cueText.text = hasPublishedAssignment
            ? "Agent-published questionnaire assignment"
            : assigned ? "Scene questionnaire assignment" : "Empty questionnaire slot";
        _statusText.text =
            $"blockId: {block.blockId}\n" +
            $"slotId: {slot.slotId}\n" +
            $"insertionPointId: {slot.insertionPointId}\n" +
            $"questionnaire: {(assigned ? questionnaireName : "not assigned")}" +
            (hasPublishedAssignment
                ? $"\nscale: {published.scale}-point {published.responseMode}\n" +
                  $"bank: {bankPath}"
                : assigned ? $"\nbank: {bankPath}" : "");
        _feedbackText.text = assigned
            ? "Assignment loaded for this exact slot.\nPress A to continue."
            : "Assign a questionnaire through the Agent.\nPress A to continue.";
    }

    void LoadPublishedSchedule()
    {
        TextAsset scheduleAsset = Resources.Load<TextAsset>(SceneScheduleResourcesPath);
        if (scheduleAsset == null || string.IsNullOrWhiteSpace(scheduleAsset.text))
            return;

        try
        {
            RuntimeSceneSchedule parsed = JsonUtility.FromJson<RuntimeSceneSchedule>(scheduleAsset.text);
            if (parsed != null && parsed.assignments != null)
                _publishedSchedule = parsed;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Could not read PAXSM Template schedule: {exception.Message}");
        }
    }

    RuntimeQuestionnaireAssignment FindPublishedAssignment(string slotId)
    {
        if (_publishedSchedule == null || _publishedSchedule.assignments == null)
            return null;

        return _publishedSchedule.assignments.Find(assignment =>
            assignment != null &&
            string.Equals(assignment.slotId, slotId, StringComparison.OrdinalIgnoreCase));
    }

    void ShowComplete()
    {
        _titleText.text = "Template Complete";
        _cueText.text = "All empty blocks and questionnaire slots were visited.";
        _statusText.text = $"Blocks completed: {blocks.Count}";
        _feedbackText.text = "No task or questionnaire response data were collected.";
    }

    void EnsureDefaultBlocks()
    {
        if (blocks.Count > 0)
            return;

        for (int index = 1; index <= 4; index++)
        {
            string suffix = index.ToString("00");
            EmptyBlock block = new EmptyBlock
            {
                blockId = "block_" + suffix,
                displayName = "Empty Block " + index
            };
            block.questionnaireSlots.Add(new QuestionnaireSlot
            {
                slotId = "after_block_" + suffix,
                displayName = "Questionnaire Slot after Block " + index,
                insertionPointId = "after_block"
            });
            blocks.Add(block);
        }
    }

    void BuildDisplay()
    {
        RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.5f);
        _titleText = CreateText("Template_Title", "Template_TitleAnchor", new Vector3(0f, 2.32f, 4.24f), 0.03f, TextAnchor.MiddleCenter);
        _cueText = CreateText("Template_Cue", "Template_CueAnchor", new Vector3(0f, 2.02f, 4.24f), 0.021f, TextAnchor.MiddleCenter);
        _statusText = CreateText("Template_Status", "Template_StatusAnchor", new Vector3(-1.8f, 1.68f, 4.24f), 0.016f, TextAnchor.UpperLeft);
        _feedbackText = CreateText("Template_Feedback", "Template_FeedbackAnchor", new Vector3(0f, 1.12f, 4.24f), 0.022f, TextAnchor.MiddleCenter);
        _titleText.color = Color.white;
        _cueText.color = new Color(0.92f, 0.96f, 1f);
        _statusText.color = new Color(0.72f, 0.82f, 0.92f);
        _feedbackText.color = new Color(1f, 0.88f, 0.35f);
    }

    TextMesh CreateText(string name, string anchorName, Vector3 position, float size, TextAnchor anchor)
    {
        Transform anchorTransform = FindOrCreateAnchor(anchorName, position);
        GameObject textObject = new GameObject(name);
        textObject.transform.SetParent(anchorTransform, false);
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.fontSize = 42;
        text.characterSize = size;
        text.anchor = anchor;
        text.alignment = anchor == TextAnchor.UpperLeft ? TextAlignment.Left : TextAlignment.Center;
        return text;
    }

    Transform FindOrCreateAnchor(string name, Vector3 position)
    {
        GameObject anchor = GameObject.Find(name);
        if (anchor == null)
        {
            anchor = new GameObject(name);
            anchor.transform.position = position;
        }
        return anchor.transform;
    }

    void EnsureCamera()
    {
        _mainCamera = Camera.main;
        if (_mainCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            _mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }

        _mainCamera.transform.position = new Vector3(0f, 1.55f, -0.15f);
        _mainCamera.transform.rotation = Quaternion.identity;
        _mainCamera.nearClipPlane = 0.03f;
        _mainCamera.farClipPlane = 100f;
        _mainCamera.clearFlags = CameraClearFlags.SolidColor;
        _mainCamera.backgroundColor = new Color(0.035f, 0.04f, 0.05f);
    }

    void AddTrackedPoseDriverIfAvailable(GameObject cameraObject)
    {
        TryAddComponentByName(cameraObject, "UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
        TryAddComponentByName(cameraObject, "UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
    }

    void TryAddComponentByName(GameObject target, string assemblyQualifiedName)
    {
        Type type = Type.GetType(assemblyQualifiedName);
        if (type == null || target.GetComponent(type) != null)
            return;
        try
        {
            target.AddComponent(type);
        }
        catch
        {
            // The desktop fallback remains usable when package-specific XR tracking is unavailable.
        }
    }
}
