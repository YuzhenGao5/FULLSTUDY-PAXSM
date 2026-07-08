using UnityEngine;

/// <summary>
/// Pager Confirm Lock (Non-exclusive):
/// - Confirm Pager -> lock + move to target.
/// - Confirm Pager again -> unlock + return.
/// 
/// ✅ Key rule:
/// - Pager does NOT auto-unlock when other sectors are confirmed.
/// - Pager confirm does NOT close/affect any other mode.
/// 
/// IMPORTANT:
/// - This script does NOT call any Button.onClick.
/// - It only listens to radial.onConfirmedIndex.
/// </summary>
public class RadialConfirm_PagerLock_MovePanel : MonoBehaviour
{
    [Header("Radial Source (auto-find if empty)")]
    public RadialStick4Way_DynamicSlots_Stable_SelectState_Hold radial;

    [Header("Which sector index is PAGER in your BASE ORDER?")]
    public int pagerIndex = 3;

    [Header("Target Panel (the object you want to move)")]
    public Transform panelToMove;

    [Header("指定位置 (Recommended)")]
    [Tooltip("Drag an empty Transform as the EXACT target pose.")]
    public Transform targetAnchor;

    [Header("Fallback: Head-relative placement (only if targetAnchor empty)")]
    [Tooltip("Drag XR Origin Camera here. If empty, will auto-find MainCamera.")]
    public Transform head;

    public float distance = 0.45f;
    public float verticalOffset = -0.08f;
    public float horizontalOffset = 0.08f;
    public float yawOffset = 0f;
    public float pitchOffset = 0f;
    public bool faceUser = true;

    [Header("Motion")]
    public bool smoothMove = true;
    public float moveSpeed = 10f;
    public float rotateSpeed = 12f;

    [Header("Behavior")]
    [Tooltip("Optional safety: if true, highlight leaving pager will cancel lock.\n" +
             "Default FALSE because pager should be stable unless re-confirmed.")]
    public bool cancelPagerWhenHighlightLeaves = false;

    [Header("Debug")]
    public bool debugLog = true;

    // -------- runtime cache --------
    Transform _origParent;
    Vector3 _origLocalPos;
    Quaternion _origLocalRot;
    Vector3 _origLocalScale;

    bool _pagerLocked = false;
    bool _movingToTarget = false;

    Vector3 _targetWorldPos;
    Quaternion _targetWorldRot;

    void Awake()
    {
        if (!panelToMove) panelToMove = transform;
        CacheOriginal();
    }

    void OnEnable()
    {
        if (!head)
        {
            var cam = Camera.main;
            if (cam) head = cam.transform;
        }

        if (!radial)
            radial = FindObjectOfType<RadialStick4Way_DynamicSlots_Stable_SelectState_Hold>(true);

        if (!radial)
        {
            Debug.LogError("[PagerLock_MovePanel] Cannot find RadialStick...Hold.", this);
            return;
        }

        radial.onConfirmedIndex.AddListener(OnRadialConfirmedIndex);

        if (debugLog)
            Debug.Log($"[PagerLock_MovePanel] Bound radial={radial.name}, panel={panelToMove.name}, pagerIndex={pagerIndex}", this);
    }

    void OnDisable()
    {
        if (!radial) return;
        radial.onConfirmedIndex.RemoveListener(OnRadialConfirmedIndex);
    }

    void Update()
    {
        if (!panelToMove || !radial) return;

        // ✅ 可选：高亮离开也取消（默认关）
        if (_pagerLocked && cancelPagerWhenHighlightLeaves)
        {
            if (radial.CurrentSectorIndex != pagerIndex)
            {
                if (debugLog)
                    Debug.Log("[PagerLock_MovePanel] Highlight left pager -> cancel lock (optional).", this);

                UnlockAndReturn();
            }
        }

        // ✅ 平滑移动到目标
        if (smoothMove && _movingToTarget)
        {
            panelToMove.position = Vector3.Lerp(panelToMove.position, _targetWorldPos, moveSpeed * Time.deltaTime);
            panelToMove.rotation = Quaternion.Slerp(panelToMove.rotation, _targetWorldRot, rotateSpeed * Time.deltaTime);

            if (Vector3.SqrMagnitude(panelToMove.position - _targetWorldPos) < 0.00001f)
            {
                panelToMove.position = _targetWorldPos;
                panelToMove.rotation = _targetWorldRot;
                _movingToTarget = false;
            }
        }
    }

    // ---------------- Confirm event handler ----------------

    void OnRadialConfirmedIndex(int idx)
    {
        if (debugLog)
            Debug.Log($"[PagerLock_MovePanel] confirm idx={idx}", this);

        // ✅ Pager 只对自己做 toggle
        if (idx == pagerIndex)
        {
            if (!_pagerLocked) LockAndMove();
            else UnlockAndReturn();
        }

        // ❌ 不再处理 “confirm 其它扇区 -> 自动解锁 pager”
        // 这样保证：
        // - 其他按钮不会关闭 pager
        // - pager 也不会影响其他按钮
    }

    // ---------------- Core ----------------

    void LockAndMove()
    {
        if (!panelToMove)
        {
            Debug.LogWarning("[PagerLock_MovePanel] Missing panelToMove.", this);
            return;
        }

        CacheOriginal();
        ComputeTargetPose();

        _pagerLocked = true;

        if (!smoothMove)
        {
            panelToMove.position = _targetWorldPos;
            panelToMove.rotation = _targetWorldRot;
            _movingToTarget = false;
        }
        else
        {
            _movingToTarget = true;
        }

        if (debugLog)
            Debug.Log($"[PagerLock_MovePanel] LOCK -> MoveToTarget ({(targetAnchor ? targetAnchor.name : "Head-Fallback")})", this);
    }

    void UnlockAndReturn()
    {
        _pagerLocked = false;
        _movingToTarget = false;

        ReturnToOriginalImmediate();

        if (debugLog)
            Debug.Log("[PagerLock_MovePanel] UNLOCK -> ReturnToOriginal", this);
    }

    void ReturnToOriginalImmediate()
    {
        if (!panelToMove) return;

        if (_origParent)
            panelToMove.SetParent(_origParent, true);

        panelToMove.localScale = _origLocalScale;
        panelToMove.localPosition = _origLocalPos;
        panelToMove.localRotation = _origLocalRot;
    }

    void CacheOriginal()
    {
        if (!panelToMove) return;

        _origParent = panelToMove.parent;
        _origLocalPos = panelToMove.localPosition;
        _origLocalRot = panelToMove.localRotation;
        _origLocalScale = panelToMove.localScale;
    }

    void ComputeTargetPose()
    {
        // ✅ 优先用指定 Anchor
        if (targetAnchor)
        {
            _targetWorldPos = targetAnchor.position;
            _targetWorldRot = targetAnchor.rotation;
            return;
        }

        // ---- fallback: head-relative ----
        if (!head)
        {
            var cam = Camera.main;
            if (cam) head = cam.transform;
        }

        if (!head)
        {
            _targetWorldPos = panelToMove.position;
            _targetWorldRot = panelToMove.rotation;
            Debug.LogWarning("[PagerLock_MovePanel] No targetAnchor and no head found.", this);
            return;
        }

        Vector3 forward = head.forward;
        Vector3 right = head.right;
        Vector3 up = head.up;

        _targetWorldPos =
            head.position +
            forward * distance +
            up * verticalOffset +
            right * horizontalOffset;

        if (faceUser)
        {
            Vector3 lookDir = (_targetWorldPos - head.position).normalized;
            _targetWorldRot = Quaternion.LookRotation(lookDir, Vector3.up);
        }
        else
        {
            _targetWorldRot = head.rotation;
        }

        _targetWorldRot *= Quaternion.Euler(pitchOffset, yawOffset, 0f);
    }

    // ---------------- Optional public API ----------------

    public bool IsPagerLocked => _pagerLocked;

    public void ForceLock()
    {
        if (_pagerLocked) return;
        LockAndMove();
    }

    public void ForceUnlock()
    {
        if (!_pagerLocked) return;
        UnlockAndReturn();
    }
}
