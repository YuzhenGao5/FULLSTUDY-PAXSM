using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// 让它尽量早执行（可选但很推荐）
[DefaultExecutionOrder(-500)]
public class SimpleJsonReader : MonoBehaviour
{
    [Header("拖你的 JSON 文件（TextAsset）")]
    public TextAsset jsonFile;

    [Header("解析后的数据（只读查看）")]
    public LikertSurveyConfig data; // 解析结果放这里

    [Header("加载完成事件（给 TickRingLocal / KnobCore 等监听）")]
    public UnityEvent onLoaded;

    /// <summary>外部可用：判断数据是否可用</summary>
    public bool IsReady => (data != null && data.scale > 1);

    void Awake()
    {
        // ✅ Awake 就读，保证别人 Start/OnEnable 来读时已经有数据
        Reload();
    }

    void OnEnable()
    {
        // ✅ 如果对象被禁用后再启用，确保也能读到
        if (data == null)
            Reload();
    }

    /// <summary>手动重新解析（比如你在运行时换了文件，或想重载）</summary>
    [ContextMenu("Reload")]
    public void Reload()
    {
        if (!jsonFile)
        {
            Debug.LogWarning("[SimpleJsonReader] 没有分配 jsonFile", this);
            data = null;
            return;
        }

        try
        {
            string raw = jsonFile.text;

            if (string.IsNullOrWhiteSpace(raw))
            {
                Debug.LogError($"[SimpleJsonReader] jsonFile '{jsonFile.name}' 内容为空", this);
                data = null;
                return;
            }

            data = JsonUtility.FromJson<LikertSurveyConfig>(raw);

            if (data == null)
            {
                Debug.LogError($"[SimpleJsonReader] 解析结果为空（jsonFile='{jsonFile.name}'）", this);
                return;
            }

            // 小兜底：确保列表不为 null（JsonUtility 可能把缺失字段解析成 null）
            if (data.labels == null) data.labels = new List<string>();
            if (data.items  == null) data.items  = new List<LikertItem>();

            Debug.Log(
                $"[SimpleJsonReader] OK ({jsonFile.name}): " +
                $"scale={data.scale}, default_mode={data.default_mode}, " +
                $"labels={data.labels.Count}, items={data.items.Count}",
                this
            );

            // ✅ 通知所有依赖方：“我已经读好了”
            onLoaded?.Invoke();
        }
        catch (Exception e)
        {
            Debug.LogError("[SimpleJsonReader] JSON 解析失败: " + e, this);
            data = null;
        }
    }
}

[Serializable]
public class LikertSurveyConfig
{
    public int version = 1;
    public int scale   = 5;

    public string default_mode = "cards";

    public List<string> labels = new List<string>();
    public List<LikertItem> items = new List<LikertItem>();
}

[Serializable]
public class LikertItem
{
    public string id;
    public string stem;
    public string mode;
}
