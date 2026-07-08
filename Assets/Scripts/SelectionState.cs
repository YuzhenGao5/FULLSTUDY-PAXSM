using System;            // 为 Action<int>
using UnityEngine;

/// 维护“当前题目”的选择状态（仅数据，不含UI）
public class SelectionState : MonoBehaviour
{
    public int Scale { get; private set; } = 7;   // 统一刻度
    public int CurrentIndex { get; private set; } = -1;  // -1 表示未选择

    /// <summary>索引改变时回调（-1..Scale-1）</summary>
    public event Action<int> OnIndexChanged;

    /// <summary>初始化刻度与初始值</summary>
    public void Init(int scale, int startIndex = -1)
    {
        Scale = Mathf.Max(2, scale);
        SetIndex(startIndex, invoke:false);
    }

    /// <summary>设置选择索引</summary>
    public void SetIndex(int idx, bool invoke = true)
    {
        int newIdx = Mathf.Clamp(idx, -1, Scale - 1);
        if (newIdx == CurrentIndex) return;
        CurrentIndex = newIdx;
        if (invoke && OnIndexChanged != null) OnIndexChanged(CurrentIndex);
    }
}
