// Assets/Scripts/Data/QuestionSessionStore.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public class QuestionSessionStore : MonoBehaviour
{
    [Header("Bank Meta")]
    public int scale = 7;
    public List<string> tickTexts = new();     // 长度可!=scale，UI自己均分/补齐
    public List<QB.Item> items = new();        // 题目列表（从 QuestionRenderer 赋值）

    [Header("Runtime State")]
    public int currentIndex = 0;               // 当前题序
    public Dictionary<string, int?> answers = new(); // item.id -> 选中的档位(0..k-1)/null

    // 事件（UI订阅）
    public event Action OnBankLoaded;          // 题库加载完
    public event Action OnItemChanged;         // 切换题
    public event Action<string,int?> OnAnswerChanged; // 某题作答变化

    public void SetBank(int k, IList<string> texts, IList<QB.Item> its)
    {
        scale = Mathf.Max(2, k);
        tickTexts = new List<string>(texts ?? Array.Empty<string>());
        items = new List<QB.Item>(its ?? Array.Empty<QB.Item>());
        answers.Clear();
        foreach (var it in items) answers[it.id] = null;
        currentIndex = 0;
        OnBankLoaded?.Invoke();
        OnItemChanged?.Invoke();
    }

    public void Goto(int idx)
    {
        currentIndex = Mathf.Clamp(idx, 0, Mathf.Max(0, items.Count - 1));
        OnItemChanged?.Invoke();
    }

    public void SetAnswer(string itemId, int? level)
    {
        if (!answers.ContainsKey(itemId)) return;
        answers[itemId] = level;               // level: 0..(scale-1) 或 null
        OnAnswerChanged?.Invoke(itemId, level);
    }

    public QB.Item CurrentItem => (items.Count==0) ? null : items[currentIndex];
}
