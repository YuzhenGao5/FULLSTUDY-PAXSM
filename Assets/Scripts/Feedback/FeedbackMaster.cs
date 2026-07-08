// 文件名：XRFeedbackMaster.cs
using UnityEngine;

/// <summary>
/// XR 问卷系统的“反馈总控”单例：
/// - 根据一个 0~1 的 globalLevel 控制场景光照（强度 + 颜色）
/// - 根据 Knob 的旋转速度，播放连续的“阻尼/摩擦”声音
///
/// 特点：
/// - 两大模块都有开关，可以在 Inspector 里单独启用/禁用
/// - 其它脚本只需要：
///   1) 在回答结束时调用 SetGlobalLevelXX(...) 更新光照
///   2) 在 KnobCore.Update() 里每帧把当前旋钮角度喂给 ReportKnobAngle(...)
/// </summary>
[DisallowMultipleComponent]
public class XRFeedbackMaster : MonoBehaviour
{
    #region Singleton
    public static XRFeedbackMaster Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // 如果你希望跨场景保留，可以打开：
        // DontDestroyOnLoad(gameObject);

        InitKnobAudio();
        InitLightingGradientIfEmpty();
    }
    #endregion

    // =================== 总开关 ===================

    [Header("=== 总开关 ===")]
    [Tooltip("是否启用光照反馈模块")]
    public bool enableLightingFeedback = true;

    [Tooltip("是否启用 Knob 阻尼声音模块")]
    public bool enableKnobDampingSound = true;

    // =================== 光照反馈 ===================

    [Header("=== 光照反馈（0~1 状态驱动） ===")]
    [Tooltip("主光源（通常是场景里的 Directional Light）")]
    public Light mainLight;

    [Tooltip("根据 0~1 状态插值的颜色渐变，不设则脚本会给一个默认值")]
    public Gradient lightColorByLevel;

    [Tooltip("根据 0~1 状态映射到光强的曲线，x=0..1, y=强度，可在 Inspector 里编辑")]
    public AnimationCurve lightIntensityCurve = AnimationCurve.Linear(0, 0.6f, 1, 1.3f);

    [Range(0f, 1f)]
    [Tooltip("当前全局状态（0=最平静，1=最高紧张），可视化调试用")]
    public float currentLevel01 = 0f;

    // =================== Knob 阻尼声音 ===================

    [Header("=== Knob 阻尼声音（连续摩擦感） ===")]
    [Tooltip("用于播放阻尼/摩擦 loop 声音的 AudioSource")]
    public AudioSource knobLoopSource;

    [Tooltip("一段循环的“机械摩擦/阻尼”AudioClip（建议无明显起止）")]
    public AudioClip knobDampingClip;

    [Tooltip("假定的 Knob 最大旋转速度（度/秒），用于归一化，建议 ≈ KnobCore.rotateSpeed")]
    public float knobRefMaxDegPerSec = 720f;

    [Tooltip("低于这个归一化速度时认为几乎不转，用于静音判定")]
    public float knobSpeedThreshold = 0.03f;

    [Tooltip("归一化速度=0 时的音量")]
    public float knobMinVolume = 0f;

    [Tooltip("归一化速度=1 时的音量")]
    public float knobMaxVolume = 0.4f;

    [Tooltip("归一化速度=0 时的音调")]
    public float knobMinPitch = 0.9f;

    [Tooltip("归一化速度=1 时的音调")]
    public float knobMaxPitch = 1.2f;

    [Tooltip("停止旋转后，音量从当前值衰减到 0 的时间（秒）")]
    public float knobFadeOutTime = 0.25f;

    [Tooltip("音量平滑时间常数（秒），越小变化越跟手")]
    public float knobVolumeSmoothTime = 0.04f;

    // 内部状态
    float _knobTargetVolume = 0f;
    float _knobCurrentVolume = 0f;
    float _knobVolumeVel = 0f;

    float _lastKnobActiveTime = -999f;
    float _prevKnobAngle = 0f;
    bool  _hasPrevKnobAngle = false;
    bool  _knobLoopStarted = false;

    void Update()
    {
        if (enableKnobDampingSound)
        {
            UpdateKnobAudio();
        }
    }

    // ============= 光照相关 =============

    void InitLightingGradientIfEmpty()
    {
        if (lightColorByLevel != null && lightColorByLevel.colorKeys.Length > 0) return;

        // 给个大致可用的默认渐变：0=冷静、柔和；1=稍微偏暖、压迫一点
        lightColorByLevel = new Gradient
        {
            colorKeys = new[]
            {
                new GradientColorKey(new Color(0.75f, 0.9f, 1f), 0f), // 冷静偏蓝
                new GradientColorKey(new Color(1f, 0.85f, 0.7f), 1f), // 偏暖
            },
            alphaKeys = new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f),
            }
        };
    }

    /// <summary>
    /// 手动设置 globalLevel（0~1），会驱动灯光颜色+强度。
    /// 建议在“每题回答结束”或“当前小节状态变化”时调用。
    /// </summary>
    public void SetGlobalLevel01(float level01)
    {
        currentLevel01 = Mathf.Clamp01(level01);
        if (enableLightingFeedback)
        {
            ApplyLighting();
        }
    }

    /// <summary>
    /// 方便 Likert：比如 1~7 或 0~10 的分数映射到 0~1。
    /// </summary>
    public void SetLevelFromLikert(int value, int minValue, int maxValue)
    {
        maxValue = Mathf.Max(minValue + 1, maxValue);
        float t = Mathf.InverseLerp(minValue, maxValue, value);
        SetGlobalLevel01(t);
    }

    void ApplyLighting()
    {
        if (!mainLight) return;

        Color c = lightColorByLevel.Evaluate(currentLevel01);
        float intensity = lightIntensityCurve != null
            ? lightIntensityCurve.Evaluate(currentLevel01)
            : Mathf.Lerp(0.6f, 1.3f, currentLevel01);

        mainLight.color = c;
        mainLight.intensity = intensity;
    }

    // ============= Knob 阻尼声音相关 =============

    void InitKnobAudio()
    {
        if (!knobLoopSource) return;

        knobLoopSource.playOnAwake = false;
        knobLoopSource.loop = true;

        if (knobDampingClip && knobLoopSource.clip != knobDampingClip)
            knobLoopSource.clip = knobDampingClip;

        _knobCurrentVolume = 0f;
        knobLoopSource.volume = 0f;
    }

    /// <summary>
    /// 由 KnobCore 每帧调用：传入当前 knob Y 轴角度（本地角度，单位：度）即可。
    /// 内部会用上一次角度 + Time.deltaTime 计算角速度，并驱动阻尼声音。
    /// </summary>
    public void ReportKnobAngle(float knobAngleDeg)
    {
        if (!enableKnobDampingSound) return;
        if (!knobLoopSource || !knobDampingClip) return;

        // 第一次进来仅记录，不计算速度
        if (!_hasPrevKnobAngle)
        {
            _prevKnobAngle = knobAngleDeg;
            _hasPrevKnobAngle = true;
            return;
        }

        // 用 DeltaAngle 处理 0/360 wrap
        float delta = Mathf.DeltaAngle(_prevKnobAngle, knobAngleDeg);
        _prevKnobAngle = knobAngleDeg;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // 角速度（度/秒）
        float degPerSec = Mathf.Abs(delta) / dt;

        // 归一化到 0~1
        float speed01 = 0f;
        if (knobRefMaxDegPerSec > 0f)
            speed01 = Mathf.Clamp01(degPerSec / knobRefMaxDegPerSec);

        UpdateKnobTargetFromSpeed(speed01);
    }

    void UpdateKnobTargetFromSpeed(float speed01)
    {
        if (!knobLoopSource) return;

        if (speed01 > knobSpeedThreshold)
        {
            // Knob 在明显转动：根据速度计算目标音量和音调
            float t = Mathf.InverseLerp(knobSpeedThreshold, 1f, speed01);
            _knobTargetVolume = Mathf.Lerp(knobMinVolume, knobMaxVolume, t);
            float pitch = Mathf.Lerp(knobMinPitch, knobMaxPitch, t);
            knobLoopSource.pitch = pitch;

            _lastKnobActiveTime = Time.time;

            if (!_knobLoopStarted)
            {
                knobLoopSource.Play();
                _knobLoopStarted = true;
            }
        }
        else
        {
            // 速度很小：让目标音量走向 0
            _knobTargetVolume = 0f;
        }
    }

    void UpdateKnobAudio()
    {
        if (!knobLoopSource || !_knobLoopStarted) return;

        // 若长时间没有报告旋转，则强制 target 收敛到 0
        if (Time.time - _lastKnobActiveTime > knobFadeOutTime)
        {
            _knobTargetVolume = 0f;
        }

        // 用 SmoothDamp 平滑音量变化
        _knobCurrentVolume = Mathf.SmoothDamp(
            _knobCurrentVolume,
            _knobTargetVolume,
            ref _knobVolumeVel,
            knobVolumeSmoothTime
        );

        _knobCurrentVolume = Mathf.Clamp01(_knobCurrentVolume);
        knobLoopSource.volume = _knobCurrentVolume;

        // 静音时可以暂停，防止累积
        if (_knobCurrentVolume <= 0.001f && knobLoopSource.isPlaying)
        {
            knobLoopSource.Pause();
        }
        else if (_knobCurrentVolume > 0.001f && !knobLoopSource.isPlaying)
        {
            knobLoopSource.UnPause();
        }
    }
}
