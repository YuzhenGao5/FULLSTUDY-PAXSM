// 文件名：PanelSubmitMode_EnterOnGrab.cs
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PanelSubmitMode_EnterOnGrab : MonoBehaviour
{
    [Header("Submit 模式下要显示的物体（可以是一个 Root，也可以填多个）")]
    [Tooltip("最常用：拖一个 SubmitUIRoot（里面含按钮/进度环/条形条等），脚本会 SetActive(true/false)")]
    public GameObject submitUIRoot;

    [Tooltip("如果你想分开控制多个对象，也可以把它们都拖进来（可空）")]
    public List<GameObject> submitObjects = new List<GameObject>();

    [Header("Start 时自动隐藏（第一次抓 Submit 旋钮才显示）")]
    public bool hideOnStart = true;

    // ====== 监听 Submit 旋钮抓取 ======
    [Header("Submit Knob 抓取监听（Submit Mode 触发源）")]
    [Tooltip("拖 Submit 旋钮上的 KnobGrabByWrist 进来")]
    public KnobGrabByWrist submitKnobGrab;

    // ====== 平滑后退（可选）=====
    [Header("平滑后退的目标（场景里的物体，可选）")]
    [Tooltip("拖一个已经在场景里的对象（例如你的 panel root / table / 或需要后退的那块）")]
    public GameObject smoothBackTarget;

    [Tooltip("后退距离（米）")]
    public float moveBackDistance = 0.2f;

    [Tooltip("平滑后退用时（秒）")]
    public float smoothMoveDuration = 0.4f;

    Coroutine _smoothMoveCo;
    bool _smoothOriginalSaved = false;
    Vector3 _smoothOriginalPos;

    // ====== 状态 ======
    bool _submitActivatedOnce = false; // ✅ 关键：进入一次 Submit 后就保持

    // ====== ✅ Exit 默认行为（Inspector 可配）======
    [Header("Exit Submit 默认行为（Inspector 可配）")]
    [Tooltip("ExitSubmitMode 时是否把 ALLCONTROL 的 stage 写回 Read")]
    public bool exitRevertStageToRead = true;

    [Tooltip("ExitSubmitMode 时是否把 smoothBackTarget 位置恢复到原位")]
    public bool exitRestorePosition = true;

    [Tooltip("ExitSubmitMode 时是否隐藏 Submit UI")]
    public bool exitHideSubmitUI = true;

    void Start()
    {
        if (hideOnStart)
        {
            SetSubmitVisual(false);
        }

        // 如果脚本启用较晚，但当下已经抓住 Submit 旋钮，则立刻进入 Submit
        if (submitKnobGrab != null && submitKnobGrab.IsGrabbing)
        {
            EnterSubmitMode();
        }
    }

    void OnEnable()
    {
        if (submitKnobGrab != null)
        {
            submitKnobGrab.OnGrabStart.AddListener(HandleGrabStart);
            submitKnobGrab.OnGrabEnd.AddListener(HandleGrabEnd);
        }
        else
        {
            Debug.LogWarning("[PanelSubmitMode] submitKnobGrab 未设置，无法监听抓取。", this);
        }
    }

    void OnDisable()
    {
        if (submitKnobGrab != null)
        {
            submitKnobGrab.OnGrabStart.RemoveListener(HandleGrabStart);
            submitKnobGrab.OnGrabEnd.RemoveListener(HandleGrabEnd);
        }
    }

    void HandleGrabStart()
    {
        Debug.Log("[PanelSubmitMode] Submit Knob Grab START → Enter Submit (sticky).", this);
        EnterSubmitMode();
    }

    void HandleGrabEnd()
    {
        // ✅ 松手不回弹、不隐藏、不恢复位置
        Debug.Log("[PanelSubmitMode] Submit Knob Grab END → stay in Submit (no revert).", this);
    }

    // ================== Submit 模式（进入一次后常驻） ==================

    public void EnterSubmitMode()
    {
        if (_submitActivatedOnce) return;
        _submitActivatedOnce = true;

        // ✅ 写入 ALLCONTROL：当前题进入 Submit 阶段
        if (ALLCONTROL.Instance != null)
        {
            ALLCONTROL.Instance.SetStageForCurrent(ALLCONTROL.QuestionStage.Submit, force: true, logEvent: true);
            ALLCONTROL.Instance.Record("EnterSubmitMode");
        }
        else
        {
            Debug.LogWarning("[PanelSubmitMode] ALLCONTROL.Instance is null，无法写入 SubmitStage。", this);
        }

        SetSubmitVisual(true);
        StartSmoothBack();

        Debug.Log("[PanelSubmitMode] Submit ON → submit visuals shown & smooth back.", this);
    }
    public void EnterFinishedSubmitMode()
    {
        if (_submitActivatedOnce) return;
        _submitActivatedOnce = true;

        SetSubmitVisual(true);
        StartSmoothBack();

        Debug.Log("[PanelSubmitMode] Submit ON → submit visuals shown & smooth back.", this);
    }
    void SetSubmitVisual(bool on)
    {
        if (submitUIRoot != null) submitUIRoot.SetActive(on);

        if (submitObjects != null)
        {
            for (int i = 0; i < submitObjects.Count; i++)
            {
                var go = submitObjects[i];
                if (go != null) go.SetActive(on);
            }
        }
    }

    // ================== 平滑后退 ==================

    void StartSmoothBack()
    {
        if (smoothBackTarget == null) return;

        var t = smoothBackTarget.transform;

        // 第一次保存原始位置，避免多次触发导致越退越远
        if (!_smoothOriginalSaved)
        {
            _smoothOriginalPos = t.position;
            _smoothOriginalSaved = true;
        }

        if (_smoothMoveCo != null) StopCoroutine(_smoothMoveCo);
        _smoothMoveCo = StartCoroutine(MoveBackSmooth(t));
    }

    IEnumerator MoveBackSmooth(Transform target)
    {
        if (target == null) yield break;

        Vector3 startPos = _smoothOriginalPos;
        Vector3 endPos   = startPos - target.forward * moveBackDistance;

        float timer = 0f;
        while (timer < smoothMoveDuration)
        {
            timer += Time.deltaTime;
            float u = Mathf.Clamp01(timer / smoothMoveDuration);

            // easeInOut
            float eased = u * u * (3f - 2f * u);

            target.position = Vector3.Lerp(startPos, endPos, eased);
            yield return null;
        }

        target.position = endPos;
        _smoothMoveCo = null;
    }

    // ================== ✅ 对外：关闭 Submit Stage（不自动调用，你在别处 call） ==================
    // 用法：
    // submitPanel.ExitSubmitMode();
    // submitPanel.ExitSubmitMode(revertStageToRead:false); // 只做UI回收，不写 stage
    public void ExitSubmitMode(
        bool? revertStageToRead = null,
        bool? restorePosition = null,
        bool? hideSubmitUI = null
    )
    {
        // ✅ 无条件隐藏 UI（不管之前有没有进入过 Submit）
        SetSubmitVisual(false);

        // ✅ 停止平滑移动（安全）
        if (_smoothMoveCo != null)
        {
            StopCoroutine(_smoothMoveCo);
            _smoothMoveCo = null;
        }

        // ✅ 关键：复位，让下次抓取还能再次 EnterSubmitMode()
        _submitActivatedOnce = false;

        Debug.Log("[PanelSubmitMode] ExitSubmitMode: UI hidden, submit can re-enter next time.", this);
    }



    // ================== 你想“提交成功后再回弹/隐藏”就调用这个 ==================
    [ContextMenu("ResetToInitial")]
    public void ResetToInitial()
    {
        _submitActivatedOnce = false;

        SetSubmitVisual(false);

        if (_smoothMoveCo != null)
        {
            StopCoroutine(_smoothMoveCo);
            _smoothMoveCo = null;
        }

        if (smoothBackTarget != null && _smoothOriginalSaved)
        {
            smoothBackTarget.transform.position = _smoothOriginalPos;
        }

        // （可选）Reset 后把阶段回到 Read
        if (ALLCONTROL.Instance != null)
        {
            ALLCONTROL.Instance.SetStageForCurrent(ALLCONTROL.QuestionStage.Read, force: true, logEvent: true);
            ALLCONTROL.Instance.Record("SubmitResetToRead");
        }

        Debug.Log("[PanelSubmitMode] ResetToInitial() done.", this);
    }
}
