// 文件名：AllControlStageEventReporter.cs
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
/// <summary>
/// 导出 ALLCONTROL.stageEvents：
/// 1) Raw：逐条事件
/// 2) ByMark：按 Qx-enterCountDerived 聚合，记录“进入每个 stage 的时间点”（realtime）
///
/// ✅ 输出到 KnobBehaviorMergedCSVExporter 创建的同一个 runFolder：
/// base/outputSubfolder/<runFolderName>/StageTimes_ByMark_*.csv
/// base/outputSubfolder/<runFolderName>/StageEvents_Raw_*.csv
///
/// ✅ enterCountDerived 推断（修复 Read->Read 重复）：
/// - 每个 index 第一次出现任何事件：enter=1
/// - 之后仅当 (toStage==Read && fromStage!=Read) 才 enter++
///   => Read->Read 不会增加 enterCount
/// </summary>
public class AllControlStageEventReporter : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("不填则用 ALLCONTROL.Instance")]
    public ALLCONTROL allcontrol;

    [Tooltip("✅ 强烈建议拖：KnobBehaviorMergedCSVExporter（用于对齐输出文件夹，并可先触发它导出创建 runFolder）")]
    public KnobBehaviorMergedCSVExporter mergedExporter;

    [Header("Export Behavior")]
    public bool exportOnQuit = true;
    public bool exportRawEvents = true;
    public KeyCode manualExportKey = KeyCode.F10;

    [Tooltip("如果 mergedExporter 不为空：导出前先调用 mergedExporter.ExportNow() 以确保 runFolder 已存在")]
    public bool callMergedExporterBeforeExport = true;

    [Tooltip("文件名前缀（可选；为空则用默认）")]
    public string filePrefix = "";

    bool _exported = false;

    ALLCONTROL AC => (allcontrol != null) ? allcontrol : ALLCONTROL.Instance;

    void Update()
    {
        if (WasManualExportPressed())
            ExportNow("ManualKey");
    }

    bool WasManualExportPressed()
    {
    #if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;

        // 只做你需要的 F10（也可以扩展映射）
        if (manualExportKey == KeyCode.F10) return kb[Key.F10].wasPressedThisFrame;
        if (manualExportKey == KeyCode.F9)  return kb[Key.F9].wasPressedThisFrame;

        return false;
    #else
        return Input.GetKeyDown(manualExportKey);
    #endif
    }
    void OnApplicationQuit()
    {
        if (exportOnQuit && !_exported)
            ExportNow("OnQuit");
    }

    [ContextMenu("Export Now")]
    public void ExportNow(string reason = "Manual")
    {
        if (_exported) return;

        var ac = AC;
        if (ac == null)
        {
            Debug.LogError("[StageEventReporter] ALLCONTROL not found.", this);
            return;
        }

        if (ac.stageEvents == null || ac.stageEvents.Count == 0)
        {
            Debug.LogWarning("[StageEventReporter] stageEvents is empty.", this);
            return;
        }

        // 1) 先确保 mergedExporter 已经导出并创建 runFolder（推荐）
        if (callMergedExporterBeforeExport && mergedExporter != null)
        {
            mergedExporter.ExportNow("StageEventReporter_PreExport");
        }

        // 2) 找到 mergedExporter 的 runFolder（最新的那一个）
        string runFolder = ResolveRunFolderOrFallback();
        if (string.IsNullOrEmpty(runFolder))
        {
            Debug.LogError("[StageEventReporter] Cannot resolve run folder. Please set mergedExporter or check output paths.", this);
            return;
        }
        Directory.CreateDirectory(runFolder);

        string stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string prefix = string.IsNullOrEmpty(filePrefix) ? "" : (filePrefix.Trim() + "_");

        string pathAgg = Path.Combine(runFolder, $"{prefix}StageTimes_ByMark_{stamp}.csv");
        string pathRaw = Path.Combine(runFolder, $"{prefix}StageEvents_Raw_{stamp}.csv");

        // 3) 排序后的事件
        var events = new List<ALLCONTROL.StageEvent>(ac.stageEvents);
        events.Sort((a, b) => a.realtime.CompareTo(b.realtime));

        // 4) 聚合
        var buckets = BuildBuckets(events);

        // 5) 写聚合
        File.WriteAllText(pathAgg, BuildAggCsv(buckets), System.Text.Encoding.UTF8);

        // 6) 写 raw
        if (exportRawEvents)
            File.WriteAllText(pathRaw, BuildRawCsv(events), System.Text.Encoding.UTF8);

        _exported = true;

        Debug.Log($"[StageEventReporter] ✅ Exported ({reason})\n- {pathAgg}\n- {(exportRawEvents ? pathRaw : "(raw disabled)")}\nrunFolder={runFolder}", this);
    }

    // -----------------------------
    // Folder resolving (match merged exporter)
    // -----------------------------
    string ResolveRunFolderOrFallback()
    {
        if (mergedExporter != null && !string.IsNullOrWhiteSpace(mergedExporter.LastExportDirectory))
            return mergedExporter.LastExportDirectory;

        return ExperimentRunContext.ResolveOutputDirectory("MainScene_Data");
    }

    // -----------------------------
    // Aggregation (ByMark)
    // -----------------------------
    class MarkTimes
    {
        public string mark;     // Q1-1
        public string itemId;
        public int index0;
        public int qNumber1;
        public int enterDerived;

        public float t_enter_read = -1f;
        public float t_enter_answer = -1f;
        public float t_enter_submit = -1f;
        public float t_enter_finished = -1f;

        public int rawEventCount = 0;

        public void SetIfUnset(ALLCONTROL.QuestionStage st, float t)
        {
            switch (st)
            {
                case ALLCONTROL.QuestionStage.Read:
                    if (t_enter_read < 0f) t_enter_read = t;
                    break;
                case ALLCONTROL.QuestionStage.Answer:
                    if (t_enter_answer < 0f) t_enter_answer = t;
                    break;
                case ALLCONTROL.QuestionStage.Submit:
                    if (t_enter_submit < 0f) t_enter_submit = t;
                    break;
                case ALLCONTROL.QuestionStage.Finished:
                    if (t_enter_finished < 0f) t_enter_finished = t;
                    break;
            }
        }
    }

    Dictionary<string, MarkTimes> BuildBuckets(List<ALLCONTROL.StageEvent> events)
    {
        // enterCountDerived per index
        var enter = new Dictionary<int, int>();
        var started = new Dictionary<int, bool>();

        var buckets = new Dictionary<string, MarkTimes>(256);

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            int idx = e.index;

            if (!enter.ContainsKey(idx)) enter[idx] = 0;
            if (!started.ContainsKey(idx)) started[idx] = false;

            // 第一次看到该题的任何事件：enter=1（保证 Qx-1 存在）
            if (!started[idx])
            {
                enter[idx] = 1;
                started[idx] = true;
            }
            else
            {
                // 只有 “进入 Read 且不是 Read->Read” 才视为新一轮 enter
                if (e.toStage == ALLCONTROL.QuestionStage.Read &&
                    e.fromStage != ALLCONTROL.QuestionStage.Read)
                {
                    enter[idx] += 1;
                }
            }

            int ent = Mathf.Max(1, enter[idx]);
            string mark = $"Q{idx + 1}-{ent}";
            string key = $"{idx}#{ent}";

            if (!buckets.TryGetValue(key, out var mt))
            {
                mt = new MarkTimes
                {
                    mark = mark,
                    itemId = e.itemId,
                    index0 = idx,
                    qNumber1 = idx + 1,
                    enterDerived = ent
                };
                buckets[key] = mt;
            }

            // 你要的是 stage event 里记录的时间：事件发生时刻 = “进入 toStage 的时间”
            mt.SetIfUnset(e.toStage, e.realtime);
            mt.rawEventCount += 1;
        }

        return buckets;
    }

    // -----------------------------
    // CSV builders
    // -----------------------------
    string BuildAggCsv(Dictionary<string, MarkTimes> buckets)
    {
        var list = new List<MarkTimes>(buckets.Values);
        list.Sort((a, b) =>
        {
            int c = a.index0.CompareTo(b.index0);
            if (c != 0) return c;
            return a.enterDerived.CompareTo(b.enterDerived);
        });

        var sb = new System.Text.StringBuilder(64 * 1024);
        sb.AppendLine("mark,itemId,index0,qNumber1,enterCountDerived,t_enter_read,t_enter_answer,t_enter_submit,t_enter_finished,rawEventCount");

        for (int i = 0; i < list.Count; i++)
        {
            var mt = list[i];
            sb.AppendLine(string.Join(",",
                Csv(mt.mark),
                Csv(mt.itemId),
                mt.index0,
                mt.qNumber1,
                mt.enterDerived,
                F(mt.t_enter_read),
                F(mt.t_enter_answer),
                F(mt.t_enter_submit),
                F(mt.t_enter_finished),
                mt.rawEventCount
            ));
        }

        return sb.ToString();
    }

    string BuildRawCsv(List<ALLCONTROL.StageEvent> events)
    {
        var sb = new System.Text.StringBuilder(128 * 1024);
        sb.AppendLine("itemId,index0,qNumber1,fromStage,toStage,realtime,utc");

        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            sb.AppendLine(string.Join(",",
                Csv(e.itemId),
                e.index,
                (e.index + 1),
                Csv(e.fromStage.ToString()),
                Csv(e.toStage.ToString()),
                F(e.realtime),
                Csv(e.utc)
            ));
        }

        return sb.ToString();
    }

    static string Csv(string s)
    {
        if (s == null) return "";
        bool mustQuote = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
        if (!mustQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static string F(float v)
    {
        if (v < 0f || float.IsNaN(v) || float.IsInfinity(v)) return "";
        return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }
}
