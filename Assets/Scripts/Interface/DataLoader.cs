// 文件名：KnobBehaviorMergedCSVExporter.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class KnobBehaviorMergedCSVExporter : MonoBehaviour
{
    [Header("✅ Drag These Two Knobs (手动拖拽，不自动搜索)")]
    public KnobCore answerKnob;       // A
    public KnobCore confidenceKnob;   // C

    [Header("Experiment Meta (Inspector 填)")]
    public int participantNumber = 1;
    public int sessionNumber = 1;
    public string conditionLabel = "Pilot";

    [Header("Row Filtering")]
    [Tooltip("默认 true：跳过 qIndex1<=0 或 itemId 为空的 NA 行")]
    public bool skipInvalidNA = true;

    [Tooltip("如果 Summary 里 s.mark 不是 'qIndex1-enterCount'，打印一条警告")]
    public bool warnIfSummaryMarkNotMatch = true;

    [Header("✅ Mark Output Format")]
    [Tooltip("markLabel/markBase 输出前缀。默认 'Q' -> Q1-2。")]
    public string markPrefix = "Q";

    // =========================
    // Output Location
    // =========================
    public enum OutputMode
    {
        ProjectAssets,
        PersistentDataPath,
        CustomAbsolutePath
    }

    [Header("Output Location")]
    public OutputMode outputMode = OutputMode.ProjectAssets;

    [Tooltip("当 outputMode=CustomAbsolutePath 时生效，例如：D:\\XRExports")]
    public string customAbsoluteFolder = "D:\\XRExports";

    [Tooltip("子目录名（会创建实验子文件夹）")]
    public string outputSubfolder = "ExportsCSV";

    [Tooltip("文件名前缀（会自动附加时间戳）")]
    public string fileNamePrefix = "XRQ";

    public bool logExportPath = true;

    [Header("Export Options")]
    public bool autoExportOnQuit = true;

    [Tooltip("手动导出快捷键（New Input System 下可用；XR 上通常没键盘）")]
    public KeyCode manualExportKey = KeyCode.F9;

    private bool _exported = false;

    void Update()
    {
        if (WasManualExportPressed())
            ExportNow("ManualKey");
    }

    void OnApplicationQuit()
    {
        if (autoExportOnQuit && !_exported)
            ExportNow("OnQuit");
    }

    // =========================
    // Input System safe trigger
    // =========================
    bool WasManualExportPressed()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return false;

        if (!TryMapKeyCode(manualExportKey, out var key)) return false;
        return kb[key].wasPressedThisFrame;
#else
        return Input.GetKeyDown(manualExportKey);
#endif
    }

#if ENABLE_INPUT_SYSTEM
    bool TryMapKeyCode(KeyCode kc, out Key key)
    {
        key = Key.None;

        if (kc >= KeyCode.F1 && kc <= KeyCode.F12)
        {
            key = Key.F1 + (kc - KeyCode.F1);
            return true;
        }

        if (kc >= KeyCode.A && kc <= KeyCode.Z)
        {
            key = Key.A + (kc - KeyCode.A);
            return true;
        }

        if (kc >= KeyCode.Alpha0 && kc <= KeyCode.Alpha9)
        {
            key = Key.Digit0 + (kc - KeyCode.Alpha0);
            return true;
        }

        switch (kc)
        {
            case KeyCode.UpArrow: key = Key.UpArrow; return true;
            case KeyCode.DownArrow: key = Key.DownArrow; return true;
            case KeyCode.LeftArrow: key = Key.LeftArrow; return true;
            case KeyCode.RightArrow: key = Key.RightArrow; return true;
            case KeyCode.Return: key = Key.Enter; return true;
            case KeyCode.Space: key = Key.Space; return true;
            case KeyCode.Escape: key = Key.Escape; return true;
        }
        return false;
    }
#endif

    // =========================
    // Export
    // =========================
    public void ExportNow(string reason = "Manual")
    {
        if (_exported) return;

        if (answerKnob == null || confidenceKnob == null)
        {
            Debug.LogError("[KnobMergedCSV] 你还没把 answerKnob / confidenceKnob 两个对象都拖进 Inspector。", this);
            return;
        }

        int aCount = (answerKnob.summaries != null) ? answerKnob.summaries.Count : -1;
        int cCount = (confidenceKnob.summaries != null) ? confidenceKnob.summaries.Count : -1;

        Debug.Log($"[KnobMergedCSV] ExportNow({reason}) " +
                  $"A='{answerKnob.name}' active={answerKnob.gameObject.activeInHierarchy} summaries={aCount} | " +
                  $"C='{confidenceKnob.name}' active={confidenceKnob.gameObject.activeInHierarchy} summaries={cCount}");

        string timestampUTC = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        string runFolderName = $"{fileNamePrefix}_P{participantNumber}_S{sessionNumber}_{conditionLabel}_{timestampUTC}";

        string baseFolder = ResolveBaseFolder();
        string folder = Path.Combine(baseFolder, outputSubfolder, runFolderName);
        Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, $"KnobBehavior_Merged_P{participantNumber}_S{sessionNumber}_{timestampUTC}.csv");
        File.WriteAllText(path, BuildMergedCsv(timestampUTC), Encoding.UTF8);

        _exported = true;

        if (logExportPath)
        {
            Debug.Log($"[KnobMergedCSV] ✅ Exported ({reason})\n- {path}");
            Debug.Log($"[KnobMergedCSV] baseFolder={baseFolder}");
        }
    }

    string ResolveBaseFolder()
    {
        if (outputMode == OutputMode.ProjectAssets)
        {
#if UNITY_EDITOR
            return Application.dataPath;
#else
            return Application.persistentDataPath;
#endif
        }

        if (outputMode == OutputMode.PersistentDataPath)
            return Application.persistentDataPath;

        if (outputMode == OutputMode.CustomAbsolutePath)
            return string.IsNullOrEmpty(customAbsoluteFolder) ? Application.persistentDataPath : customAbsoluteFolder;

        return Application.persistentDataPath;
    }

    // =========================
    // Build merged CSV (A + C in one row)
    // =========================
    class RowPair
    {
        public KnobCore.KnobMarkSummary A;
        public KnobCore.KnobMarkSummary C;
    }

    string BuildMergedCsv(string exportTimestampUTC)
    {
        var sb = new StringBuilder(256 * 1024);

        // ✅ 新增：stage 时间与 initiation（你要的“每个阶段耗时”）
        sb.AppendLine(string.Join(",",
            "participantNumber","sessionNumber","conditionLabel",
            "markLabel","markBase","itemId","qIndex0","qIndex1","enterCount",
            "stage(A)","stage(C)",
            "knobObjectName(A)","knobActiveInHierarchy(A)",
            "knobObjectName(C)","knobActiveInHierarchy(C)",

            // ---- A stage timing ----
            "t_read_in(A)","t_read_out(A)","rt_read(A)",
            "t_answer_in(A)","t_answer_out(A)","rt_answer(A)",
            "t_conf_in(A)","t_conf_out(A)","rt_conf(A)",
            "t_firstMove_answer(A)","rt_initiation_answer(A)",
            "t_firstMove_conf(A)","rt_initiation_conf(A)",

            // ---- A behavior ----
            "tickCount","currentSlot","currentAngleY",
            "slotChangeCount","reverseCount","pauseCount","confirmCount",
            "minSlot","maxSlot","slotSpan","uniqueSlotsVisited",

            // ✅ 四类时间（应闭合到启用统计的 stage RT）
            "stillTimeSum",
            "microAdjustTimeSum","microAdjustCount",
            "normalAdjustTimeSum","normalAdjustCount",
            "flickTimeSum","fastFlickCount",
            "maxFlickVel","maxAbsVel",
            "activeMoveTimeSum","activeMoveCount",
            "totalAbsAngle",

            // diagnostics
            "speedBandValid","speedMedian","speedMAD","speedThLow","speedThHigh","speedBandNote",

            // ---- C stage timing ----
            "t_read_in(C)","t_read_out(C)","rt_read(C)",
            "t_answer_in(C)","t_answer_out(C)","rt_answer(C)",
            "t_conf_in(C)","t_conf_out(C)","rt_conf(C)",
            "t_firstMove_answer(C)","rt_initiation_answer(C)",
            "t_firstMove_conf(C)","rt_initiation_conf(C)",

            // ---- C behavior ----
            "tickCount(C)","currentSlot(C)","currentAngleY(C)",
            "slotChangeCount(C)","reverseCount(C)","pauseCount(C)","confirmCount(C)",
            "minSlot(C)","maxSlot(C)","slotSpan(C)","uniqueSlotsVisited(C)",

            "stillTimeSum(C)",
            "microAdjustTimeSum(C)","microAdjustCount(C)",
            "normalAdjustTimeSum(C)","normalAdjustCount(C)",
            "flickTimeSum(C)","fastFlickCount(C)",
            "maxFlickVel(C)","maxAbsVel(C)",
            "activeMoveTimeSum(C)","activeMoveCount(C)",
            "totalAbsAngle(C)",

            "speedBandValid(C)","speedMedian(C)","speedMAD(C)","speedThLow(C)","speedThHigh(C)","speedBandNote(C)",

            "exportTimestampUTC"
        ));

        var map = new Dictionary<string, RowPair>(256);
        MergeInto(map, answerKnob, isA: true);
        MergeInto(map, confidenceKnob, isA: false);

        var keys = new List<string>(map.Keys);
        keys.Sort((k1, k2) =>
        {
            var r1 = map[k1];
            var r2 = map[k2];

            int q1 = GetQIndex1(r1) - GetQIndex1(r2);
            if (q1 != 0) return q1;

            int e1 = GetEnter(r1) - GetEnter(r2);
            if (e1 != 0) return e1;

            return string.CompareOrdinal(k1, k2);
        });

        string knobNameA = answerKnob ? answerKnob.name : "";
        string knobNameC = confidenceKnob ? confidenceKnob.name : "";

        string activeA = (answerKnob && answerKnob.gameObject.activeInHierarchy) ? "1" : "0";
        string activeC = (confidenceKnob && confidenceKnob.gameObject.activeInHierarchy) ? "1" : "0";

        foreach (var key in keys)
        {
            var pair = map[key];
            var A = pair.A;
            var C = pair.C;

            string itemId = GetStr(A, "itemId", "") != "" ? GetStr(A, "itemId", "") : GetStr(C, "itemId", "");
            int qIndex0 = GetInt(A, "qIndex0", GetInt(C, "qIndex0", -1));
            int qIndex1 = GetInt(A, "qIndex1", GetInt(C, "qIndex1", 0));
            int enterCount = GetInt(A, "enterCount", GetInt(C, "enterCount", 0));

            string stageA = GetStr(A, "stage", "");
            string stageC = GetStr(C, "stage", "");

            string safeMark = $"{markPrefix}{qIndex1}-{enterCount}";
            string markLabel = safeMark;
            string markBase = safeMark;

            // 兼容：activeMove = micro+normal+flick（如果脚本里已经这样写）
            float aActiveT = GetFloat(A, "activeMoveTimeSum", -1f);
            int   aActiveC = GetInt(A, "activeMoveCount", -1);

            float cActiveT = GetFloat(C, "activeMoveTimeSum", -1f);
            int   cActiveC = GetInt(C, "activeMoveCount", -1);

            sb.AppendLine(string.Join(",",
                participantNumber,
                sessionNumber,
                Csv(conditionLabel),

                Csv(markLabel),
                Csv(markBase),
                Csv(itemId),
                qIndex0,
                qIndex1,
                enterCount,

                Csv(stageA),
                Csv(stageC),

                Csv(knobNameA),
                activeA,
                Csv(knobNameC),
                activeC,

                // ---- A stage timing ----
                F(GetFloat(A, "t_read_in", -1f)),
                F(GetFloat(A, "t_read_out", -1f)),
                F(GetRT(A, "RT_Read")),

                F(GetFloat(A, "t_answer_in", -1f)),
                F(GetFloat(A, "t_answer_out", -1f)),
                F(GetRT(A, "RT_Answer")),

                F(GetFloat(A, "t_conf_in", -1f)),
                F(GetFloat(A, "t_conf_out", -1f)),
                F(GetRT(A, "RT_Conf")),

                F(GetFloat(A, "t_firstMove_answer", -1f)),
                F(GetRT(A, "RT_Initiation_Answer")),

                F(GetFloat(A, "t_firstMove_conf", -1f)),
                F(GetRT(A, "RT_Initiation_Conf")),

                // ---- A behavior ----
                I(GetInt(A, "tickCount", -1)),
                I(GetInt(A, "currentSlot", -1)),
                F(GetFloat(A, "currentAngleY", -1f)),

                I(GetInt(A, "slotChangeCount", -1)),
                I(GetInt(A, "reverseCount", -1)),
                I(GetInt(A, "pauseCount", -1)),
                I(GetInt(A, "confirmCount", -1)),

                MinSlot(A),
                MaxSlot(A),
                SlotSpan(A),
                I(GetInt(A, "uniqueSlotsVisited", -1)),

                F(GetFloat(A, "stillTimeSum", -1f)),
                F(GetFloat(A, "microAdjustTimeSum", -1f)),
                I(GetInt(A, "microAdjustCount", -1)),
                F(GetFloat(A, "normalAdjustTimeSum", -1f)),
                I(GetInt(A, "normalAdjustCount", -1)),
                F(GetFloat(A, "flickTimeSum", -1f)),
                I(GetInt(A, "fastFlickCount", -1)),

                F(GetFloat(A, "maxFlickVel", -1f)),
                F(GetFloat(A, "maxAbsVel", -1f)),

                F(aActiveT),
                I(aActiveC),

                F(GetFloat(A, "totalAbsAngle", -1f)),

                B(GetBool(A, "speedClassificationValid", false)),
                F(GetFloat(A, "speedMedian", -1f)),
                F(GetFloat(A, "speedMAD", -1f)),
                F(GetFloat(A, "speedThLow", -1f)),
                F(GetFloat(A, "speedThHigh", -1f)),
                Csv(GetStr(A, "speedClassificationNote", "")),

                // ---- C stage timing ----
                F(GetFloat(C, "t_read_in", -1f)),
                F(GetFloat(C, "t_read_out", -1f)),
                F(GetRT(C, "RT_Read")),

                F(GetFloat(C, "t_answer_in", -1f)),
                F(GetFloat(C, "t_answer_out", -1f)),
                F(GetRT(C, "RT_Answer")),

                F(GetFloat(C, "t_conf_in", -1f)),
                F(GetFloat(C, "t_conf_out", -1f)),
                F(GetRT(C, "RT_Conf")),

                F(GetFloat(C, "t_firstMove_answer", -1f)),
                F(GetRT(C, "RT_Initiation_Answer")),

                F(GetFloat(C, "t_firstMove_conf", -1f)),
                F(GetRT(C, "RT_Initiation_Conf")),

                // ---- C behavior ----
                I(GetInt(C, "tickCount", -1)),
                I(GetInt(C, "currentSlot", -1)),
                F(GetFloat(C, "currentAngleY", -1f)),

                I(GetInt(C, "slotChangeCount", -1)),
                I(GetInt(C, "reverseCount", -1)),
                I(GetInt(C, "pauseCount", -1)),
                I(GetInt(C, "confirmCount", -1)),

                MinSlot(C),
                MaxSlot(C),
                SlotSpan(C),
                I(GetInt(C, "uniqueSlotsVisited", -1)),

                F(GetFloat(C, "stillTimeSum", -1f)),
                F(GetFloat(C, "microAdjustTimeSum", -1f)),
                I(GetInt(C, "microAdjustCount", -1)),
                F(GetFloat(C, "normalAdjustTimeSum", -1f)),
                I(GetInt(C, "normalAdjustCount", -1)),
                F(GetFloat(C, "flickTimeSum", -1f)),
                I(GetInt(C, "fastFlickCount", -1)),

                F(GetFloat(C, "maxFlickVel", -1f)),
                F(GetFloat(C, "maxAbsVel", -1f)),

                F(cActiveT),
                I(cActiveC),

                F(GetFloat(C, "totalAbsAngle", -1f)),

                B(GetBool(C, "speedClassificationValid", false)),
                F(GetFloat(C, "speedMedian", -1f)),
                F(GetFloat(C, "speedMAD", -1f)),
                F(GetFloat(C, "speedThLow", -1f)),
                F(GetFloat(C, "speedThHigh", -1f)),
                Csv(GetStr(C, "speedClassificationNote", "")),

                Csv(exportTimestampUTC)
            ));
        }

        return sb.ToString();
    }

    // ✅ key 用 qIndex1-enterCount（内部合并用）
    void MergeInto(Dictionary<string, RowPair> map, KnobCore knob, bool isA)
    {
        if (knob == null || knob.summaries == null) return;

        foreach (var s in knob.summaries)
        {
            if (s == null) continue;

            int q1 = GetInt(s, "qIndex1", 0);
            int enter = GetInt(s, "enterCount", 0);
            string itemId = GetStr(s, "itemId", "");

            if (skipInvalidNA)
            {
                if (q1 <= 0) continue;
                if (string.IsNullOrEmpty(itemId)) continue;
            }

            string computedKey = $"{q1}-{enter}";

            if (warnIfSummaryMarkNotMatch)
            {
                string mark = GetStr(s, "mark", "");
                if (!string.IsNullOrEmpty(mark) && mark != computedKey && !mark.StartsWith("Q"))
                {
                    Debug.LogWarning($"[KnobMergedCSV] Summary.mark 异常：summary.mark='{mark}' 但应为 '{computedKey}'。");
                }
            }

            if (!map.TryGetValue(computedKey, out var pair))
            {
                pair = new RowPair();
                map[computedKey] = pair;
            }

            if (isA) pair.A = s;
            else pair.C = s;
        }
    }

    int GetQIndex1(RowPair r)
    {
        if (r.A != null) return GetInt(r.A, "qIndex1", 0);
        if (r.C != null) return GetInt(r.C, "qIndex1", 0);
        return 0;
    }

    int GetEnter(RowPair r)
    {
        if (r.A != null) return GetInt(r.A, "enterCount", 0);
        if (r.C != null) return GetInt(r.C, "enterCount", 0);
        return 0;
    }

    // =========================
    // Reflection-safe getters (compile-time compatible)
    // =========================
    static readonly BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    static FieldInfo FindField(object obj, string name)
    {
        if (obj == null) return null;
        return obj.GetType().GetField(name, BF);
    }

    static int GetInt(object obj, string field, int def)
    {
        try
        {
            var f = FindField(obj, field);
            if (f == null) return def;
            object v = f.GetValue(obj);
            if (v == null) return def;
            if (v is int i) return i;
            return Convert.ToInt32(v);
        }
        catch { return def; }
    }

    static float GetFloat(object obj, string field, float def)
    {
        try
        {
            var f = FindField(obj, field);
            if (f == null) return def;
            object v = f.GetValue(obj);
            if (v == null) return def;
            if (v is float x) return x;
            return Convert.ToSingle(v);
        }
        catch { return def; }
    }

    static string GetStr(object obj, string field, string def)
    {
        try
        {
            var f = FindField(obj, field);
            if (f == null) return def;
            object v = f.GetValue(obj);
            return v != null ? v.ToString() : def;
        }
        catch { return def; }
    }

    static bool GetBool(object obj, string field, bool def)
    {
        try
        {
            var f = FindField(obj, field);
            if (f == null) return def;
            object v = f.GetValue(obj);
            if (v == null) return def;
            if (v is bool b) return b;
            return Convert.ToBoolean(v);
        }
        catch { return def; }
    }

    // ✅ 调用方法 RT_Read()/RT_Answer()/RT_Conf()/RT_Initiation_Answer()/RT_Initiation_Conf()
    static float GetRT(object obj, string methodName)
    {
        if (obj == null) return -1f;
        try
        {
            var m = obj.GetType().GetMethod(methodName, BF);
            if (m == null) return -1f;
            object v = m.Invoke(obj, null);
            return Convert.ToSingle(v);
        }
        catch { return -1f; }
    }

    // =========================
    // Helpers
    // =========================
    static string Csv(string s)
    {
        if (s == null) return "";
        bool mustQuote = s.Contains(",") || s.Contains("\"") || s.Contains("\n") || s.Contains("\r");
        if (!mustQuote) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    static string F(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return "-1";
        return v.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    }

    static string I(int v) => v.ToString();

    static string B(bool v) => v ? "1" : "0";

    static string MinSlot(object s)
    {
        int min = GetInt(s, "minSlot", int.MaxValue);
        return (min == int.MaxValue) ? "-1" : min.ToString();
    }

    static string MaxSlot(object s)
    {
        int max = GetInt(s, "maxSlot", int.MinValue);
        return (max == int.MinValue) ? "-1" : max.ToString();
    }

    static string SlotSpan(object s)
    {
        if (s == null) return "0";
        try
        {
            var m = s.GetType().GetMethod("SlotSpan", BF);
            if (m != null)
            {
                object v = m.Invoke(s, null);
                return Convert.ToInt32(v).ToString();
            }
        }
        catch { }

        int min = GetInt(s, "minSlot", int.MaxValue);
        int max = GetInt(s, "maxSlot", int.MinValue);
        if (min == int.MaxValue || max == int.MinValue) return "0";
        return (max - min).ToString();
    }
}
