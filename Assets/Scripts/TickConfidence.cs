using System.Collections.Generic;
using UnityEngine;

public class TickRingConfidenceLocal : MonoBehaviour, ITickRing
{
    public enum LongAxis { X, Y, Z }

    [Header("必填")]
    [Tooltip("圆心基准（位置用它，朝向只用初始值）")]
    public Transform knobRoot;      // 指向 Knob/KnobRig 的中心点
    public GameObject tickPrefab;   // 场景里的模板，平时可以隐藏
    public bool autoHideTemplate = true;

    [Header("Confidence 刻度数量（写多少生成多少）")]
    [Min(1)] public int confidenceLevels = 3;

    [Header("半圆分布")]
    public float radius   = 0.22f;
    public float startDeg = -90f;
    public float endDeg   =  90f;

    [Header("朝向")]
    public LongAxis longAxis = LongAxis.X;

    [Tooltip("是否让朝向跟随 Knob 当前 rotation 变化")]
    public bool followKnobRotation = false;

    [Tooltip("如果不跟随，则锁定在 Awake 时的初始朝向")]
    public bool useInitialKnobRotation = true;

    public bool lookOutward  = true;
    public Vector3 rotOffsetEuler = Vector3.zero;

    // ✅ 对外暴露：每个刻度相对于 knobRoot.forward 的角度（度数）
    [HideInInspector]
    public List<float> tickAngles = new List<float>();

    public int TickCount => tickAngles.Count;

    public float GetAngleForIndex(int index)
    {
        if (tickAngles == null || tickAngles.Count == 0) return 0f;
        index = Mathf.Clamp(index, 0, tickAngles.Count - 1);
        return tickAngles[index];
    }

    // ---------- 内部缓存 ----------
    Quaternion _baseRotation;

    void Awake()
    {
        if (knobRoot)
            _baseRotation = knobRoot.rotation;
        else
        {
            _baseRotation = transform.rotation;
            Debug.LogWarning("[TickRingConfidenceLocal] knobRoot 未设置，使用自身 transform 作为基准。", this);
        }

        if (autoHideTemplate && tickPrefab && tickPrefab.scene.IsValid())
            tickPrefab.SetActive(false);
    }

    void OnEnable()
    {
        if (Application.isPlaying)
            Rebuild();
    }

    [ContextMenu("Rebuild (Confidence Levels)")]
    public void Rebuild()
    {
        Clear();
        tickAngles.Clear();

        if (!knobRoot || !tickPrefab)
        {
            Debug.LogWarning("[TickRingConfidenceLocal] 缺少 knobRoot 或 tickPrefab", this);
            return;
        }

        int k = Mathf.Max(1, confidenceLevels);

        Quaternion ori;
        if (followKnobRotation && knobRoot)
            ori = knobRoot.rotation;
        else if (useInitialKnobRotation)
            ori = _baseRotation;
        else
            ori = transform.rotation;

        Vector3 center = knobRoot.position;

        for (int i = 0; i < k; i++)
        {
            // ✅ 和你完美版一致：两端均匀分布；当 k==1 时放在正中
            float t = (k <= 1) ? 0.5f : i / (float)(k - 1);
            float deg = Mathf.Lerp(startDeg, endDeg, t);
            float rad = deg * Mathf.Deg2Rad;

            tickAngles.Add(deg);

            Vector3 dirLocal = (Vector3.forward * Mathf.Cos(rad) + Vector3.right * Mathf.Sin(rad)).normalized;

            Vector3 worldDir = ori * dirLocal;
            Vector3 worldUp  = ori * Vector3.up;
            Vector3 worldPos = center + worldDir * radius;

            Quaternion rot;
            switch (longAxis)
            {
                case LongAxis.X:
                {
                    Vector3 X = (lookOutward ? worldDir : -worldDir).normalized;
                    Vector3 Y = worldUp.normalized;
                    Vector3 Z = Vector3.Cross(Y, X).normalized;
                    rot = Quaternion.LookRotation(Z, Y);
                    break;
                }
                case LongAxis.Y:
                {
                    Vector3 Y = (lookOutward ? worldDir : -worldDir).normalized;
                    Vector3 F = worldUp.normalized;
                    rot = Quaternion.LookRotation(F, Y);
                    break;
                }
                default: // Z
                {
                    Vector3 Z = (lookOutward ? worldDir : -worldDir).normalized;
                    Vector3 Y = worldUp.normalized;
                    rot = Quaternion.LookRotation(Z, Y);
                    break;
                }
            }

            rot *= Quaternion.Euler(rotOffsetEuler);

            var go = Instantiate(tickPrefab, transform);
            go.name = $"Tick_{i + 1}";   // ✅ 仍然用 Tick_ 前缀，保证 Clear 逻辑一致
            go.SetActive(true);

            var tr = go.transform;
            tr.position = worldPos;
            tr.rotation = rot;
        }
    }

    [ContextMenu("Clear All Ticks")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child == null) continue;
            if (!child.name.StartsWith("Tick_")) continue;  // ✅ 和 TickRingLocal 一致

#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(child.gameObject);
            else
                Destroy(child.gameObject);
#else
            Destroy(child.gameObject);
#endif
        }
    }
}
