using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 刻度环布局（刻度绘制在 TablePlane/ TickRing 的“局部平面”里）
/// - 刻度数量来自 SimpleJsonReader.data.scale
/// - 在 tickParent（建议放在 TablePlane 下）周围生成半环刻度（短/长）
/// - 提供：角度 <-> 档位的双向映射；吸附到某档；hover/confirm 事件
/// - 不包含输入读取（你可用自己的输入脚本来调用 SetAngle / SnapToIndex / ConfirmCurrent）
/// </summary>
public class KnobDetentRingLayout : MonoBehaviour
{
    [Header("Data")]
    public SimpleJsonReader reader;          // 你的 JSON 读取器（scale 来源）
    public int fallbackScale = 5;            // 没读到时的兜底刻度数(>=2)

    [Header("Scene Refs")]
    [Tooltip("刻度父物体（建议放在 TablePlane 下，局部 up = 桌面法线）")]
    public Transform tickParent;             // 把它放在 TablePlane 下
    [Tooltip("旋钮父结点（绕 tickParent.up 旋转）；可选")]
    public Transform knob;                   // 可选，用于演示设置旋钮旋转
    [Tooltip("箭头（+Z 为指向方向）；可选")]
    public Transform pointer;                // 可选，一般作为 knob 的子物体

    [Header("Tick Prefabs")]
    public GameObject tickShortPrefab;       // 短刻度（Cube/Quad）
    public GameObject tickLongPrefab;        // 长刻度（首末档；没有就用短刻度）

    [Header("Ring Layout (局部)")]
    [Tooltip("刻度半径（米），在 tickParent 的局部坐标里")]
    public float radius = 0.12f;
    [Tooltip("半环角度（度），如 180 = 半圆")]
    public float ringAngleSpan = 180f;
    [Tooltip("起始角（度），-90 表示从“上方”开始，逆时针展开")]
    public float baseAngle = -90f;

    [Header("Smoothing")]
    [Tooltip("指针/旋钮角度插值速度")]
    public float angleLerpSpeed = 12f;

    [Header("Events")]
    public UnityEvent<int> onHoverIndex;     // 当前角度映射到的“悬停档位”
    public UnityEvent<int> onConfirmIndex;   // 确认的档位（由外部调用 ConfirmCurrent 触发）

    // 运行时
    int   _k = 0;                            // 刻度数量
    float _currentAngle;                     // 当前角度（可平滑）
    float _targetAngle;                      // 目标角度（外部/内部设置）
    int   _hoverIndex = -1;                  // 当前悬停档位
    readonly List<Transform> _ticks = new();

    // =============== 生命周期 ===============
    void Start()
    {
        RebuildFromReader();
        // 初始吸附到中间
        SnapToIndex(Mathf.Clamp(_k / 2, 0, Mathf.Max(0, _k - 1)));
    }

    void Update()
    {
        // 平滑角度
        _currentAngle = Mathf.Lerp(_currentAngle, _targetAngle, 1f - Mathf.Exp(-angleLerpSpeed * Time.deltaTime));

        // 若有 knob，则让它绕桌面法线旋转（桌面法线 = tickParent.up）
        if (knob && tickParent)
        {
            var axis = tickParent.up;
            var baseRot = tickParent.rotation; // 与桌面对齐
            knob.rotation = Quaternion.AngleAxis(_currentAngle, axis) * baseRot;
        }

        // 更新悬停档位并发事件
        int idx = GetClosestIndexByAngle(_currentAngle);
        if (idx != _hoverIndex)
        {
            _hoverIndex = idx;
            onHoverIndex?.Invoke(_hoverIndex);
        }
    }

    // =============== 对外 API ===============

    /// <summary>按 JSON（或兜底）重建刻度。</summary>
    public void RebuildFromReader()
    {
        _k = (reader && reader.data != null && reader.data.scale > 1)
            ? reader.data.scale
            : Mathf.Max(2, fallbackScale);

        BuildTicks();
    }

    /// <summary>把当前“目标角度”设为某个角度（度），并保持在合法范围。</summary>
    public void SetAngle(float angDeg, bool snap = false)
    {
        float minA = baseAngle;
        float maxA = baseAngle + ringAngleSpan;
        _targetAngle = Mathf.Clamp(angDeg, minA, maxA);
        if (snap) _currentAngle = _targetAngle;
    }

    /// <summary>把角度设置为第 idx 档位（0..k-1）。</summary>
    public void SnapToIndex(int idx)
    {
        if (_k <= 0) return;
        idx = Mathf.Clamp(idx, 0, _k - 1);
        float ang = AngleForIndex(idx);
        _targetAngle = ang;
        _currentAngle = ang; // 直接吸附
    }

    /// <summary>返回：给定角度（度）最接近的档位索引。</summary>
    public int GetClosestIndexByAngle(float ang)
    {
        if (_k <= 0) return -1;
        float t = Mathf.InverseLerp(baseAngle, baseAngle + ringAngleSpan, ang);
        int idx = Mathf.RoundToInt(t * (_k - 1));
        return Mathf.Clamp(idx, 0, _k - 1);
    }

    /// <summary>返回：给定“世界方向”（例如箭头 forward），在环平面上的最近档位索引。</summary>
    public int GetClosestIndexByDirection(Vector3 worldDir)
    {
        if (!tickParent || _k <= 0) return -1;

        // 把世界方向投到 tickParent 的局部平面（去掉法线分量）
        Vector3 local = tickParent.InverseTransformDirection(worldDir);
        local.y = 0f;
        if (local.sqrMagnitude < 1e-6f) return _hoverIndex;

        float ang = Mathf.Atan2(local.z, local.x) * Mathf.Rad2Deg; // 局部角度
        // 将角度 clamp 到基准/跨度范围
        float clamped = Mathf.Clamp(NormalizeAngleDeg(ang), baseAngle, baseAngle + ringAngleSpan);
        return GetClosestIndexByAngle(clamped);
    }

    /// <summary>确认当前悬停档位（触发事件）。</summary>
    public void ConfirmCurrent()
    {
        onConfirmIndex?.Invoke(_hoverIndex);
    }

    /// <summary>当前悬停索引（只读）。</summary>
    public int CurrentIndex => _hoverIndex;

    /// <summary>刻度数（只读）。</summary>
    public int ScaleCount => _k;

    /// <summary>返回指定档位的中心角度（度）。</summary>
    public float AngleForIndex(int idx)
    {
        if (_k <= 1) return baseAngle;
        idx = Mathf.Clamp(idx, 0, _k - 1);
        float step = ringAngleSpan / (_k - 1);
        return baseAngle + idx * step;
    }

    // =============== 内部：构建刻度 ===============
    void BuildTicks()
    {
        if (!tickParent)
        {
            Debug.LogWarning("[KnobDetentRingLayout] tickParent 未设置（建议放在 TablePlane 下）");
            return;
        }

        // 清旧
        for (int i = tickParent.childCount - 1; i >= 0; i--)
            Destroy(tickParent.GetChild(i).gameObject);
        _ticks.Clear();

        if (_k < 2) _k = 2;

        float step = (_k > 1) ? ringAngleSpan / (_k - 1) : ringAngleSpan;

        for (int i = 0; i < _k; i++)
        {
            float ang = baseAngle + i * step;

            // 在 tickParent 的局部平面（XZ）上布局
            Vector3 localPos = RingPosLocal(ang);
            Vector3 outwardLocal = new Vector3(Mathf.Cos(ang * Mathf.Deg2Rad), 0f, Mathf.Sin(ang * Mathf.Deg2Rad));

            bool isEnd = (i == 0 || i == _k - 1);
            var prefab = isEnd ? (tickLongPrefab ? tickLongPrefab : tickShortPrefab) : tickShortPrefab;
            if (!prefab) continue;

            var go = Instantiate(prefab, tickParent);
            var t = go.transform;
            t.localPosition = localPos;

            // forward 指向外侧，up = 桌面法线（tickParent 的 +Y）
            t.localRotation = Quaternion.LookRotation(outwardLocal, Vector3.up);

            _ticks.Add(t);
        }
    }

    Vector3 RingPosLocal(float angDeg)
    {
        float rad = angDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
    }

    static float NormalizeAngleDeg(float a)
    {
        while (a > 180f) a -= 360f;
        while (a < -180f) a += 360f;
        return a;
    }
}
