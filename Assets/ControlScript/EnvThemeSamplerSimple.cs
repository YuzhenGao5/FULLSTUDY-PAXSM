using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 和背景「同一色系」但经过 UI 风格约束的配色 + 透明度自适应：
/// 1）进场景 / 调用时先把问卷关掉
/// 2）从相机画面的下半屏采样平均颜色
/// 3）取环境 Hue，压饱和度 + 限制亮度 + 量化成 UI 风格的色阶
/// 4）根据环境亮度在 [minMainAlpha, maxMainAlpha] 范围内自适应透明度
/// 5）给主面板 / 边条等上色
/// 6）根据近似 WCAG 对比度自动挑白字/黑字
/// 7）再把问卷打开
/// </summary>
public class EcAdapte : MonoBehaviour
{
    [Header("基础引用")]
    [Tooltip("XR 主相机（用于采样场景颜色）")]
    public Camera sampleCamera;

    [Tooltip("问卷根节点（包含所有 UI 的父物体）")]
    public GameObject questionnaireRoot;

    [Header("需要上色的 Image 列表")]
    [Tooltip("主背景板，通常是最主要的那块面板")]
    public Image mainPanel;

    [Tooltip("比主面板略浅 / 略深的其它面板、边条等")]
    public Image[] extraPanels;

    [Header("UI 风格色彩约束")]
    [Tooltip("UI 饱和度缩放：0 = 几乎灰，1 = 直接用环境饱和度")]
    [Range(0f, 1.5f)]
    public float uiSaturationScale = 0.6f;

    [Tooltip("UI 饱和度下限（太灰会不好看）")]
    [Range(0f, 1f)]
    public float uiMinSaturation = 0.05f;

    [Tooltip("UI 饱和度上限（太艳会像“场景颜色”而不是 UI）")]
    [Range(0f, 1f)]
    public float uiMaxSaturation = 0.4f;

    [Tooltip("在环境基础上的亮度偏移：负数 = 再暗一点")]
    [Range(-0.5f, 0.5f)]
    public float globalValueOffset = -0.15f;

    [Tooltip("UI 面板亮度下限")]
    [Range(0f, 1f)]
    public float uiMinValue = 0.2f;

    [Tooltip("UI 面板亮度上限")]
    [Range(0f, 1f)]
    public float uiMaxValue = 0.6f;

    [Header("透明度自适应（主答题面板）")]
    [Tooltip("主面板透明度下限（暗场景下可稍微透明一点）")]
    [Range(0f, 1f)]
    public float minMainAlpha = 0.75f;

    [Tooltip("主面板透明度上限（亮背景下更不透明以保证可读性）")]
    [Range(0f, 1f)]
    public float maxMainAlpha = 0.9f;

    [Tooltip("环境亮度对透明度的影响程度：0 = 固定在中间值，1 = 完全跟随环境亮度映射")]
    [Range(0f, 1f)]
    public float alphaEnvInfluence = 0.7f;

    [Header("其他面板深浅（相对主面板）")]
    [Tooltip("其他面板亮度相对主面板的偏移，正数=更亮，负数=更暗")]
    public float extraPanelsValueOffset = 0.08f;

    [Header("文字（WCAG 风格选择）")]
    [Tooltip("候选浅色文字（一般接近白）")]
    public Color lightTextColor = new Color(0.96f, 0.95f, 0.91f, 1f);

    [Tooltip("候选深色文字（一般接近黑/深灰）")]
    public Color darkTextColor = new Color(0.12f, 0.12f, 0.12f, 1f);

    [Tooltip("需要自动选浅/深色的文本（标题、题干等）")]
    public TextMeshProUGUI[] textElements;

    [Header("生命周期")]
    [Tooltip("进入场景时自动执行一次适配")]
    public bool adaptOnStart = true;

    [Tooltip("采样时是否暂时隐藏问卷 UI，避免采到自己的面板")]
    public bool hideRootWhileSampling = true;

    [Header("调试（只读）")]
    [SerializeField] Color sampledEnvColor = Color.gray;
    [SerializeField] Color finalMainColor = Color.black;

    void Start()
    {
        if (adaptOnStart)
        {
            StartCoroutine(AdaptRoutine());
        }
    }

    /// <summary>
    /// 外部想主动刷新颜色时调用（比如换背景场景）。
    /// </summary>
    [ContextMenu("Adapt Now")]
    public void AdaptNow()
    {
        StartCoroutine(AdaptRoutine());
    }

    IEnumerator AdaptRoutine()
    {
        if (!sampleCamera)
            sampleCamera = Camera.main;

        if (!sampleCamera)
        {
            Debug.LogWarning("[EcAdapte] 没有找到 Camera。");
            yield break;
        }

        // 1. 先关问卷
        if (hideRootWhileSampling && questionnaireRoot)
            questionnaireRoot.SetActive(false);

        // 等一帧，让相机渲染一帧“没有问卷”的场景
        yield return null;

        // 2. 采样场景颜色（下半屏：更接近地面 / 桌面）
        sampledEnvColor = SampleScreenAverageColorLowerHalf(sampleCamera, 32);

        // 3. 根据环境颜色，生成「同色系 UI 风格 + 自适应透明度」主面板色
        finalMainColor = ComputeUIPanelColorFromEnv(sampledEnvColor);

        // 4. 应用到主面板和其它面板
        ApplyPanelColors();

        // 5. 根据近似 WCAG 对比度，从浅/深候选里选一个文字颜色
        ApplyTextColors();

        // 6. 再把问卷打开
        if (questionnaireRoot)
            questionnaireRoot.SetActive(true);
    }

    // ================= 颜色采样 =================

    /// <summary>
    /// 只采样画面的下半屏（y: 0~0.5），减少天空的影响。
    /// </summary>
    Color SampleScreenAverageColorLowerHalf(Camera cam, int size)
    {
        RenderTexture rt = RenderTexture.GetTemporary(size, size, 16, RenderTextureFormat.ARGB32);
        var prevTarget = cam.targetTexture;
        var prevRect   = cam.rect;

        cam.targetTexture = rt;
        cam.rect          = new Rect(0f, 0f, 1f, 0.5f);
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        tex.Apply();

        Color[] pixels = tex.GetPixels();
        Color sum = Color.black;
        int count = pixels.Length;
        for (int i = 0; i < count; i++)
            sum += pixels[i];

        Color avg = sum / Mathf.Max(1, count);

        RenderTexture.active  = null;
        cam.targetTexture     = prevTarget;
        cam.rect              = prevRect;
        RenderTexture.ReleaseTemporary(rt);

#if UNITY_EDITOR
        if (Application.isPlaying) Destroy(tex);
        else DestroyImmediate(tex);
#else
        Destroy(tex);
#endif

        return avg;
    }

    // ================= 颜色计算 / 应用 =================

    /// <summary>
    /// 用环境 Hue，同色系；饱和度/亮度约束成 UI 风格，并根据环境亮度自适应透明度。
    /// </summary>
    Color ComputeUIPanelColorFromEnv(Color env)
    {
        Color.RGBToHSV(env, out float h_env, out float s_env, out float v_env);

        // 1) Hue 直接用环境的 Hue：保持“同一色系”
        float h = h_env;

        // 2) Saturation：压缩到 UI 区间 [uiMinSaturation, uiMaxSaturation]
        float s = s_env * uiSaturationScale;
        s = Mathf.Clamp(s, uiMinSaturation, uiMaxSaturation);

        // 3) Value：基础是环境亮度，偏移一点，再限制、量化成「UI 色阶」
        float vBase = v_env + globalValueOffset;
        vBase = Mathf.Clamp(vBase, uiMinValue, uiMaxValue);

        // 简单的 UI 亮度档位（你可以按喜好改）
        float[] steps = { 0.22f, 0.30f, 0.38f, 0.46f, 0.54f, 0.60f };
        float vQuant = steps[0];
        float minDiff = Mathf.Abs(vBase - steps[0]);
        for (int i = 1; i < steps.Length; i++)
        {
            float d = Mathf.Abs(vBase - steps[i]);
            if (d < minDiff)
            {
                minDiff = d;
                vQuant = steps[i];
            }
        }
        float v = vQuant;

        Color c = Color.HSVToRGB(h, s, v);

        // 4) 透明度：根据环境亮度在 [minMainAlpha, maxMainAlpha] 上插值
        // v_env 越大（越亮的背景），面板越不透明（更接近 maxMainAlpha）
        float rawAlpha = Mathf.Lerp(minMainAlpha, maxMainAlpha, Mathf.Clamp01(v_env));

        // 可以用 alphaEnvInfluence 控制“跟环境走”的程度
        float mid = (minMainAlpha + maxMainAlpha) * 0.5f;
        float finalAlpha = Mathf.Lerp(mid, rawAlpha, alphaEnvInfluence);
        finalAlpha = Mathf.Clamp01(finalAlpha);

        c.a = finalAlpha;
        return c;
    }

    void ApplyPanelColors()
    {
        if (mainPanel)
        {
            mainPanel.color = finalMainColor;
        }

        if (extraPanels != null && extraPanels.Length > 0)
        {
            Color.RGBToHSV(finalMainColor, out float h, out float s, out float v);
            float v2 = Mathf.Clamp01(v + extraPanelsValueOffset);
            Color extraColor = Color.HSVToRGB(h, s, v2);

            foreach (var img in extraPanels)
            {
                if (!img) continue;
                Color c = extraColor;
                c.a = img.color.a; // 保留各自原本设置的 alpha
                img.color = c;
            }
        }
    }

    // ================= 文本颜色：近似 WCAG 对比度 =================

    void ApplyTextColors()
    {
        if (textElements == null || textElements.Length == 0 || !mainPanel)
            return;

        Color bg = mainPanel.color;

        Color candidateLight = lightTextColor;
        Color candidateDark  = darkTextColor;

        float contrastLight = ContrastRatio(bg, candidateLight);
        float contrastDark  = ContrastRatio(bg, candidateDark);

        // 优先选满足 4.5:1 的，如果都满足就选更高的
        Color chosen;
        bool lightOk = contrastLight >= 4.5f;
        bool darkOk  = contrastDark  >= 4.5f;

        if (lightOk && darkOk)
        {
            chosen = (contrastLight >= contrastDark) ? candidateLight : candidateDark;
        }
        else if (lightOk)
        {
            chosen = candidateLight;
        }
        else if (darkOk)
        {
            chosen = candidateDark;
        }
        else
        {
            // 都不达标，就选对比度更高的一边
            chosen = (contrastLight >= contrastDark) ? candidateLight : candidateDark;
        }

        foreach (var t in textElements)
        {
            if (!t) continue;
            Color c = chosen;
            c.a = t.color.a;    // 保留原透明度
            t.color = c;
        }
    }

    // 相对亮度（WCAG 公式的简化实现）
    float RelativeLuminance(Color c)
    {
        float Linear(float ch)
        {
            if (ch <= 0.03928f) return ch / 12.92f;
            return Mathf.Pow((ch + 0.055f) / 1.055f, 2.4f);
        }

        float r = Linear(c.r);
        float g = Linear(c.g);
        float b = Linear(c.b);
        return 0.2126f * r + 0.7152f * g + 0.0722f * b;
    }

    float ContrastRatio(Color bg, Color fg)
    {
        float L1 = RelativeLuminance(bg);
        float L2 = RelativeLuminance(fg);
        if (L1 < L2)
        {
            float tmp = L1;
            L1 = L2;
            L2 = tmp;
        }
        return (L1 + 0.05f) / (L2 + 0.05f);
    }
}
