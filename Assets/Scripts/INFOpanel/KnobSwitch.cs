// 文件名：AnswerSubmitPrefabToggle.cs
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

public class AnswerSubmitPrefabToggle : MonoBehaviour
{
    [Header("Button (Event Source)")]
    public StageAwarePagerSubmitButton submitButton;

    [Header("What to show/hide")]
    [Tooltip("A：Answer 提交成功后显示；也可作为 Submit/已提交渲染时显示")]
    public GameObject showOnAnswerConfirmed; // A

    [Tooltip("B：Answer 提交成功后隐藏；Read/未提交 Answer 渲染时显示")]
    public GameObject hideOnAnswerConfirmed; // B

    [Tooltip("若 A/B 是 Prefab：实例化到这个父节点下（可为空）")]
    public Transform instantiateParent;

    [Header("Submit stage behavior")]
    public bool hideShownObjectOnSubmitConfirmed = true;
    public bool restoreHiddenObjectOnSubmitConfirmed = true;

    [Header("Debug")]
    public bool debugLog = true;

    GameObject _spawnedA;
    GameObject _spawnedB;

    // ✅ 缓存初始 active 状态
    bool _initCached = false;
    bool _initAActive = false;
    bool _initBActive = true;

    ALLCONTROL _acCached;

    // ✅ 防重复绑定
    bool _isHooked = false;

    // ✅ 延迟 Apply（等 ALLCONTROL 状态写完）
    Coroutine _applyCo;

    void Awake()
    {
        CacheInitialActivesIfNeeded();
    }

    void OnEnable()
    {
        CacheInitialActivesIfNeeded();
        TryHookAllcontrol();

        if (!submitButton)
        {
            Debug.LogWarning("[AnswerSubmitPrefabToggle] submitButton 未绑定");
            return;
        }

        HookButtonEventsOnce();

        // ✅ 启用时也不要“立刻读状态”，给一帧让外部初始化完成
        RequestApplyNextFrame();
    }

    void OnDisable()
    {
        UnhookAllcontrol();
        UnhookButtonEvents();

        if (_applyCo != null)
        {
            StopCoroutine(_applyCo);
            _applyCo = null;
        }
    }

    // ===================== ✅ ALLCONTROL hook =====================

    void TryHookAllcontrol()
    {
        var newAc = (submitButton != null && submitButton.allcontrol != null)
            ? submitButton.allcontrol
            : ALLCONTROL.Instance;

        // ✅ 如果换了引用，先解绑旧的
        if (_acCached != null && _acCached != newAc)
            UnhookAllcontrol();

        _acCached = newAc;

        if (_acCached != null)
        {
            // 防重复绑定
            _acCached.OnCurrentQuestionChanged -= HandleQuestionChanged;
            _acCached.OnCurrentQuestionChanged += HandleQuestionChanged;

            // ✅ NEW：监听 StageChanged（Redo 就靠这个刷新）
            _acCached.OnStageChanged -= HandleStageChanged;
            _acCached.OnStageChanged += HandleStageChanged;

            // ✅ 可选更稳：监听 AnswerChanged（Redo 清空答案也能刷新）
            _acCached.OnAnswerChanged -= HandleAnswerChanged;
            _acCached.OnAnswerChanged += HandleAnswerChanged;
        }
    }

    void UnhookAllcontrol()
    {
        if (_acCached == null) return;

        _acCached.OnCurrentQuestionChanged -= HandleQuestionChanged;
        _acCached.OnStageChanged -= HandleStageChanged;
        _acCached.OnAnswerChanged -= HandleAnswerChanged;

        _acCached = null;
    }

    void HandleQuestionChanged(int idx)
    {
        if (debugLog) Debug.Log($"[AnswerSubmitPrefabToggle] OnCurrentQuestionChanged -> {idx}");
        RequestApplyNextFrame();
    }

    // ✅ NEW：Redo 会触发 SetStageForCurrent(Read) -> OnStageChanged，因此这里必须刷新
    void HandleStageChanged(int idx, ALLCONTROL.QuestionStage stage)
    {
        // 只关心当前题，避免别的题切 stage 时刷乱
        var ac = _acCached != null ? _acCached : ALLCONTROL.Instance;
        if (ac == null) return;
        if (idx != ac.currentIndex) return;

        if (debugLog) Debug.Log($"[AnswerSubmitPrefabToggle] OnStageChanged -> Q[{idx}] stage={stage}");
        RequestApplyNextFrame();
    }

    // ✅ 可选更稳：Redo 清空答案/置信也会触发（如果你在 Redo 里调用了 OnAnswerChanged）
    void HandleAnswerChanged(int idx, bool hasAnswer)
    {
        var ac = _acCached != null ? _acCached : ALLCONTROL.Instance;
        if (ac == null) return;
        if (idx != ac.currentIndex) return;

        if (debugLog) Debug.Log($"[AnswerSubmitPrefabToggle] OnAnswerChanged -> Q[{idx}] hasAnswer={hasAnswer}");
        RequestApplyNextFrame();
    }

    // ===================== ✅ 事件绑定（防重复） =====================

    void HookButtonEventsOnce()
    {
        if (_isHooked) return;
        _isHooked = true;

        bool okAnswer = TryBindUnityEventByName(submitButton, "OnAnswerHoldConfirmed", OnAnswerConfirmed);
        bool okSubmit = TryBindUnityEventByName(submitButton, "OnSubmitHoldConfirmed", OnSubmitConfirmed);

        // 如果没有拆分事件，就绑定通用 Confirm
        if (!okAnswer || !okSubmit)
        {
            bool okGeneric = false;
            okGeneric |= TryBindUnityEventByName(submitButton, "OnSubmitConfirmed", OnAnyConfirmed);
            okGeneric |= TryBindUnityEventByName(submitButton, "OnSubmitConfirm", OnAnyConfirmed);

            if (debugLog)
            {
                Debug.Log($"[AnswerSubmitPrefabToggle] Bind result: " +
                          $"AnswerSpecific={okAnswer}, SubmitSpecific={okSubmit}, GenericFallback={okGeneric}");
            }
        }
        else
        {
            if (debugLog) Debug.Log("[AnswerSubmitPrefabToggle] ✅ Bound Answer+Submit specific events");
        }
    }

    void UnhookButtonEvents()
    {
        if (!_isHooked) return;
        _isHooked = false;

        if (!submitButton) return;

        TryUnbindUnityEventByName(submitButton, "OnAnswerHoldConfirmed", OnAnswerConfirmed);
        TryUnbindUnityEventByName(submitButton, "OnSubmitHoldConfirmed", OnSubmitConfirmed);
        TryUnbindUnityEventByName(submitButton, "OnSubmitConfirmed", OnAnyConfirmed);
        TryUnbindUnityEventByName(submitButton, "OnSubmitConfirm", OnAnyConfirmed);
    }

    // ===================== Callbacks =====================

    void OnAnyConfirmed()
    {
        var ac = (_acCached != null) ? _acCached
            : ((submitButton != null && submitButton.allcontrol != null) ? submitButton.allcontrol : ALLCONTROL.Instance);

        if (ac == null)
        {
            if (debugLog) Debug.LogWarning("[AnswerSubmitPrefabToggle] ALLCONTROL 为空，无法判断 stage");
            return;
        }

        var st = ac.GetStageForCurrent();
        if (debugLog) Debug.Log($"[AnswerSubmitPrefabToggle] Generic confirmed @ stage={st}");

        if (st == ALLCONTROL.QuestionStage.Answer) OnAnswerConfirmed();
        else if (st == ALLCONTROL.QuestionStage.Submit) OnSubmitConfirmed();
    }

    void OnAnswerConfirmed()
    {
        if (debugLog) Debug.Log("[AnswerSubmitPrefabToggle] ✅ Answer confirmed -> show A, hide B");

        // 先做即时视觉切换
        SetBActive(false);
        SetAActive(true);

        // ✅ 关键修复：不要立刻读 ALLCONTROL 状态（此时常常还没写完）
        RequestApplyNextFrame();
    }

    void OnSubmitConfirmed()
    {
        if (debugLog) Debug.Log("[AnswerSubmitPrefabToggle] ✅ Submit confirmed -> hide A, restore B");

        if (hideShownObjectOnSubmitConfirmed)
            SetAActive(false);

        if (restoreHiddenObjectOnSubmitConfirmed)
            SetBActive(true);

        // ✅ 同理：延迟对齐
        RequestApplyNextFrame();
    }

    // ===================== ✅ 延迟 Apply（等一帧） =====================

    void RequestApplyNextFrame()
    {
        if (!isActiveAndEnabled) return;

        if (_applyCo != null) StopCoroutine(_applyCo);
        _applyCo = StartCoroutine(CoApplyNextFrame());
    }

    IEnumerator CoApplyNextFrame()
    {
        yield return null; // ✅ 等一帧：让 ALLCONTROL/StageController 先更新完成
        ApplyFromAllcontrolState();
        _applyCo = null;
    }

    // ===================== ✅ 核心：按 ALLCONTROL 状态渲染 A/B/全隐藏 =====================

    public void ApplyFromAllcontrolState()
    {
        var ac = (_acCached != null) ? _acCached
            : ((submitButton != null && submitButton.allcontrol != null) ? submitButton.allcontrol : ALLCONTROL.Instance);

        if (ac == null || ac.answers == null || ac.answers.Count == 0) return;

        int i = ac.currentIndex;
        if (i < 0 || i >= ac.answers.Count) return;

        var st = ac.answers[i];

        bool answerSubmitted = st.hasAnswer && st.selectedSlot > 0; // ✅ Answer(已提交)
        bool submitSubmitted = st.selfConfidence > 0;               // ✅ Submit(已提交)
        var stage = st.stage;

        if (debugLog)
        {
            Debug.Log(
                $"[AnswerSubmitPrefabToggle] ApplyFromAllcontrolState -> Q[{i}] " +
                $"stage={stage}, hasAnswer={st.hasAnswer}, slot={st.selectedSlot}, selfConfidence={st.selfConfidence} " +
                $"| answerSubmitted={answerSubmitted}, submitSubmitted={submitSubmitted}"
            );
        }

        // ✅ 全完成：全部隐藏
        if (answerSubmitted && submitSubmitted)
        {
            SetAActive(false);
            SetBActive(false);
            return;
        }

        // ✅ A 显示条件：
        // - stage == Submit (未提交)  OR  (Answer已提交但Submit未提交)
        bool shouldShowA = (stage == ALLCONTROL.QuestionStage.Submit && !submitSubmitted)
                           || (answerSubmitted && !submitSubmitted);

        if (shouldShowA)
        {
            SetAActive(true);
            SetBActive(false);
            return;
        }

        // ✅ 其他情况（Read / Answer未提交）显示 B
        SetAActive(false);
        SetBActive(true);
    }

    // ===================== ✅ Reset API =====================

    public void ResetToInitialState()
    {
        CacheInitialActivesIfNeeded();

        SetAActive(_initAActive);
        SetBActive(_initBActive);

        if (debugLog)
        {
            Debug.Log($"[AnswerSubmitPrefabToggle] 🔄 ResetToInitialState() " +
                      $"A={_initAActive}, B={_initBActive}", this);
        }
    }

    void CacheInitialActivesIfNeeded()
    {
        if (_initCached) return;

        _initAActive = GetSceneActiveOrDefault(showOnAnswerConfirmed, defaultValue: false);
        _initBActive = GetSceneActiveOrDefault(hideOnAnswerConfirmed, defaultValue: true);

        _initCached = true;
    }

    // ===================== Helpers (A/B instantiate + active) =====================

    void SetAActive(bool on)
    {
        var go = EnsureObject(showOnAnswerConfirmed, ref _spawnedA, "_A");
        if (go) go.SetActive(on);
    }

    void SetBActive(bool on)
    {
        var go = EnsureObject(hideOnAnswerConfirmed, ref _spawnedB, "_B");
        if (go) go.SetActive(on);
    }

    GameObject EnsureObject(GameObject obj, ref GameObject spawned, string suffix)
    {
        if (!obj) return null;

        // 场景物体：直接用
        if (obj.scene.IsValid())
            return obj;

        // Prefab 资产：实例化一次并缓存
        if (spawned == null)
        {
            spawned = Instantiate(obj, instantiateParent);
            spawned.name = obj.name + suffix + "_Instance";
        }
        return spawned;
    }

    bool GetSceneActiveOrDefault(GameObject obj, bool defaultValue)
    {
        if (!obj) return defaultValue;
        if (obj.scene.IsValid()) return obj.activeSelf;
        return defaultValue; // prefab 资产没有 activeSelf 的“场景态”，用默认值
    }

    // ===================== Reflection bind/unbind =====================

    static bool TryBindUnityEventByName(object target, string fieldName, UnityAction action)
    {
        try
        {
            var fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null) return false;

            var val = fi.GetValue(target);
            if (val is UnityEvent ue)
            {
                ue.AddListener(action);
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    static bool TryUnbindUnityEventByName(object target, string fieldName, UnityAction action)
    {
        try
        {
            var fi = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null) return false;

            var val = fi.GetValue(target);
            if (val is UnityEvent ue)
            {
                ue.RemoveListener(action);
                return true;
            }
            return false;
        }
        catch { return false; }
    }
}
