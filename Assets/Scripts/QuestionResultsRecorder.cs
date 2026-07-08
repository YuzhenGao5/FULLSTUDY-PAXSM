using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// 问卷结果收集器：订阅 QuestionSessionStore 的事件，记录每题作答与反应时。
/// - 记录逻辑：当切换题目时记下开始时间；当本题首次作答时计算反应时；
///             若后续更改同一题的答案，追加一条“修改记录”(is_change=true)。
/// - 导出：SaveCsv() 写出 CSV 文件（含 session/participant 元信息）。
public class QuestionResultsRecorder : MonoBehaviour
{
    [Header("Wiring")]
    public QuestionSessionStore store;          // 拖场景里的 SessionStore

    [Header("Meta (可选)")]
    public string sessionId = "";               // e.g., DateTime.Now.Ticks
    public string participantId = "";           // 受试者编号

    [Header("Auto Save")]
    public bool saveOnQuit = true;              // 退出 Play/应用时自动保存
    public string fileNamePrefix = "QE_Result"; // 文件名前缀

    // 单条记录
    [Serializable]
    public class Entry {
        public string session_id;
        public string participant_id;
        public string timestamp_iso;      // 记录写入时间（ISO）
        public string item_id;            // 题目 ID
        public int    scale;              // 量表刻度 K
        public int    chosen_index;       // 选中的档位 0..K-1
        public bool   is_change;          // 是否为对同一题的修改
        public double response_time_sec;  // 切题→此次作答 的秒数
        public int    item_index;         // 题目序号（0..N-1）
    }

    readonly List<Entry> _rows = new();
    double _itemStartTime = -1;                  // 当前题开始计时
    string _currentItemId = null;                // 当前题 ID
    bool _hasFirstAnswerThisItem = false;        // 是否已经记录过首次作答

    void Awake()
    {
        if (string.IsNullOrEmpty(sessionId))
            sessionId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
    }

    void OnEnable()
    {
        if (!store) {
            Debug.LogWarning("[ResultsRecorder] store 未设置，无法记录。");
            return;
        }
        store.OnBankLoaded   += OnBankLoaded;
        store.OnItemChanged  += OnItemChanged;
        store.OnAnswerChanged+= OnAnswerChanged;
    }

    void OnDisable()
    {
        if (!store) return;
        store.OnBankLoaded   -= OnBankLoaded;
        store.OnItemChanged  -= OnItemChanged;
        store.OnAnswerChanged-= OnAnswerChanged;
    }

    void OnApplicationQuit()
    {
        if (saveOnQuit) SaveCsv(); // 退出自动保存
    }

    // —— 事件回调 ————————————————————————————————
    void OnBankLoaded()
    {
        // 新题库开始，清理状态
        _rows.Clear();
        _itemStartTime = -1;
        _currentItemId = null;
        _hasFirstAnswerThisItem = false;
    }

    void OnItemChanged()
    {
        var cur = store.CurrentItem;
        if (cur == null) return;

        _currentItemId = cur.id;
        _hasFirstAnswerThisItem = false;
        _itemStartTime = Time.realtimeSinceStartupAsDouble;
    }

    void OnAnswerChanged(string itemId, int? level)
    {
        // 只记录当前题的事件；忽略 null（清空）情况
        if (store.CurrentItem == null || itemId != store.CurrentItem.id) return;
        if (!level.HasValue) return;

        double now = Time.realtimeSinceStartupAsDouble;
        double rt  = (_itemStartTime > 0) ? (now - _itemStartTime) : 0.0;

        var e = new Entry {
            session_id        = sessionId,
            participant_id    = participantId,
            timestamp_iso     = DateTime.UtcNow.ToString("o"),
            item_id           = itemId,
            scale             = store.scale,
            chosen_index      = Mathf.Clamp(level.Value, 0, Mathf.Max(1, store.scale) - 1),
            is_change         = _hasFirstAnswerThisItem, // 首次=false，改选=true
            response_time_sec = rt,
            item_index        = store.currentIndex
        };
        _rows.Add(e);

        if (!_hasFirstAnswerThisItem) _hasFirstAnswerThisItem = true;
    }

    // —— 导出 CSV ————————————————————————————————
    public string SaveCsv(string directory = null)
    {
        if (string.IsNullOrEmpty(directory))
            directory = Application.persistentDataPath;

        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"{fileNamePrefix}_{sessionId}.csv");

        var sb = new StringBuilder();
        sb.AppendLine("session_id,participant_id,timestamp_iso,item_id,item_index,scale,chosen_index,is_change,response_time_sec");

        foreach (var r in _rows)
        {
            sb.Append(Escape(r.session_id)).Append(',')
              .Append(Escape(r.participant_id)).Append(',')
              .Append(Escape(r.timestamp_iso)).Append(',')
              .Append(Escape(r.item_id)).Append(',')
              .Append(r.item_index).Append(',')
              .Append(r.scale).Append(',')
              .Append(r.chosen_index).Append(',')
              .Append(r.is_change ? "1" : "0").Append(',')
              .Append(r.response_time_sec.ToString("0.000"))
              .Append('\n');
        }

        File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        Debug.Log($"[ResultsRecorder] CSV saved: {file}");
        return file;
    }

    static string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains("\n"))
            return $"\"{s.Replace("\"","\"\"")}\"";
        return s;
    }
}
