using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class ExperimentSetupHeadsetWaitingView : MonoBehaviour
{
    const string WaitingSkyShaderName = "CARE-XR/Experiment Setup Waiting Sky";
    const float DefaultEyeHeight = 1.65f;

    [Header("Waiting Space")]
    [SerializeField, Min(10f)] float domeRadius = 55f;
    [SerializeField, Range(3f, 6f)] float platformSize = 4.2f;

    [Header("Tracked Controller Visuals")]
    [SerializeField] GameObject leftControllerVisualPrefab;
    [SerializeField] GameObject rightControllerVisualPrefab;

    readonly List<Material> _runtimeMaterials = new List<Material>();

    Camera _camera;
    GameObject _skyDome;
    GameObject _environmentRoot;
    GameObject _leftControllerAnchor;
    GameObject _rightControllerAnchor;
    Material _skyMaterial;
    float _enabledAt;
    bool _floorAnchored;

    public static ExperimentSetupHeadsetWaitingView Ensure(Camera targetCamera)
    {
        if (targetCamera == null)
            return null;

        ExperimentSetupHeadsetWaitingView waitingView =
            targetCamera.GetComponent<ExperimentSetupHeadsetWaitingView>();
        if (waitingView == null)
            waitingView = targetCamera.gameObject.AddComponent<ExperimentSetupHeadsetWaitingView>();

        waitingView.Configure(targetCamera);
        return waitingView;
    }

    public void SetControllerVisualPrefabs(GameObject leftPrefab, GameObject rightPrefab)
    {
        leftControllerVisualPrefab = leftPrefab;
        rightControllerVisualPrefab = rightPrefab;
    }

    void Awake()
    {
        Configure(GetComponent<Camera>());
    }

    void OnEnable()
    {
        _enabledAt = Time.unscaledTime;
        _floorAnchored = false;

        if (_camera == null)
            Configure(GetComponent<Camera>());

        EnsureSkyDome();
        EnsureWaitingSpace();
        EnsureControllerVisuals();
    }

    void LateUpdate()
    {
        if (_camera == null)
            return;

        EnsureSkyDome();
        EnsureWaitingSpace();
        EnsureControllerVisuals();

        if (_skyDome != null)
        {
            // The waiting world must remain fixed. Even translation-following sky
            // geometry can feel head-locked during room-scale movement.
            _skyDome.transform.position = Vector3.zero;
            _skyDome.transform.rotation = Quaternion.identity;
        }

        AnchorFloorToTrackingMode();
        UpdateControllerPose(_leftControllerAnchor, XRNode.LeftHand, true);
        UpdateControllerPose(_rightControllerAnchor, XRNode.RightHand, false);
    }

    void OnDestroy()
    {
        DestroyRuntimeObject(_leftControllerAnchor);
        DestroyRuntimeObject(_rightControllerAnchor);
        DestroyRuntimeObject(_environmentRoot);
        DestroyRuntimeObject(_skyDome);
        DestroyRuntimeObject(_skyMaterial);

        for (int i = 0; i < _runtimeMaterials.Count; i++)
            DestroyRuntimeObject(_runtimeMaterials[i]);
        _runtimeMaterials.Clear();
    }

    void Configure(Camera targetCamera)
    {
        _camera = targetCamera;
        if (_camera == null)
            return;

        _camera.clearFlags = CameraClearFlags.SolidColor;
        _camera.backgroundColor = new Color(0.24f, 0.58f, 0.82f, 1f);
        _camera.orthographic = false;
        _camera.fieldOfView = 65f;
        _camera.nearClipPlane = 0.03f;
        _camera.farClipPlane = Mathf.Max(_camera.farClipPlane, domeRadius + 10f);
        _camera.stereoTargetEye = StereoTargetEyeMask.Both;

        if (_camera.transform.parent == null && _camera.transform.position.sqrMagnitude < 0.001f)
            _camera.transform.position = Vector3.up * DefaultEyeHeight;

        VerifyTrackedPoseDriver(_camera.gameObject);
        TryAddComponentByName(
            _camera.gameObject,
            "UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime");
        EnsureSkyDome();
        EnsureWaitingSpace();
        EnsureControllerVisuals();
    }

    void EnsureSkyDome()
    {
        if (_skyDome != null)
            return;

        Shader shader = Shader.Find(WaitingSkyShaderName);
        if (shader == null)
        {
            Debug.LogWarning($"[ExperimentSetup] Waiting sky shader '{WaitingSkyShaderName}' was not found. Using the blue camera fallback.");
            return;
        }

        _skyDome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _skyDome.name = "Participant Waiting Sky";
        _skyDome.transform.position = Vector3.zero;
        _skyDome.transform.rotation = Quaternion.identity;
        _skyDome.transform.localScale = Vector3.one * (domeRadius * 2f);

        Collider collider = _skyDome.GetComponent<Collider>();
        if (collider != null)
            DestroyRuntimeObject(collider);

        MeshRenderer renderer = _skyDome.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        _skyMaterial = new Material(shader)
        {
            name = "Participant Waiting Sky (Runtime)",
            hideFlags = HideFlags.HideAndDontSave
        };
        renderer.sharedMaterial = _skyMaterial;
    }

    void EnsureWaitingSpace()
    {
        if (_environmentRoot != null)
            return;

        _environmentRoot = new GameObject("Participant Waiting Platform");

        Material slabMaterial = CreateLitMaterial(
            "Waiting Platform Slab",
            new Color(0.10f, 0.16f, 0.20f, 1f),
            0.15f,
            0.78f);
        Material floorMaterial = CreateLitMaterial(
            "Waiting Platform Surface",
            new Color(0.30f, 0.43f, 0.47f, 1f),
            0.05f,
            0.62f);
        Material insetMaterial = CreateLitMaterial(
            "Waiting Platform Inset",
            new Color(0.21f, 0.35f, 0.39f, 1f),
            0.08f,
            0.55f);
        Material gridMaterial = CreateLitMaterial(
            "Waiting Platform Grid",
            new Color(0.54f, 0.76f, 0.76f, 1f),
            0.08f,
            0.42f,
            new Color(0.02f, 0.08f, 0.08f));
        Material railMaterial = CreateLitMaterial(
            "Waiting Platform Boundary",
            new Color(0.12f, 0.23f, 0.27f, 1f),
            0.22f,
            0.48f);
        Material accentMaterial = CreateLitMaterial(
            "Waiting Platform Accent",
            new Color(0.43f, 0.83f, 0.78f, 1f),
            0.05f,
            0.36f,
            new Color(0.08f, 0.31f, 0.28f));

        CreatePrimitive(
            "Platform Base",
            PrimitiveType.Cube,
            _environmentRoot.transform,
            new Vector3(0f, -0.11f, 0f),
            new Vector3(platformSize, 0.22f, platformSize),
            slabMaterial,
            true);
        CreatePrimitive(
            "Platform Surface",
            PrimitiveType.Cube,
            _environmentRoot.transform,
            new Vector3(0f, 0.015f, 0f),
            new Vector3(platformSize - 0.16f, 0.05f, platformSize - 0.16f),
            floorMaterial,
            true);
        CreatePrimitive(
            "Standing Area",
            PrimitiveType.Cylinder,
            _environmentRoot.transform,
            new Vector3(0f, 0.052f, 0f),
            new Vector3(1.35f, 0.025f, 1.35f),
            insetMaterial,
            false);

        float gridSpan = platformSize - 0.34f;
        for (int i = -3; i <= 3; i++)
        {
            float offset = i * 0.5f;
            CreatePrimitive(
                $"Floor Grid X {i + 4}",
                PrimitiveType.Cube,
                _environmentRoot.transform,
                new Vector3(offset, 0.046f, 0f),
                new Vector3(0.012f, 0.006f, gridSpan),
                gridMaterial,
                false);
            CreatePrimitive(
                $"Floor Grid Z {i + 4}",
                PrimitiveType.Cube,
                _environmentRoot.transform,
                new Vector3(0f, 0.046f, offset),
                new Vector3(gridSpan, 0.006f, 0.012f),
                gridMaterial,
                false);
        }

        float edge = platformSize * 0.5f - 0.08f;
        CreateBoundary("Rear Boundary", new Vector3(0f, 0.28f, edge), new Vector3(platformSize, 0.08f, 0.08f), railMaterial, accentMaterial);
        CreateBoundary("Left Boundary", new Vector3(-edge, 0.28f, 0f), new Vector3(0.08f, 0.08f, platformSize), railMaterial, accentMaterial);
        CreateBoundary("Right Boundary", new Vector3(edge, 0.28f, 0f), new Vector3(0.08f, 0.08f, platformSize), railMaterial, accentMaterial);
        CreateBoundary("Front Boundary", new Vector3(0f, 0.10f, -edge), new Vector3(platformSize, 0.08f, 0.08f), railMaterial, accentMaterial);

        CreatePrimitive(
            "Rear Handrail",
            PrimitiveType.Cube,
            _environmentRoot.transform,
            new Vector3(0f, 0.88f, edge),
            new Vector3(platformSize, 0.065f, 0.065f),
            railMaterial,
            false);
        CreatePrimitive(
            "Left Handrail",
            PrimitiveType.Cube,
            _environmentRoot.transform,
            new Vector3(-edge, 0.88f, 0f),
            new Vector3(0.065f, 0.065f, platformSize),
            railMaterial,
            false);
        CreatePrimitive(
            "Right Handrail",
            PrimitiveType.Cube,
            _environmentRoot.transform,
            new Vector3(edge, 0.88f, 0f),
            new Vector3(0.065f, 0.065f, platformSize),
            railMaterial,
            false);

        Vector3[] corners =
        {
            new Vector3(-edge, 0.34f, -edge),
            new Vector3(edge, 0.34f, -edge),
            new Vector3(-edge, 0.34f, edge),
            new Vector3(edge, 0.34f, edge)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            CreatePrimitive(
                $"Boundary Post {i + 1}",
                PrimitiveType.Cylinder,
                _environmentRoot.transform,
                new Vector3(corners[i].x, 0.48f, corners[i].z),
                new Vector3(0.055f, 0.48f, 0.055f),
                railMaterial,
                false);
            CreatePrimitive(
                $"Boundary Light {i + 1}",
                PrimitiveType.Sphere,
                _environmentRoot.transform,
                new Vector3(corners[i].x, 1f, corners[i].z),
                Vector3.one * 0.12f,
                accentMaterial,
                false);
        }

        EnsureLighting();
    }

    void CreateBoundary(
        string name,
        Vector3 position,
        Vector3 scale,
        Material railMaterial,
        Material accentMaterial)
    {
        CreatePrimitive(name, PrimitiveType.Cube, _environmentRoot.transform, position, scale, railMaterial, false);

        Vector3 accentPosition = position + Vector3.up * 0.055f;
        Vector3 accentScale = scale;
        accentScale.y = 0.012f;
        if (accentScale.x > accentScale.z)
            accentScale.x -= 0.16f;
        else
            accentScale.z -= 0.16f;
        CreatePrimitive(name + " Light", PrimitiveType.Cube, _environmentRoot.transform, accentPosition, accentScale, accentMaterial, false);
    }

    void EnsureLighting()
    {
        var lightObject = new GameObject("Waiting Space Sun", typeof(Light));
        lightObject.transform.SetParent(_environmentRoot.transform, false);
        lightObject.transform.rotation = Quaternion.Euler(42f, -32f, 0f);

        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.95f, 0.86f, 1f);
        light.intensity = 1.05f;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.55f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.44f, 0.64f, 0.78f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.31f, 0.43f, 0.48f, 1f);
        RenderSettings.ambientGroundColor = new Color(0.11f, 0.15f, 0.17f, 1f);
    }

    void EnsureControllerVisuals()
    {
        if (_leftControllerAnchor == null)
            _leftControllerAnchor = CreateControllerVisual("Tracked Left Controller", leftControllerVisualPrefab);
        if (_rightControllerAnchor == null)
            _rightControllerAnchor = CreateControllerVisual("Tracked Right Controller", rightControllerVisualPrefab);
    }

    GameObject CreateControllerVisual(string name, GameObject visualPrefab)
    {
        var anchor = new GameObject(name);
        if (visualPrefab == null)
        {
            anchor.SetActive(false);
            return anchor;
        }

        GameObject visual = Instantiate(visualPrefab, anchor.transform, false);
        visual.name = visualPrefab.name + " Visual";

        MonoBehaviour[] behaviours = visual.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
            behaviours[i].enabled = false;

        Collider[] colliders = visual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        return anchor;
    }

    void UpdateControllerPose(GameObject anchor, XRNode node, bool isLeft)
    {
        if (anchor == null)
            return;

        InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        Vector3 localPosition = default;
        Quaternion localRotation = Quaternion.identity;
        bool hasPosition = device.isValid &&
            device.TryGetFeatureValue(CommonUsages.devicePosition, out localPosition);
        bool hasRotation = device.isValid &&
            device.TryGetFeatureValue(CommonUsages.deviceRotation, out localRotation);

        if (hasPosition && hasRotation)
        {
            SetTrackingSpacePose(anchor.transform, localPosition, localRotation);
            if (!anchor.activeSelf)
                anchor.SetActive(true);
            return;
        }

#if UNITY_EDITOR
        Vector3 previewPosition = new Vector3(isLeft ? -0.24f : 0.24f, 1.16f, 0.52f);
        Quaternion previewRotation = Quaternion.Euler(35f, isLeft ? 15f : -15f, isLeft ? -10f : 10f);
        SetTrackingSpacePose(anchor.transform, previewPosition, previewRotation);
        if (!anchor.activeSelf)
            anchor.SetActive(true);
#else
        if (anchor.activeSelf)
            anchor.SetActive(false);
#endif
    }

    void SetTrackingSpacePose(Transform target, Vector3 localPosition, Quaternion localRotation)
    {
        Transform trackingOrigin = _camera != null ? _camera.transform.parent : null;
        if (trackingOrigin == null)
        {
            target.SetPositionAndRotation(localPosition, localRotation);
            return;
        }

        target.SetPositionAndRotation(
            trackingOrigin.TransformPoint(localPosition),
            trackingOrigin.rotation * localRotation);
    }

    void AnchorFloorToTrackingMode()
    {
        if (_floorAnchored || _environmentRoot == null || _camera == null)
            return;
        if (Time.unscaledTime - _enabledAt < 0.5f)
            return;

        // Floor-origin tracking reports normal standing head height. Device-origin
        // tracking starts near zero, so provide a comfortable virtual floor below it.
        float trackedHeadHeight = _camera.transform.localPosition.y;
        float floorY = trackedHeadHeight >= 0.75f ? 0f : trackedHeadHeight - DefaultEyeHeight;
        Transform trackingOrigin = _camera.transform.parent;
        Vector3 localFloor = new Vector3(0f, floorY, 0f);
        _environmentRoot.transform.position = trackingOrigin != null
            ? trackingOrigin.TransformPoint(localFloor)
            : localFloor;
        _environmentRoot.transform.rotation = trackingOrigin != null
            ? trackingOrigin.rotation
            : Quaternion.identity;
        _floorAnchored = true;
    }

    Material CreateLitMaterial(
        string name,
        Color color,
        float metallic,
        float smoothness,
        Color emission = default)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        var material = new Material(shader)
        {
            name = name + " (Runtime)",
            color = color,
            hideFlags = HideFlags.HideAndDontSave
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", metallic);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", smoothness);
        if (emission.maxColorComponent > 0f)
        {
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", emission);
        }

        _runtimeMaterials.Add(material);
        return material;
    }

    static GameObject CreatePrimitive(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material,
        bool keepCollider)
    {
        GameObject value = GameObject.CreatePrimitive(primitiveType);
        value.name = name;
        value.transform.SetParent(parent, false);
        value.transform.localPosition = localPosition;
        value.transform.localRotation = Quaternion.identity;
        value.transform.localScale = localScale;

        Renderer renderer = value.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;

        Collider collider = value.GetComponent<Collider>();
        if (!keepCollider && collider != null)
            collider.enabled = false;

        return value;
    }

    static void VerifyTrackedPoseDriver(GameObject cameraObject)
    {
        Type inputSystemDriver =
            Type.GetType("UnityEngine.InputSystem.XR.TrackedPoseDriver, Unity.InputSystem");
        if (inputSystemDriver != null && cameraObject.GetComponent(inputSystemDriver) != null)
            return;

        Type legacyDriver =
            Type.GetType("UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
        if (legacyDriver != null && cameraObject.GetComponent(legacyDriver) != null)
            return;

        Debug.LogError(
            "[ExperimentSetup] The headset Camera has no configured TrackedPoseDriver. " +
            "Rebuild ExperimentSetup so it uses the XR Origin prefab.");
    }

    static bool TryAddComponentByName(GameObject target, string assemblyQualifiedName)
    {
        Type type = Type.GetType(assemblyQualifiedName);
        if (type == null)
            return false;
        if (target.GetComponent(type) != null)
            return true;

        try
        {
            target.AddComponent(type);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void DestroyRuntimeObject(UnityEngine.Object value)
    {
        if (value == null)
            return;

        if (Application.isPlaying)
            Destroy(value);
        else
            DestroyImmediate(value);
    }
}
