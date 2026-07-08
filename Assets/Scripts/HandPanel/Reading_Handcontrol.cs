using UnityEngine;

/// <summary>
/// Reading Mode Visibility Toggle (Read-Only):
/// - Confirm on Reading -> SHOW readingModeRoot + HIDE originalScreenRoot.
/// - Confirm on Reading again -> HIDE readingModeRoot + SHOW originalScreenRoot.
/// - Confirm on any OTHER sector -> if reading is active, auto exit (optional).
/// 
/// IMPORTANT:
/// - This script does NOT move anything.
/// - It does NOT call Button.onClick.
/// - It only listens to radial.onConfirmedIndex.
/// - It does NOT touch any camera settings.
/// </summary>
public class RadialConfirm_ReadingModeToggle_ReadOnly : MonoBehaviour
{
    [Header("Radial Source (auto-find if empty)")]
    public RadialStick4Way_DynamicSlots_Stable_SelectState_Hold radial;

    [Header("Which sector index is READING in your BASE ORDER?")]
    public int readingIndex = 0;

    [Header("Objects To Toggle")]
    [Tooltip("ReadingMode 根物体（你层级里的 ReadingMode）")]
    public GameObject readingModeRoot;

    [Tooltip("你原来的主问卷/主屏幕根物体（进入 Reading 时隐藏）")]
    public GameObject originalScreenRoot;

    [Header("Behavior")]
    [Tooltip("If true, confirming any NON-reading sector will exit reading mode.")]
    public bool cancelReadingWhenOtherConfirmed = true;

    [Tooltip("Optional safety: if true, highlight leaving reading will also cancel reading mode.\n" +
             "Default FALSE to match your pager logic.")]
    public bool cancelReadingWhenHighlightLeaves = false;

    [Tooltip("Start with reading mode hidden.")]
    public bool hideReadingOnStart = true;

    [Header("Debug")]
    public bool debugLog = true;

    // -------- runtime state --------
    bool _readingActive = false;

    void Start()
    {
        if (hideReadingOnStart && readingModeRoot)
            readingModeRoot.SetActive(false);

        if (originalScreenRoot)
            originalScreenRoot.SetActive(true);

        _readingActive = readingModeRoot ? readingModeRoot.activeSelf : false;
    }

    void OnEnable()
    {
        if (!radial)
            radial = FindObjectOfType<RadialStick4Way_DynamicSlots_Stable_SelectState_Hold>(true);

        if (!radial)
        {
            Debug.LogError("[ReadingModeToggle_ReadOnly] Cannot find RadialStick...Hold.", this);
            return;
        }

        radial.onConfirmedIndex.AddListener(OnRadialConfirmedIndex);

        if (debugLog)
            Debug.Log($"[ReadingModeToggle_ReadOnly] Bound radial={radial.name}, readingIndex={readingIndex}", this);
    }

    void OnDisable()
    {
        if (!radial) return;
        radial.onConfirmedIndex.RemoveListener(OnRadialConfirmedIndex);
    }

    void Update()
    {
        if (!radial) return;

        if (_readingActive && cancelReadingWhenHighlightLeaves)
        {
            if (radial.CurrentSectorIndex != readingIndex)
            {
                if (debugLog)
                    Debug.Log("[ReadingModeToggle_ReadOnly] Highlight left reading -> auto exit (optional).", this);

                ExitReading();
            }
        }
    }

    // ---------------- Confirm event handler ----------------

    void OnRadialConfirmedIndex(int idx)
    {
        if (debugLog)
            Debug.Log($"[ReadingModeToggle_ReadOnly] confirm idx={idx}", this);

        if (idx == readingIndex)
        {
            // ✅ Reading Confirm 是“显示/隐藏开关”
            if (!_readingActive) EnterReading();
            else ExitReading();

            return;
        }

        // ✅ confirm 了别的选项 -> 可选：退出 reading
        if (_readingActive && cancelReadingWhenOtherConfirmed)
        {
            if (debugLog)
                Debug.Log("[ReadingModeToggle_ReadOnly] Other sector confirmed -> exit reading.", this);

            ExitReading();
        }
    }

    // ---------------- Core ----------------

    void EnterReading()
    {
        _readingActive = true;

        if (originalScreenRoot)
            originalScreenRoot.SetActive(false);

        if (readingModeRoot)
            readingModeRoot.SetActive(true);

        if (debugLog)
            Debug.Log("[ReadingModeToggle_ReadOnly] ENTER reading -> show ReadingMode, hide Original.", this);
    }

    void ExitReading()
    {
        _readingActive = false;

        if (readingModeRoot)
            readingModeRoot.SetActive(false);

        if (originalScreenRoot)
            originalScreenRoot.SetActive(true);

        if (debugLog)
            Debug.Log("[ReadingModeToggle_ReadOnly] EXIT reading -> hide ReadingMode, show Original.", this);
    }

    // ---------------- Optional public API ----------------

    public bool IsReadingActive => _readingActive;

    public void ForceEnter()
    {
        if (_readingActive) return;
        EnterReading();
    }

    public void ForceExit()
    {
        if (!_readingActive) return;
        ExitReading();
    }
}
