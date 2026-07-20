using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public sealed class ExperimentSetupController : MonoBehaviour
{
    const int ResearcherUiLayer = 5;

    [SerializeField] ExperimentSceneCatalog sceneCatalog;

    public void SetSceneCatalog(ExperimentSceneCatalog catalog)
    {
        sceneCatalog = catalog;
    }

    readonly Color _pageColor = new Color32(239, 243, 246, 255);
    readonly Color _surfaceColor = new Color32(255, 255, 255, 255);
    readonly Color _inkColor = new Color32(28, 39, 49, 255);
    readonly Color _mutedColor = new Color32(91, 106, 119, 255);
    readonly Color _lineColor = new Color32(207, 216, 223, 255);
    readonly Color _accentColor = new Color32(23, 118, 111, 255);
    readonly Color _accentPressedColor = new Color32(15, 89, 84, 255);
    readonly Color _errorColor = new Color32(172, 53, 53, 255);
    readonly Color _successColor = new Color32(30, 121, 77, 255);

    readonly List<ExperimentSceneCatalog.SceneEntry> _availableScenes =
        new List<ExperimentSceneCatalog.SceneEntry>();

    Font _font;
    Dropdown _sceneDropdown;
    InputField _participantInput;
    InputField _outputInput;
    Text _previewText;
    Text _statusText;
    Button _startButton;
    Camera _headsetCamera;
    Camera _researcherUiCamera;
    bool _isLoading;
    bool _initialized;

    void Awake()
    {
        InitializeInterface();
    }

    public void InitializeForEditorValidation()
    {
        InitializeInterface();
    }

    void InitializeInterface()
    {
        if (_initialized)
            return;
        _initialized = true;

        ExperimentRunContext.ClearActiveRun();
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        _font = CreateInterfaceFont();

        ResolveCatalog();
        EnsureCamera();
        EnsureResearcherUiCamera();
        EnsureEventSystem();
        BuildInterface();
        RestoreLastSetup();
        RefreshState();
    }

    void ResolveCatalog()
    {
        if (sceneCatalog == null)
            sceneCatalog = Resources.Load<ExperimentSceneCatalog>("Experiment/ExperimentSceneCatalog");

        _availableScenes.Clear();
        if (sceneCatalog != null)
            _availableScenes.AddRange(sceneCatalog.GetEnabledScenes());

        if (_availableScenes.Count > 0)
            return;

        _availableScenes.Add(new ExperimentSceneCatalog.SceneEntry(
            "main", "Main questionnaire", "MainScene", "MainScene_Data"));
        _availableScenes.Add(new ExperimentSceneCatalog.SceneEntry(
            "workload", "Workload probe", "XRWorkloadProbeScene", "XRWorkloadProbe_Data"));
        _availableScenes.Add(new ExperimentSceneCatalog.SceneEntry(
            "questionnaire-read", "Questionnaire with Read stage", "XRQuestionnaireReadScene", "XRQuestionnaireRead_Data"));
        _availableScenes.Add(new ExperimentSceneCatalog.SceneEntry(
            "paxsm-comparison", "PAXSM comparison study", "PAXSMComparisonScene", "PAXSMComparison_Data"));
    }

    void EnsureCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            var cameraObject = new GameObject("Setup Camera", typeof(Camera), typeof(AudioListener));
            camera = cameraObject.GetComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = Vector3.up * 1.65f;
        }

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.24f, 0.58f, 0.82f, 1f);
        camera.orthographic = false;
        camera.fieldOfView = 65f;
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 100f;
        camera.cullingMask &= ~(1 << ResearcherUiLayer);
        _headsetCamera = camera;
        ExperimentSetupHeadsetWaitingView.Ensure(camera);
    }

    void EnsureResearcherUiCamera()
    {
        GameObject cameraObject = GameObject.Find("Researcher UI Camera");
        if (cameraObject == null)
            cameraObject = new GameObject("Researcher UI Camera", typeof(Camera));

        cameraObject.tag = "Untagged";
        cameraObject.layer = ResearcherUiLayer;
        cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, -10f), Quaternion.identity);

        _researcherUiCamera = cameraObject.GetComponent<Camera>();
        _researcherUiCamera.clearFlags = CameraClearFlags.Depth;
        _researcherUiCamera.cullingMask = 1 << ResearcherUiLayer;
        _researcherUiCamera.depth = 100f;
        _researcherUiCamera.orthographic = true;
        _researcherUiCamera.orthographicSize = 5f;
        _researcherUiCamera.nearClipPlane = 0.1f;
        _researcherUiCamera.farClipPlane = 20f;
        _researcherUiCamera.stereoTargetEye = StereoTargetEyeMask.None;
        _researcherUiCamera.targetDisplay = 0;

        UniversalAdditionalCameraData cameraData =
            cameraObject.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null)
            cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
        cameraData.allowXRRendering = false;
        cameraData.renderPostProcessing = false;
    }

    void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
        InputSystemUIInputModule module = eventSystemObject.AddComponent<InputSystemUIInputModule>();
        module.AssignDefaultActions();
#else
        eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
    }

    void BuildInterface()
    {
        var canvasObject = new GameObject(
            "Researcher Setup Canvas",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = _researcherUiCamera;
        canvas.planeDistance = 1f;
        canvas.targetDisplay = 0;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1440f, 900f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        Image background = CreateImage("Background", canvasRect, _pageColor);
        Stretch(background.rectTransform);

        Image topBar = CreateImage("Top Bar", canvasRect, _surfaceColor);
        AnchorTop(topBar.rectTransform, 0f, 0f, 0f, 78f, stretchWidth: true);

        Image accentLine = CreateImage("Accent", topBar.rectTransform, _accentColor);
        AnchorTop(accentLine.rectTransform, 0f, 0f, 0f, 5f, stretchWidth: true);

        Text title = CreateText("Title", topBar.rectTransform, "CARE-XR Experiment Setup", 27, FontStyle.Bold, _inkColor);
        AnchorTop(title.rectTransform, 54f, -21f, 560f, 38f, stretchWidth: false, leftAligned: true);

        Text mode = CreateText("Mode", topBar.rectTransform, "PRE-RUN CONFIGURATION", 13, FontStyle.Bold, _accentColor);
        AnchorTop(mode.rectTransform, -54f, -27f, 280f, 26f, stretchWidth: false, leftAligned: false);
        mode.alignment = TextAnchor.MiddleRight;

        Image formBorder = CreateImage("Configuration Border", canvasRect, _lineColor);
        AnchorTopCenter(formBorder.rectTransform, 0f, -112f, 822f, 674f);

        Image form = CreateImage("Configuration Form", formBorder.rectTransform, _surfaceColor);
        Stretch(form.rectTransform, 1f, 1f, 1f, 1f);

        Text formTitle = CreateText("Form Title", form.rectTransform, "Run configuration", 22, FontStyle.Bold, _inkColor);
        AnchorTopCenter(formTitle.rectTransform, 0f, -26f, 708f, 34f);
        formTitle.alignment = TextAnchor.MiddleLeft;

        Image divider = CreateImage("Divider", form.rectTransform, _lineColor);
        AnchorTopCenter(divider.rectTransform, 0f, -70f, 708f, 1f);

        Text sceneLabel = CreateText("Scene Label", form.rectTransform, "Experiment scene", 15, FontStyle.Bold, _inkColor);
        AnchorTopCenter(sceneLabel.rectTransform, 0f, -91f, 708f, 24f);
        sceneLabel.alignment = TextAnchor.MiddleLeft;

        _sceneDropdown = CreateDropdown("Scene Dropdown", form.rectTransform);
        AnchorTopCenter(_sceneDropdown.GetComponent<RectTransform>(), 0f, -119f, 708f, 48f);

        Text participantLabel = CreateText("Participant Label", form.rectTransform, "Participant ID", 15, FontStyle.Bold, _inkColor);
        AnchorTopCenter(participantLabel.rectTransform, 0f, -187f, 708f, 24f);
        participantLabel.alignment = TextAnchor.MiddleLeft;

        _participantInput = CreateInputField("Participant Input", form.rectTransform, "e.g. P001");
        AnchorTopCenter(_participantInput.GetComponent<RectTransform>(), 0f, -215f, 708f, 48f);
        _participantInput.characterLimit = 48;

        Text outputLabel = CreateText("Output Label", form.rectTransform, "Data output location", 15, FontStyle.Bold, _inkColor);
        AnchorTopCenter(outputLabel.rectTransform, 0f, -283f, 708f, 24f);
        outputLabel.alignment = TextAnchor.MiddleLeft;

        _outputInput = CreateInputField("Output Input", form.rectTransform, ExperimentRunContext.GetDefaultOutputRoot());
        AnchorTopCenter(_outputInput.GetComponent<RectTransform>(), 0f, -311f, 708f, 48f);

        Button defaultButton = CreateButton("Default Location", form.rectTransform, "Use default", secondary: true);
        AnchorTopCenter(defaultButton.GetComponent<RectTransform>(), -269f, -371f, 170f, 38f);
        defaultButton.onClick.AddListener(UseDefaultOutputLocation);

#if UNITY_EDITOR
        string outputActionLabel = "Choose folder";
#else
        string outputActionLabel = "Check location";
#endif
        Button outputActionButton = CreateButton("Output Action", form.rectTransform, outputActionLabel, secondary: true);
        AnchorTopCenter(outputActionButton.GetComponent<RectTransform>(), -81f, -371f, 190f, 38f);
        outputActionButton.onClick.AddListener(HandleOutputLocationAction);

        Text previewLabel = CreateText("Preview Label", form.rectTransform, "Run folder preview", 13, FontStyle.Bold, _mutedColor);
        AnchorTopCenter(previewLabel.rectTransform, 0f, -429f, 708f, 22f);
        previewLabel.alignment = TextAnchor.MiddleLeft;

        Image previewBackground = CreateImage("Preview Background", form.rectTransform, new Color32(244, 247, 249, 255));
        AnchorTopCenter(previewBackground.rectTransform, 0f, -454f, 708f, 58f);

        _previewText = CreateText("Preview Text", previewBackground.rectTransform, "", 13, FontStyle.Normal, _mutedColor);
        Stretch(_previewText.rectTransform, 13f, 13f, 7f, 7f);
        _previewText.alignment = TextAnchor.MiddleLeft;
        _previewText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _previewText.verticalOverflow = VerticalWrapMode.Truncate;
        _previewText.resizeTextForBestFit = true;
        _previewText.resizeTextMinSize = 10;
        _previewText.resizeTextMaxSize = 13;

        _statusText = CreateText("Status", form.rectTransform, "", 14, FontStyle.Normal, _mutedColor);
        AnchorTopCenter(_statusText.rectTransform, -164f, -531f, 380f, 50f);
        _statusText.alignment = TextAnchor.MiddleLeft;
        _statusText.horizontalOverflow = HorizontalWrapMode.Wrap;
        _statusText.verticalOverflow = VerticalWrapMode.Truncate;

        _startButton = CreateButton("Start Experiment", form.rectTransform, "Start experiment", secondary: false);
        AnchorTopCenter(_startButton.GetComponent<RectTransform>(), 222f, -536f, 264f, 52f);
        _startButton.onClick.AddListener(StartExperiment);

        _sceneDropdown.ClearOptions();
        var sceneNames = new List<string>(_availableScenes.Count);
        for (int i = 0; i < _availableScenes.Count; i++)
            sceneNames.Add(_availableScenes[i].displayName);
        _sceneDropdown.AddOptions(sceneNames);

        _sceneDropdown.onValueChanged.AddListener(_ => RefreshState());
        _participantInput.onValueChanged.AddListener(_ => RefreshState());
        _outputInput.onValueChanged.AddListener(_ => RefreshState());

        SetLayerRecursively(canvasObject, ResearcherUiLayer);
    }

    static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null)
            return;

        root.layer = layer;
        Transform rootTransform = root.transform;
        for (int i = 0; i < rootTransform.childCount; i++)
            SetLayerRecursively(rootTransform.GetChild(i).gameObject, layer);
    }

    void RestoreLastSetup()
    {
        _participantInput.text = "";
        _outputInput.text = ExperimentRunContext.GetDefaultOutputRoot();

        if (!ExperimentRunContext.TryLoadLastSetup(out ExperimentRunContext.LastSetup setup))
            return;

        _outputInput.text = ExperimentRunContext.MigrateSavedOutputRoot(setup.outputRoot);

        for (int i = 0; i < _availableScenes.Count; i++)
        {
            if (!string.Equals(_availableScenes[i].id, setup.selectedSceneId, StringComparison.Ordinal))
                continue;
            _sceneDropdown.SetValueWithoutNotify(i);
            break;
        }
    }

    void RefreshState()
    {
        ExperimentSceneCatalog.SceneEntry scene = SelectedScene();
        _previewText.text = ExperimentRunContext.BuildPreviewPath(
            _outputInput != null ? _outputInput.text : "",
            _participantInput != null ? _participantInput.text : "",
            scene);

        bool participantValid = ExperimentRunContext.ValidateParticipantId(
            _participantInput != null ? _participantInput.text : "",
            out _,
            out string participantError);
        bool sceneValid = scene != null && !string.IsNullOrWhiteSpace(scene.sceneName);
        bool outputPresent = _outputInput != null && !string.IsNullOrWhiteSpace(_outputInput.text);
        bool valid = participantValid && sceneValid && outputPresent && !_isLoading;

        if (_startButton != null)
            _startButton.interactable = valid;

        if (_isLoading)
            return;
        if (!sceneValid)
            SetStatus("No experiment scene is available.", _errorColor);
        else if (!participantValid && !string.IsNullOrWhiteSpace(_participantInput.text))
            SetStatus(participantError, _errorColor);
        else if (!outputPresent)
            SetStatus("Data output location is required.", _errorColor);
        else if (participantValid)
            SetStatus("Configuration ready.", _successColor);
        else
            SetStatus("Enter the participant ID.", _mutedColor);
    }

    void UseDefaultOutputLocation()
    {
        _outputInput.text = ExperimentRunContext.GetDefaultOutputRoot();
        RefreshState();
    }

    void HandleOutputLocationAction()
    {
#if UNITY_EDITOR
        string current = string.IsNullOrWhiteSpace(_outputInput.text)
            ? ExperimentRunContext.GetDefaultOutputRoot()
            : _outputInput.text.Trim();
        string selected = UnityEditor.EditorUtility.OpenFolderPanel("Select data output folder", current, "");
        if (!string.IsNullOrWhiteSpace(selected))
            _outputInput.text = selected;
#else
        if (ExperimentRunContext.ValidateOutputRoot(_outputInput.text, out string normalized, out string error))
        {
            _outputInput.text = normalized;
            SetStatus("Output location is writable.", _successColor);
        }
        else
        {
            SetStatus(error, _errorColor);
        }
#endif
        RefreshState();
    }

    void StartExperiment()
    {
        if (_isLoading)
            return;

        ExperimentSceneCatalog.SceneEntry scene = SelectedScene();
        if (scene == null)
        {
            SetStatus("Select an experiment scene.", _errorColor);
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(scene.sceneName))
        {
            SetStatus($"Scene '{scene.sceneName}' is not included in Build Settings.", _errorColor);
            return;
        }

        if (!ExperimentRunContext.Configure(scene, _participantInput.text, _outputInput.text, out string error))
        {
            SetStatus(error, _errorColor);
            RefreshState();
            return;
        }

        _isLoading = true;
        _sceneDropdown.interactable = false;
        _participantInput.interactable = false;
        _outputInput.interactable = false;
        _startButton.interactable = false;
        SetStatus($"Run {ExperimentRunContext.RunId} created. Loading...", _accentColor);
        StartCoroutine(LoadSelectedScene(scene.sceneName));
    }

    IEnumerator LoadSelectedScene(string sceneName)
    {
        AsyncOperation operation = null;
        try
        {
            operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }
        catch (Exception exception)
        {
            SetStatus($"Could not load scene: {exception.Message}", _errorColor);
        }

        if (operation == null)
        {
            _isLoading = false;
            RefreshState();
            yield break;
        }

        while (!operation.isDone)
            yield return null;
    }

    ExperimentSceneCatalog.SceneEntry SelectedScene()
    {
        if (_availableScenes.Count == 0 || _sceneDropdown == null)
            return null;
        int index = Mathf.Clamp(_sceneDropdown.value, 0, _availableScenes.Count - 1);
        return _availableScenes[index];
    }

    void SetStatus(string message, Color color)
    {
        if (_statusText == null)
            return;
        _statusText.text = message ?? "";
        _statusText.color = color;
    }

    Font CreateInterfaceFont()
    {
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    Image CreateImage(string name, Transform parent, Color color)
    {
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        gameObject.transform.SetParent(parent, false);
        Image image = gameObject.GetComponent<Image>();
        image.color = color;
        return image;
    }

    Text CreateText(string name, Transform parent, string value, int fontSize, FontStyle style, Color color)
    {
        var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        gameObject.transform.SetParent(parent, false);
        Text text = gameObject.GetComponent<Text>();
        text.font = _font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    InputField CreateInputField(string name, Transform parent, string placeholderValue)
    {
        Image background = CreateImage(name, parent, new Color32(250, 252, 253, 255));
        background.raycastTarget = true;

        Text valueText = CreateText("Text", background.transform, "", 16, FontStyle.Normal, _inkColor);
        Stretch(valueText.rectTransform, 14f, 14f, 6f, 6f);
        valueText.supportRichText = false;

        Text placeholder = CreateText("Placeholder", background.transform, placeholderValue, 16, FontStyle.Italic, _mutedColor);
        Stretch(placeholder.rectTransform, 14f, 14f, 6f, 6f);

        InputField input = background.gameObject.AddComponent<InputField>();
        input.targetGraphic = background;
        input.textComponent = valueText;
        input.placeholder = placeholder;
        input.lineType = InputField.LineType.SingleLine;
        input.contentType = InputField.ContentType.Standard;
        input.caretColor = _accentColor;
        input.selectionColor = new Color(_accentColor.r, _accentColor.g, _accentColor.b, 0.25f);

        ColorBlock colors = input.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(242, 249, 248, 255);
        colors.selectedColor = new Color32(235, 246, 245, 255);
        colors.disabledColor = new Color32(232, 236, 239, 255);
        input.colors = colors;
        return input;
    }

    Dropdown CreateDropdown(string name, Transform parent)
    {
        GameObject dropdownObject = DefaultControls.CreateDropdown(new DefaultControls.Resources());
        dropdownObject.name = name;
        dropdownObject.transform.SetParent(parent, false);
        Dropdown dropdown = dropdownObject.GetComponent<Dropdown>();

        Image background = dropdownObject.GetComponent<Image>();
        if (background != null)
            background.color = new Color32(250, 252, 253, 255);

        Text[] texts = dropdownObject.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            texts[i].font = _font;
            texts[i].fontSize = 16;
            texts[i].color = _inkColor;
        }

        if (dropdown.captionText != null)
        {
            dropdown.captionText.alignment = TextAnchor.MiddleLeft;
            RectTransform captionRect = dropdown.captionText.rectTransform;
            captionRect.offsetMin = new Vector2(14f, 2f);
            captionRect.offsetMax = new Vector2(-42f, -2f);
        }

        Transform defaultArrow = dropdownObject.transform.Find("Arrow");
        if (defaultArrow != null)
        {
            Image defaultArrowImage = defaultArrow.GetComponent<Image>();
            if (defaultArrowImage != null)
                defaultArrowImage.enabled = false;
        }

        Text chevron = CreateText("Chevron", dropdownObject.transform, "v", 15, FontStyle.Bold, _mutedColor);
        RectTransform chevronRect = chevron.rectTransform;
        chevronRect.anchorMin = new Vector2(1f, 0f);
        chevronRect.anchorMax = new Vector2(1f, 1f);
        chevronRect.pivot = new Vector2(1f, 0.5f);
        chevronRect.anchoredPosition = new Vector2(-13f, 0f);
        chevronRect.sizeDelta = new Vector2(24f, 0f);
        chevron.alignment = TextAnchor.MiddleCenter;
        chevron.raycastTarget = false;

        if (dropdown.template != null)
        {
            dropdown.template.sizeDelta = new Vector2(0f, 180f);
            Image templateImage = dropdown.template.GetComponent<Image>();
            if (templateImage != null)
                templateImage.color = _surfaceColor;
        }

        ColorBlock colors = dropdown.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(242, 249, 248, 255);
        colors.selectedColor = new Color32(235, 246, 245, 255);
        colors.pressedColor = new Color32(225, 241, 239, 255);
        dropdown.colors = colors;
        return dropdown;
    }

    Button CreateButton(string name, Transform parent, string label, bool secondary)
    {
        Color backgroundColor = secondary ? new Color32(245, 248, 250, 255) : _accentColor;
        Color labelColor = secondary ? _inkColor : Color.white;
        Image background = CreateImage(name, parent, backgroundColor);
        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;

        ColorBlock colors = button.colors;
        colors.normalColor = backgroundColor;
        colors.highlightedColor = secondary ? new Color32(232, 239, 243, 255) : new Color32(29, 139, 131, 255);
        colors.pressedColor = secondary ? new Color32(218, 228, 234, 255) : _accentPressedColor;
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color32(186, 196, 202, 255);
        button.colors = colors;

        Text text = CreateText("Label", background.transform, label, secondary ? 14 : 16, FontStyle.Bold, labelColor);
        Stretch(text.rectTransform, 8f, 8f, 4f, 4f);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    static void AnchorTopCenter(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(x, y);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void AnchorTop(
        RectTransform rect,
        float horizontalInset,
        float y,
        float width,
        float height,
        bool stretchWidth,
        bool leftAligned = true)
    {
        if (stretchWidth)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(-horizontalInset * 2f, height);
            return;
        }

        float anchorX = leftAligned ? 0f : 1f;
        float pivotX = leftAligned ? 0f : 1f;
        rect.anchorMin = new Vector2(anchorX, 1f);
        rect.anchorMax = new Vector2(anchorX, 1f);
        rect.pivot = new Vector2(pivotX, 1f);
        rect.anchoredPosition = new Vector2(horizontalInset, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
