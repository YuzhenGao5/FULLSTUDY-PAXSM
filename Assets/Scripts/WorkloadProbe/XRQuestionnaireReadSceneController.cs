using System.Collections;
using UnityEngine;

public sealed class XRQuestionnaireReadSceneController : XRWorkloadProbeSceneController
{
    [Header("Read Scene Head-relative Layout")]
    [Range(0f, 1.5f)] public float headPoseSettleSeconds = 0.35f;
    [Range(0.3f, 0.8f)] public float readSceneKnobForwardOffset = 0.50f;
    [Range(-0.2f, 0.25f)] public float readSceneKnobRightOffset = 0.10f;
    [Range(0.25f, 0.65f)] public float readSceneKnobBelowEyeOffset = 0.26f;
    [Range(0.55f, 1.0f)] public float readSceneKnobMinimumHeight = 0.65f;
    [Range(1.1f, 1.6f)] public float readSceneKnobMaximumHeight = 1.35f;

    [Header("First-grab Guidance")]
    public bool showFirstGrabGuidance = true;
    [Range(0.12f, 0.25f)] public float grabGuideOuterOffset = 0.18f;
    [Range(0.055f, 0.11f)] public float grabGuideInnerOffset = 0.075f;

    [Header("Animated Controller Tutorial")]
    public bool showControllerTutorial = true;
    public GameObject tutorialRightControllerPrefab;
    public Vector3 tutorialControllerTablePosition = new Vector3(-0.45f, 0.76f, 1.45f);
    public Vector3 tutorialControllerEuler = new Vector3(24f, 162f, -24f);
    [Range(0.005f, 0.05f)] public float tutorialControllerTableClearance = 0.015f;
    [Range(1.5f, 4.5f)] public float tutorialControllerScale = 2.8f;
    [Range(2.5f, 7f)] public float tutorialCycleSeconds = 4.6f;

    Transform _questionnaireRoot;

    GameObject _grabGuideRoot;
    LineRenderer _leftGrabArrow;
    LineRenderer _rightGrabArrow;
    TextMesh _grabGuideText;
    Material _grabGuideMaterial;
    bool _firstGrabCompleted;

    GameObject _controllerTutorialRoot;
    Transform _controllerTutorialPivot;
    Vector3 _controllerTutorialRestPosition;
    Quaternion _controllerTutorialRestRotation;
    Transform _tutorialButtonB;
    Transform _tutorialTrigger;
    Vector3 _tutorialButtonBRestPosition;
    Vector3 _tutorialButtonBRestScale;
    Quaternion _tutorialTriggerRestRotation;
    Vector3 _tutorialTriggerRestScale;
    TextMesh _tutorialInstructionText;
    TextMesh _tutorialButtonBText;
    TextMesh _tutorialTriggerText;
    LineRenderer _tutorialButtonBLine;
    LineRenderer _tutorialTriggerLine;
    Material _tutorialButtonBMaterial;
    Material _tutorialTriggerMaterial;
    float _tutorialStartTime;

    protected override void Awake()
    {
        questionnaireOnlyMode = true;
        requireReadAcknowledgement = true;
        collectQuestionnaireBetweenBlocks = true;
        collectConfidenceAfterEachItem = true;
        recordQuestionnairePersonalSpeed = true;

        // These offsets apply only to the standalone read scene.
        questionnaireUseHeadRelativePlacement = true;
        questionnaireForwardOffset = readSceneKnobForwardOffset;
        questionnaireRightOffset = readSceneKnobRightOffset;
        questionnaireBelowEyeOffset = readSceneKnobBelowEyeOffset;
        questionnaireMinimumHeight = readSceneKnobMinimumHeight;
        questionnaireMaximumHeight = readSceneKnobMaximumHeight;

        if (string.IsNullOrWhiteSpace(conditionLabel))
            conditionLabel = "QuestionnaireRead";
        if (string.IsNullOrWhiteSpace(questionnaireOnlySessionId))
            questionnaireOnlySessionId = "standalone_questionnaire";
        if (string.IsNullOrWhiteSpace(outputFolderName) || outputFolderName == "XRWorkloadProbe_Data")
            outputFolderName = "XRQuestionnaireRead_Data";

        base.Awake();
    }

    protected override void Start()
    {
        StartCoroutine(StartAfterHeadPoseSettles());
    }

    protected override void Update()
    {
        base.Update();

        ResolveQuestionnaireRoot();
        UpdateFirstGrabGuidance();
    }

    IEnumerator StartAfterHeadPoseSettles()
    {
        yield return null;
        if (headPoseSettleSeconds > 0f)
            yield return new WaitForSecondsRealtime(headPoseSettleSeconds);

        // The base controller samples the settled HMD pose here and freezes the knob rig there.
        // Wall text keeps the scene's original fixed wall layout.
        base.Start();
    }

    void ResolveQuestionnaireRoot()
    {
        if (_questionnaireRoot != null)
            return;

        GameObject root = GameObject.Find("PAXSM_InterBlockQuestionnaire");
        if (root != null)
            _questionnaireRoot = root.transform;
    }

    void UpdateFirstGrabGuidance()
    {
        if (_questionnaireRoot == null || !_questionnaireRoot.gameObject.activeInHierarchy)
        {
            SetGrabGuideVisible(false);
            SetControllerTutorialVisible(false);
            return;
        }

        // The table-mounted controller remains visible throughout the questionnaire.
        UpdateControllerTutorial();

        if (!showFirstGrabGuidance || _firstGrabCompleted)
        {
            SetGrabGuideVisible(false);
            return;
        }

        KnobGrabByWrist grab = _questionnaireRoot.GetComponentInChildren<KnobGrabByWrist>(true);
        Transform panel = _questionnaireRoot.Find("PAXSM_KnobBackingPanel");
        if (grab == null || panel == null)
        {
            SetGrabGuideVisible(false);
            return;
        }

        if (grab.IsGrabbing)
        {
            _firstGrabCompleted = true;
            SetGrabGuideVisible(false);
            Debug.Log(
                "[Questionnaire Read] First knob grab completed; arrows hidden and table controller retained.",
                this);
            return;
        }

        EnsureGrabGuide();
        Transform knobCenter = grab.knobCenter != null ? grab.knobCenter : grab.transform;
        Quaternion panelRotation = panel.rotation;
        Vector3 panelFront = panelRotation * Vector3.back;
        _grabGuideRoot.transform.SetPositionAndRotation(
            knobCenter.position + panelFront * 0.082f,
            panelRotation);
        SetGrabGuideVisible(true);

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.5f);
        float width = Mathf.Lerp(0.005f, 0.009f, pulse);
        _leftGrabArrow.widthMultiplier = width;
        _rightGrabArrow.widthMultiplier = width;
        Color color = Color.Lerp(
            new Color(0.25f, 0.78f, 1f, 0.8f),
            new Color(1f, 0.86f, 0.2f, 1f),
            pulse);
        _leftGrabArrow.startColor = _leftGrabArrow.endColor = color;
        _rightGrabArrow.startColor = _rightGrabArrow.endColor = color;
        _grabGuideText.color = color;

    }

    void EnsureGrabGuide()
    {
        if (_grabGuideRoot != null)
            return;

        _grabGuideRoot = new GameObject("PAXSM_ReadSceneGrabGuide");
        _grabGuideRoot.transform.SetParent(_questionnaireRoot, true);

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        _grabGuideMaterial = new Material(shader);

        _leftGrabArrow = CreateArrow("GrabArrow_Left", pointsLeftToRight: true);
        _rightGrabArrow = CreateArrow("GrabArrow_Right", pointsLeftToRight: false);

        GameObject textObject = new GameObject("GrabGuide_Text");
        textObject.transform.SetParent(_grabGuideRoot.transform, false);
        textObject.transform.localPosition = new Vector3(0f, -0.115f, 0f);
        _grabGuideText = textObject.AddComponent<TextMesh>();
        _grabGuideText.text = "Move close, then hold B + Trigger together";
        _grabGuideText.fontSize = 42;
        _grabGuideText.characterSize = 0.0026f;
        _grabGuideText.anchor = TextAnchor.MiddleCenter;
        _grabGuideText.alignment = TextAlignment.Center;
    }

    LineRenderer CreateArrow(string objectName, bool pointsLeftToRight)
    {
        GameObject arrowObject = new GameObject(objectName);
        arrowObject.transform.SetParent(_grabGuideRoot.transform, false);
        LineRenderer line = arrowObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = false;
        line.positionCount = 5;
        line.sharedMaterial = _grabGuideMaterial;
        line.numCapVertices = 4;
        line.numCornerVertices = 2;
        line.alignment = LineAlignment.TransformZ;

        float outer = pointsLeftToRight ? -grabGuideOuterOffset : grabGuideOuterOffset;
        float inner = pointsLeftToRight ? -grabGuideInnerOffset : grabGuideInnerOffset;
        float headBack = pointsLeftToRight ? inner - 0.035f : inner + 0.035f;
        line.SetPosition(0, new Vector3(outer, 0f, 0f));
        line.SetPosition(1, new Vector3(inner, 0f, 0f));
        line.SetPosition(2, new Vector3(headBack, 0.026f, 0f));
        line.SetPosition(3, new Vector3(inner, 0f, 0f));
        line.SetPosition(4, new Vector3(headBack, -0.026f, 0f));
        return line;
    }

    void SetGrabGuideVisible(bool visible)
    {
        if (_grabGuideRoot != null && _grabGuideRoot.activeSelf != visible)
            _grabGuideRoot.SetActive(visible);
    }

    void UpdateControllerTutorial()
    {
        if (!showControllerTutorial || !EnsureControllerTutorial())
        {
            SetControllerTutorialVisible(false);
            return;
        }

        SetControllerTutorialVisible(true);

        float cycle = Mathf.Max(2.5f, tutorialCycleSeconds);
        float phase = Mathf.Repeat(Time.unscaledTime - _tutorialStartTime, cycle) / cycle;
        float press;

        if (phase < 0.38f)
        {
            press = 0f;
            _tutorialInstructionText.text = "Move your controller close to the knob";
        }
        else if (phase < 0.82f)
        {
            float pressIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 0.48f, phase));
            float pressOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.82f, phase));
            press = Mathf.Min(pressIn, pressOut);
            _tutorialInstructionText.text = "When close, hold B + Trigger together";
        }
        else
        {
            press = 0f;
            _tutorialInstructionText.text = "Move close, then hold both together";
        }

        _controllerTutorialPivot.position = _controllerTutorialRestPosition;
        _controllerTutorialPivot.rotation = _controllerTutorialRestRotation;
        _controllerTutorialPivot.localScale = Vector3.one * tutorialControllerScale;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f);
        float activePulse = press * Mathf.Lerp(0.72f, 1f, pulse);

        _tutorialButtonB.localPosition =
            _tutorialButtonBRestPosition + Vector3.down * (0.0024f * press);
        _tutorialButtonB.localScale = _tutorialButtonBRestScale * (1f + 0.18f * activePulse);
        _tutorialTrigger.localRotation =
            _tutorialTriggerRestRotation * Quaternion.Euler(-15f * press, 0f, 0f);
        _tutorialTrigger.localScale = _tutorialTriggerRestScale * (1f + 0.14f * activePulse);

        Color restingB = new Color(0.16f, 0.48f, 0.62f, 1f);
        Color activeB = new Color(0.18f, 0.9f, 1f, 1f);
        Color restingTrigger = new Color(0.58f, 0.42f, 0.08f, 1f);
        Color activeTrigger = new Color(1f, 0.82f, 0.12f, 1f);
        SetMaterialColor(_tutorialButtonBMaterial, Color.Lerp(restingB, activeB, activePulse));
        SetMaterialColor(_tutorialTriggerMaterial, Color.Lerp(restingTrigger, activeTrigger, activePulse));

        UpdateTutorialCallouts(activePulse);
    }

    bool EnsureControllerTutorial()
    {
        if (_controllerTutorialRoot != null)
            return true;
        if (tutorialRightControllerPrefab == null)
        {
            Debug.LogWarning(
                "[Questionnaire Read] Controller tutorial prefab is not assigned; tutorial skipped.",
                this);
            return false;
        }

        _controllerTutorialRoot = new GameObject("PAXSM_ControllerGrabTutorial");
        _controllerTutorialRoot.transform.SetParent(_questionnaireRoot, true);

        GameObject pivotObject = new GameObject("AnimatedControllerPivot");
        pivotObject.transform.SetParent(_controllerTutorialRoot.transform, true);
        _controllerTutorialPivot = pivotObject.transform;
        _controllerTutorialPivot.position = tutorialControllerTablePosition;
        _controllerTutorialPivot.rotation = Quaternion.Euler(tutorialControllerEuler);
        _controllerTutorialPivot.localScale = Vector3.one * tutorialControllerScale;

        GameObject model = Instantiate(tutorialRightControllerPrefab, _controllerTutorialPivot, false);
        model.name = "RightController_TutorialVisual";
        DisableTutorialInteraction(model);

        _tutorialButtonB = FindTutorialChild(model.transform, "Button_B");
        _tutorialTrigger = FindTutorialChild(model.transform, "Trigger");
        if (_tutorialButtonB == null || _tutorialTrigger == null)
        {
            Debug.LogWarning(
                "[Questionnaire Read] Tutorial controller is missing Button_B or Trigger meshes.",
                this);
            Destroy(_controllerTutorialRoot);
            _controllerTutorialRoot = null;
            return false;
        }

        _tutorialButtonBRestPosition = _tutorialButtonB.localPosition;
        _tutorialButtonBRestScale = _tutorialButtonB.localScale;
        _tutorialTriggerRestRotation = _tutorialTrigger.localRotation;
        _tutorialTriggerRestScale = _tutorialTrigger.localScale;

        LayoutControllerTutorialOnTable(model);

        _tutorialButtonBMaterial = CreateTutorialMaterial(
            "Tutorial_B_Highlight",
            new Color(0.18f, 0.9f, 1f, 1f));
        _tutorialTriggerMaterial = CreateTutorialMaterial(
            "Tutorial_Trigger_Highlight",
            new Color(1f, 0.82f, 0.12f, 1f));
        AssignTutorialMaterial(_tutorialButtonB, _tutorialButtonBMaterial);
        AssignTutorialMaterial(_tutorialTrigger, _tutorialTriggerMaterial);

        _tutorialInstructionText = CreateTutorialText(
            "Tutorial_Instruction",
            "Move close, then hold B + Trigger together",
            0.0075f,
            Color.white);
        _tutorialButtonBText = CreateTutorialText(
            "Tutorial_B_Label",
            "B",
            0.0065f,
            new Color(0.18f, 0.9f, 1f));
        _tutorialTriggerText = CreateTutorialText(
            "Tutorial_Trigger_Label",
            "TRIGGER",
            0.0055f,
            new Color(1f, 0.82f, 0.12f));

        _tutorialButtonBLine = CreateTutorialCalloutLine(
            "Tutorial_B_Callout",
            _tutorialButtonBMaterial);
        _tutorialTriggerLine = CreateTutorialCalloutLine(
            "Tutorial_Trigger_Callout",
            _tutorialTriggerMaterial);

        _tutorialStartTime = Time.unscaledTime;
        Debug.Log(
            "[Questionnaire Read] Animated controller tutorial created: approach knob, then hold B + Trigger together.",
            this);
        return true;
    }

    void LayoutControllerTutorialOnTable(GameObject model)
    {
        _controllerTutorialPivot.position = tutorialControllerTablePosition;
        _controllerTutorialPivot.rotation = Quaternion.Euler(tutorialControllerEuler);
        _controllerTutorialPivot.localScale = Vector3.one * tutorialControllerScale;

        GameObject table = GameObject.Find("Probe_TableTop");
        Renderer tableRenderer = table != null ? table.GetComponent<Renderer>() : null;
        Renderer[] modelRenderers = model.GetComponentsInChildren<Renderer>(true);
        if (tableRenderer != null && modelRenderers.Length > 0)
        {
            float tableSurfaceY = tableRenderer.bounds.max.y;
            _controllerTutorialPivot.position = new Vector3(
                tutorialControllerTablePosition.x,
                tableSurfaceY + 0.35f,
                tutorialControllerTablePosition.z);

            Bounds modelBounds = modelRenderers[0].bounds;
            for (int i = 1; i < modelRenderers.Length; i++)
                modelBounds.Encapsulate(modelRenderers[i].bounds);

            float targetBottomY = tableSurfaceY + tutorialControllerTableClearance;
            _controllerTutorialPivot.position +=
                Vector3.up * (targetBottomY - modelBounds.min.y);
        }

        _controllerTutorialRestPosition = _controllerTutorialPivot.position;
        _controllerTutorialRestRotation = _controllerTutorialPivot.rotation;
    }

    void DisableTutorialInteraction(GameObject model)
    {
        Behaviour[] behaviours = model.GetComponentsInChildren<Behaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;

        Collider[] colliders = model.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody[] rigidbodies = model.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < rigidbodies.Length; i++)
        {
            rigidbodies[i].isKinematic = true;
            rigidbodies[i].detectCollisions = false;
        }
    }

    Transform FindTutorialChild(Transform parent, string childName)
    {
        if (parent == null)
            return null;
        if (parent.name == childName)
            return parent;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform result = FindTutorialChild(parent.GetChild(i), childName);
            if (result != null)
                return result;
        }
        return null;
    }

    Material CreateTutorialMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader) { name = materialName };
        SetMaterialColor(material, color);
        return material;
    }

    void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        material.color = color;
    }

    void AssignTutorialMaterial(Transform target, Material material)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].sharedMaterial = material;
    }

    TextMesh CreateTutorialText(string objectName, string content, float size, Color color)
    {
        GameObject textObject = new GameObject(objectName);
        textObject.transform.SetParent(_controllerTutorialRoot.transform, true);
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.text = content;
        text.fontSize = 48;
        text.characterSize = size;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = color;
        return text;
    }

    LineRenderer CreateTutorialCalloutLine(string objectName, Material material)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(_controllerTutorialRoot.transform, true);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.004f;
        line.endWidth = 0.004f;
        line.numCapVertices = 4;
        line.sharedMaterial = material;
        return line;
    }

    void UpdateTutorialCallouts(float activePulse)
    {
        Camera head = Camera.main;
        Vector3 forward = head != null
            ? Vector3.ProjectOnPlane(head.transform.forward, Vector3.up)
            : Vector3.forward;
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Quaternion facing = Quaternion.LookRotation(forward, Vector3.up);

        Vector3 bLabelPosition = _tutorialButtonB.position - right * 0.105f + Vector3.up * 0.10f;
        Vector3 triggerLabelPosition = _tutorialTrigger.position + right * 0.14f + Vector3.up * 0.035f;
        _tutorialButtonBText.transform.SetPositionAndRotation(bLabelPosition, facing);
        _tutorialTriggerText.transform.SetPositionAndRotation(triggerLabelPosition, facing);
        _tutorialInstructionText.transform.SetPositionAndRotation(
            _controllerTutorialRestPosition + Vector3.up * 0.23f,
            facing);

        _tutorialButtonBLine.SetPosition(0, _tutorialButtonB.position);
        _tutorialButtonBLine.SetPosition(1, bLabelPosition - Vector3.up * 0.018f);
        _tutorialTriggerLine.SetPosition(0, _tutorialTrigger.position);
        _tutorialTriggerLine.SetPosition(1, triggerLabelPosition - Vector3.up * 0.018f);

        float labelScale = 1f + activePulse * 0.12f;
        _tutorialButtonBText.transform.localScale = Vector3.one * labelScale;
        _tutorialTriggerText.transform.localScale = Vector3.one * labelScale;
    }

    void SetControllerTutorialVisible(bool visible)
    {
        if (_controllerTutorialRoot != null && _controllerTutorialRoot.activeSelf != visible)
            _controllerTutorialRoot.SetActive(visible);
    }

    void OnDestroy()
    {
        if (_grabGuideMaterial != null)
            Destroy(_grabGuideMaterial);
        if (_tutorialButtonBMaterial != null)
            Destroy(_tutorialButtonBMaterial);
        if (_tutorialTriggerMaterial != null)
            Destroy(_tutorialTriggerMaterial);
    }
}
