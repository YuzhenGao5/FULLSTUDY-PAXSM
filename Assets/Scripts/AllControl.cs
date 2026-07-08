// 文件名：ALLCONTROL.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ALLCONTROL : MonoBehaviour
{
    // ====== 全局单例 ======
    public static ALLCONTROL Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ALLCONTROL] 已经有一个 Instance 了，销毁这个重复的", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("[ALLCONTROL] Awake：设为全局 Instance", this);
    }

    // ====== 每道题的模式/阶段 ======
    public enum QuestionStage { Read, Answer, Submit, Finished }

    // ====== 全局 UI 模式（所有题同一种） ======
    private enum AnswerUIMode { Card, Slider }

    [Header("全局 UI 模式（从 JSON 读取）")]
    [Tooltip("从 reader.data.default_mode 读取：'card' 或 'slider'。所有题统一。")]
    [SerializeField] private AnswerUIMode uiMode = AnswerUIMode.Card;

    bool IsCardMode => uiMode == AnswerUIMode.Card;
    bool IsSliderMode => uiMode == AnswerUIMode.Slider;

    // ====== ✅ NEW：同题 SetCurrentQuestion 去重（修复 Q1-2） ======
    [Header("✅ EnterCount Fix")]
    [Tooltip("true：如果 SetCurrentQuestion 被重复调用到同一题（尤其启动时 Q0），不再 enterCount++，而是当作 Refresh。")]
    public bool treatSameIndexAsRefresh = true;

    [Tooltip("同题 Refresh 是否仍然触发 OnCurrentQuestionChanged（默认 false 更安全）")]
    public bool invokeOnChangeOnRefresh = false;

    // ====== 数据结构定义 ======
    [System.Serializable]
    public struct ItemAnswerState
    {
        public string itemId;
        public int index;
        public bool hasAnswer;
        public int selectedSlot;
        public int selfConfidence;

        public QuestionStage stage;

        // 每题进入次数（只有“真实进入”才 +1：切题回来 or redo）
        public int enterCount;
        public int redoCount;
    }

    [System.Serializable]
    public struct StageEvent
    {
        public string itemId;
        public int index;
        public QuestionStage fromStage;
        public QuestionStage toStage;
        public float realtime;
        public string utc;
    }

    [System.Serializable]
    public struct RecordEvent
    {
        public string itemId;
        public int index;
        public QuestionStage stage;
        public string tag;
        public string data;
        public float realtime;
        public string utc;
    }

    [Header("题目来源（JSON）")]
    public SimpleJsonReader reader;

    [Header("全局回答数据集（只在这里存/改）")]
    public List<ItemAnswerState> answers = new List<ItemAnswerState>();

    [Header("当前展示的题号（0-based，全局通用）")]
    public int currentIndex = 0;

    [Header("视觉高亮引用（可选）")]
    public OptionHighLight cardsHighLight;
    public SliderTickHighLight sliderHighLight;
    public ConfidenceHighLight confidenceHighLight;

    [Header("Knob 旋钮（可选）")]
    public KnobCore knobCore;

    [Header("Stage Controllers (Optional)")]
    public PanelAnswerRevealAndChoose answerStageController;
    public PanelSubmitMode_EnterOnGrab SumitStageController;

    // ====== 只读属性 / 方法 ======
    public int TotalQuestions => answers != null ? answers.Count : 0;

    public int AnsweredCount
    {
        get
        {
            if (answers == null) return 0;
            int c = 0;
            foreach (var st in answers) if (st.hasAnswer) c++;
            return c;
        }
    }

    public bool IsAnswered(int index)
    {
        if (answers == null) return false;
        if (index < 0 || index >= answers.Count) return false;
        return answers[index].hasAnswer;
    }

    // ====== 阶段 / 进入次数读取 ======
    public QuestionStage GetStage(int index)
    {
        if (answers == null) return QuestionStage.Read;
        if (index < 0 || index >= answers.Count) return QuestionStage.Read;
        return answers[index].stage;
    }

    public QuestionStage GetStageForCurrent() => GetStage(currentIndex);

    public int GetEnterCount(int index)
    {
        if (answers == null) return 0;
        if (index < 0 || index >= answers.Count) return 0;
        return answers[index].enterCount;
    }

    public int GetEnterCountForCurrent() => GetEnterCount(currentIndex);

    // ====== 事件 ======
    public Action<int> OnCurrentQuestionChanged;
    public Action<int, bool> OnAnswerChanged;
    public Action<int, QuestionStage> OnStageChanged;

    [Header("（可选）阶段切换日志")]
    public List<StageEvent> stageEvents = new List<StageEvent>();

    [Header("（可选）通用打点日志 Record()")]
    public List<RecordEvent> recordEvents = new List<RecordEvent>();

    // ====== 启动时建库 ======
    IEnumerator Start()
    {
        yield return null;

        Debug.Log("[ALLCONTROL] Start：准备从 reader 构建答案表");
        BuildAnswerTableFromReader();

        DetectAndApplyGlobalUIModeFromJson();

        // ✅ 启动第一次进入：强制走“真实进入”，让 Q1 enterCount=1
        if (answers != null && answers.Count > 0)
        {
            currentIndex = Mathf.Clamp(currentIndex, 0, answers.Count - 1);
            SetCurrentQuestion(currentIndex, forceEnter: true);
        }
    }

    // ====== ✅ 全局模式读取 & 硬隔离 ======
    void DetectAndApplyGlobalUIModeFromJson()
    {
        string mode = "card";

        if (reader != null && reader.data != null)
        {
            try
            {
                if (!string.IsNullOrEmpty(reader.data.default_mode))
                    mode = reader.data.default_mode;
            }
            catch { }
        }

        mode = (mode ?? "card").Trim().ToLower();
        uiMode = (mode == "slider") ? AnswerUIMode.Slider : AnswerUIMode.Card;

        Debug.Log($"[ALLCONTROL] ✅ Global UI Mode from JSON: default_mode='{mode}' -> uiMode={uiMode}");

        ApplyUIModeGates();
    }

    void ApplyUIModeGates()
    {
        if (cardsHighLight != null) cardsHighLight.enabled = IsCardMode;
        if (sliderHighLight != null) sliderHighLight.enabled = IsSliderMode;

        Debug.Log($"[ALLCONTROL] ApplyUIModeGates -> CardHighLight.enabled={(cardsHighLight ? cardsHighLight.enabled : false)}, " +
                  $"SliderHighLight.enabled={(sliderHighLight ? sliderHighLight.enabled : false)}");
    }

    // ====== 核心：切题强制回收浮动面板（防残留抢交互） ======
    void ForceRetractFloatingPanels()
    {
        if (SumitStageController != null)
        {
            SumitStageController.ExitSubmitMode(
                revertStageToRead: false,
                restorePosition: true,
                hideSubmitUI: true
            );
        }

        if (answerStageController != null)
        {
            answerStageController.ExitAnswerMode(
                revertStageToRead: false,
                restorePosition: true,
                hideOptions: true
            );
        }
    }

    // ====== 从 JSON 建立“题目-答案库” ======
    void BuildAnswerTableFromReader()
    {
        answers.Clear();

        if (reader == null)
        {
            Debug.LogError("[ALLCONTROL] BuildAnswerTableFromReader：reader == null，没有拖 SimpleJsonReader");
            return;
        }

        if (reader.data == null || reader.data.items == null)
        {
            Debug.LogError("[ALLCONTROL] BuildAnswerTableFromReader：reader.data 或 items 为空，JSON 可能没解析成功");
            return;
        }

        var items = reader.data.items;
        Debug.Log($"[ALLCONTROL] BuildAnswerTableFromReader：读取到题目数量 = {items.Count}");

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            ItemAnswerState st = new ItemAnswerState
            {
                itemId = item.id,
                index = i,
                hasAnswer = false,
                selectedSlot = -1,
                selfConfidence = -1,
                stage = QuestionStage.Read,
                enterCount = 0,
                redoCount = 0
            };

            answers.Add(st);
            Debug.Log($"[ALLCONTROL]  -> 建立题目 [{i}] id='{item.id}', 初始：未作答 stage=Read enterCount=0 selfConfidence=-1");
        }

        Debug.Log($"[ALLCONTROL] ✅ 答案表建立完成，总题数 = {answers.Count}");
    }

    // ====== ✅ Finished 点击 redo!（建议你的按钮直接调用这个） ======
    public void RedoCurrentQuestionToRead(bool logEvent = true)
    {
        if (answers == null || answers.Count == 0) return;
        if (currentIndex < 0 || currentIndex >= answers.Count) return;

        ForceRetractFloatingPanels();

        var st = answers[currentIndex];

        // ✅ redo 也算“再次进入同一题”（这样 mark=Qx-enterCount 会变化）
        st.enterCount = Mathf.Max(0, st.enterCount) + 1;
        st.redoCount += 1;

        // ✅ 清空作答/自信
        st.hasAnswer = false;
        st.selectedSlot = -1;
        st.selfConfidence = -1;

        // ✅ 回到 Read
        st.stage = QuestionStage.Read;

        answers[currentIndex] = st;

        OnAnswerChanged?.Invoke(currentIndex, false);

        if (logEvent)
        {
            Record("Redo", $"clear answer+confidence; redoCount={st.redoCount}; enterCount={st.enterCount}");
        }

        // ✅ 强制发 stage changed（很多 UI/Knob 监听这个）
        OnStageChanged?.Invoke(currentIndex, QuestionStage.Read);

        // ✅ 旋钮复位
        if (knobCore != null) knobCore.InitToMiddle();

        // ✅ 高亮复位
        if (IsCardMode && cardsHighLight != null) cardsHighLight.ResetVisual();
        if (IsSliderMode && sliderHighLight != null) sliderHighLight.ResetVisual();
        if (confidenceHighLight != null) confidenceHighLight.ResetVisual();

        ApplyLayoutFromCurrentQuestionState();

        // ✅ 通知“当前题刷新”（保证第一题也能正常刷新 UI）
        OnCurrentQuestionChanged?.Invoke(currentIndex);

        Debug.Log($"[ALLCONTROL] ✅ RedoCurrentQuestionToRead -> Q[{currentIndex}] redoCount={st.redoCount}, enterCount={st.enterCount}, cleared and back to Read");
    }

    // 兼容：如果你按钮里叫 RedoCurrentQuestion()，也给一个别名
    public void RedoCurrentQuestion() => RedoCurrentQuestionToRead(true);

    // ====== ✅ 关键修复：SetCurrentQuestion 同题不再 enterCount++（但“第一次进入”必须走正常流程） ======
    public void SetCurrentQuestion(int itemIndex, bool forceEnter = false)
    {
        if (answers == null || answers.Count == 0)
        {
            Debug.LogWarning("[ALLCONTROL] SetCurrentQuestion：answers 为空，还没建库？");
            return;
        }

        int clamped = Mathf.Clamp(itemIndex, 0, answers.Count - 1);
        int fromIndex = currentIndex;
        bool sameIndex = (clamped == fromIndex);

        // 当前题状态（用于判断是不是“第一次进入”）
        var curBefore = answers[clamped];
        bool isFirstEnterOfThisIndex = (curBefore.enterCount <= 0);

        // ✅ 同题重复调用：只有在“不是第一次进入”且没有 forceEnter 时，才当 refresh
        if (sameIndex && treatSameIndexAsRefresh && !forceEnter && !isFirstEnterOfThisIndex)
        {
            ForceRetractFloatingPanels();

            currentIndex = clamped;
            var st = answers[currentIndex];

            Debug.Log($"[ALLCONTROL] SetCurrentQuestion(REFRESH)：Q[{currentIndex}] id='{st.itemId}' " +
                      $"hasAnswer={st.hasAnswer}, slot={st.selectedSlot}, stage={st.stage}, enterCount={st.enterCount}, selfConfidence={st.selfConfidence}");

            ApplyLayoutFromCurrentQuestionState();

            // 旋钮/视觉恢复
            if (knobCore != null)
            {
                if (st.hasAnswer && st.selectedSlot > 0) knobCore.SnapTo(st.selectedSlot);
                else knobCore.InitToMiddle();
            }

            if (!st.hasAnswer)
            {
                if (IsCardMode && cardsHighLight != null) cardsHighLight.ResetVisual();
                if (IsSliderMode && sliderHighLight != null) sliderHighLight.ResetVisual();
                if (confidenceHighLight != null) confidenceHighLight.ResetVisual();
            }
            else
            {
                if (st.selectedSlot > 0)
                {
                    if (IsCardMode && cardsHighLight != null) cardsHighLight.RestoreConfirmedVisual(st.selectedSlot);
                    if (IsSliderMode && sliderHighLight != null) sliderHighLight.RestoreConfirmedVisual(st.selectedSlot);

                    if (confidenceHighLight != null)
                    {
                        if (st.selfConfidence > 0) confidenceHighLight.RestoreConfirmedVisual(st.selfConfidence);
                        else confidenceHighLight.ResetVisual();
                    }
                }
            }

            Record("RefreshQuestion", "sameIndex refresh (no enterCount++)");

            if (invokeOnChangeOnRefresh)
                OnCurrentQuestionChanged?.Invoke(currentIndex);

            return;
        }

        // ✅ 真切题 or 强制进入：先清场
        ForceRetractFloatingPanels();

        currentIndex = clamped;

        // ✅ 只有“真实进入/强制进入”才 +1
        var st2 = answers[currentIndex];
        st2.enterCount = Mathf.Max(0, st2.enterCount) + 1;
        answers[currentIndex] = st2;

        bool firstEnter = (st2.enterCount == 1);

        Debug.Log($"[ALLCONTROL] SetCurrentQuestion：题[{currentIndex}] id='{st2.itemId}' " +
                  $"hasAnswer={st2.hasAnswer}, slot={st2.selectedSlot}, stage={st2.stage}, enterCount={st2.enterCount}, selfConfidence={st2.selfConfidence}");

        if (firstEnter)
        {
            // ✅ 第一次进入该题：强制从 Read 开始
            SetStageForCurrent(QuestionStage.Read, force: true, logEvent: true);
            ForceRetractFloatingPanels();
        }
        else
        {
            ApplyLayoutFromCurrentQuestionState();
        }

        if (knobCore != null)
        {
            if (st2.hasAnswer && st2.selectedSlot > 0)
            {
                knobCore.SnapTo(st2.selectedSlot);
                Debug.Log($"[ALLCONTROL] KnobCore.SnapTo({st2.selectedSlot})");
            }
            else
            {
                knobCore.InitToMiddle();
                Debug.Log("[ALLCONTROL] 当前题未作答 -> KnobCore.InitToMiddle()");
            }
        }

        if (!st2.hasAnswer)
        {
            if (IsCardMode && cardsHighLight != null) cardsHighLight.ResetVisual();
            if (IsSliderMode && sliderHighLight != null) sliderHighLight.ResetVisual();
            if (confidenceHighLight != null) confidenceHighLight.ResetVisual();
        }
        else
        {
            if (st2.selectedSlot > 0)
            {
                if (IsCardMode && cardsHighLight != null) cardsHighLight.RestoreConfirmedVisual(st2.selectedSlot);
                if (IsSliderMode && sliderHighLight != null) sliderHighLight.RestoreConfirmedVisual(st2.selectedSlot);

                if (confidenceHighLight != null)
                {
                    if (st2.selfConfidence > 0) confidenceHighLight.RestoreConfirmedVisual(st2.selfConfidence);
                    else confidenceHighLight.ResetVisual();
                }
            }
        }

        Record("EnterQuestion", "enterCount=" + st2.enterCount);

        OnCurrentQuestionChanged?.Invoke(currentIndex);
    }

    public void GoNextQuestion()
    {
        if (answers == null || answers.Count == 0)
        {
            Debug.LogWarning("[ALLCONTROL] GoNextQuestion：answers 为空，还没建库？");
            return;
        }

        int next = Mathf.Clamp(currentIndex + 1, 0, answers.Count - 1);

        if (next == currentIndex)
        {
            Debug.Log("[ALLCONTROL] GoNextQuestion：已经是最后一题了");
            return;
        }

        int from = currentIndex;
        SetCurrentQuestion(next);
        Record("GoNextQuestion", $"from={from} to={currentIndex}");
    }

    // ====== 写入阶段 ======
    public void SetStageForCurrent(QuestionStage newStage, bool force = false, bool logEvent = true)
    {
        SetStageByIndex(currentIndex, newStage, force, logEvent);
    }

    public void SetStageByIndex(int itemIndex, QuestionStage newStage, bool force = false, bool logEvent = true)
    {
        if (answers == null || answers.Count == 0) return;
        if (itemIndex < 0 || itemIndex >= answers.Count) return;

        var st = answers[itemIndex];
        var old = st.stage;

        if (!force && old == newStage) return;

        st.stage = newStage;
        answers[itemIndex] = st;

        if (logEvent)
        {
            stageEvents.Add(new StageEvent
            {
                itemId = st.itemId,
                index = itemIndex,
                fromStage = old,
                toStage = newStage,
                realtime = Time.realtimeSinceStartup,
                utc = DateTime.UtcNow.ToString("o")
            });
        }

        Debug.Log($"[ALLCONTROL] StageChanged: Q[{itemIndex}] {old} -> {newStage}");
        OnStageChanged?.Invoke(itemIndex, newStage);
        Record("StageChanged", $"{old}->{newStage}");
    }

    // ====== 通用记录 ======
    public void Record(string tag, string data = "")
    {
        if (answers == null || answers.Count == 0) return;
        if (currentIndex < 0 || currentIndex >= answers.Count) return;

        var st = answers[currentIndex];

        recordEvents.Add(new RecordEvent
        {
            itemId = st.itemId,
            index = currentIndex,
            stage = st.stage,
            tag = tag,
            data = data,
            realtime = Time.realtimeSinceStartup,
            utc = DateTime.UtcNow.ToString("o")
        });

        Debug.Log($"[ALLCONTROL][Record] Q[{currentIndex}] stage={st.stage} tag={tag} data={data}");
    }

    // ====== ✅ 自动 Finished ======
    void TryAutoFinishCurrent(string reason)
    {
        if (answers == null || answers.Count == 0) return;
        if (currentIndex < 0 || currentIndex >= answers.Count) return;

        var st = answers[currentIndex];

        bool ok = st.hasAnswer && st.selectedSlot > 0 && st.selfConfidence > 0;
        if (!ok) return;

        if (st.stage != QuestionStage.Finished)
        {
            Debug.Log($"[ALLCONTROL] ✅ AutoFinish ({reason}) -> Q[{currentIndex}] set stage = Finished");
            SetStageForCurrent(QuestionStage.Finished, force: true, logEvent: true);
        }

        Record("AutoFinish", reason);
    }

    public void MarkFinishedForCurrent(bool force = true, bool logEvent = true)
    {
        SetStageForCurrent(QuestionStage.Finished, force: force, logEvent: logEvent);
        ForceRetractFloatingPanels();
        Record("FinishedManual", "ok");
    }

    // ====== 写入 self-confidence ======
    public void SetSelfConfidenceForCurrent(int level)
    {
        if (answers == null || answers.Count == 0)
        {
            Debug.LogWarning("[ALLCONTROL] SetSelfConfidenceForCurrent：answers 为空，还没建库？");
            return;
        }

        if (currentIndex < 0 || currentIndex >= answers.Count)
        {
            Debug.LogWarning($"[ALLCONTROL] SetSelfConfidenceForCurrent：currentIndex={currentIndex} 越界");
            return;
        }

        level = Mathf.Max(1, level);

        var st = answers[currentIndex];
        st.selfConfidence = level;
        answers[currentIndex] = st;

        Debug.Log($"[ALLCONTROL] SetSelfConfidenceForCurrent：题[{currentIndex}] id='{st.itemId}' 写入 selfConfidence={level}");
        Record("SelfConfidenceSet", "level=" + level);

        TryAutoFinishCurrent("afterConfidence");
    }

    // ====== 读取答案（给 SliderHighLight / 其它脚本用） ======
    public bool TryGetAnswerByIndex(int itemIndex, out int slot)
    {
        slot = -1;

        if (answers == null || answers.Count == 0)
        {
            Debug.LogWarning("[ALLCONTROL] TryGetAnswerByIndex：answers 为空，还没建库？");
            return false;
        }

        if (itemIndex < 0 || itemIndex >= answers.Count)
        {
            Debug.LogWarning($"[ALLCONTROL] TryGetAnswerByIndex：index={itemIndex} 越界");
            return false;
        }

        var st = answers[itemIndex];
        if (!st.hasAnswer || st.selectedSlot <= 0)
        {
            return false;
        }

        slot = st.selectedSlot;
        return true;
    }

    public bool TryGetAnswerForCurrent(out int slot)
    {
        return TryGetAnswerByIndex(currentIndex, out slot);
    }

    // ====== 布局切换 ======
    public void ApplyLayoutFromCurrentQuestionState()
    {
        if (answers == null || answers.Count == 0) return;
        if (currentIndex < 0 || currentIndex >= answers.Count) return;

        var st = answers[currentIndex];
        var stage = st.stage;

        Debug.Log($"[ALLCONTROL] ApplyLayoutFromCurrentQuestionState -> Q[{currentIndex}] stage={stage}, hasAnswer={st.hasAnswer}, slot={st.selectedSlot}, selfConfidence={st.selfConfidence}, uiMode={uiMode}");

        if (stage != QuestionStage.Finished)
        {
            ForceRetractFloatingPanels();

            if (IsCardMode && cardsHighLight != null) cardsHighLight.EnablePendingHighlight();
            if (IsSliderMode && sliderHighLight != null) sliderHighLight.EnablePendingHighlight();
            if (confidenceHighLight != null) confidenceHighLight.EnablePendingHighlight();
        }

        if (stage == QuestionStage.Answer)
        {
            if (answerStageController != null) answerStageController.EnterAnswerMode();

            if (IsCardMode && cardsHighLight != null) cardsHighLight.EnablePendingHighlight();
            if (IsSliderMode && sliderHighLight != null) sliderHighLight.EnablePendingHighlight();
            if (confidenceHighLight != null) confidenceHighLight.EnablePendingHighlight();
        }
        else if (stage == QuestionStage.Submit)
        {
            if (answerStageController != null) answerStageController.EnterAnswerMode();
            if (SumitStageController != null) SumitStageController.EnterSubmitMode();

            if (IsCardMode && cardsHighLight != null) cardsHighLight.DisablePendingHighlightKeepConfirmed();
            if (IsSliderMode && sliderHighLight != null) sliderHighLight.DisablePendingHighlight();
            if (confidenceHighLight != null) confidenceHighLight.DisablePendingHighlightKeepConfirmed();
        }
        else if (stage == QuestionStage.Read)
        {
            if (IsCardMode && cardsHighLight != null) cardsHighLight.EnablePendingHighlight();
            if (IsSliderMode && sliderHighLight != null) sliderHighLight.EnablePendingHighlight();
        }
        else if (stage == QuestionStage.Finished)
        {
            if (answerStageController != null) answerStageController.EnterFinishedAAnswerMode();
            if (SumitStageController != null) SumitStageController.EnterFinishedSubmitMode();

            if (IsCardMode && cardsHighLight != null)
            {
                if (st.selectedSlot > 0) cardsHighLight.RestoreConfirmedVisual(st.selectedSlot);
                cardsHighLight.DisablePendingHighlightKeepConfirmed();
            }

            if (IsSliderMode && sliderHighLight != null)
            {
                if (st.selectedSlot > 0) sliderHighLight.RestoreConfirmedVisual(st.selectedSlot);
                sliderHighLight.DisablePendingHighlight();
            }

            if (confidenceHighLight != null)
            {
                if (st.selfConfidence > 0) confidenceHighLight.RestoreConfirmedVisual(st.selfConfidence);
                confidenceHighLight.DisablePendingHighlightKeepConfirmed();
            }
        }
    }

    // ====== 读 / 写答案 ======
    public void SetAnswerByIndex(int slot)
    {
        if (answers == null || answers.Count == 0)
        {
            Debug.LogWarning("[ALLCONTROL] SetAnswerByIndex：answers 为空，还没建库？");
            return;
        }

        if (currentIndex < 0 || currentIndex >= answers.Count)
        {
            Debug.LogWarning($"[ALLCONTROL] SetAnswerByIndex：currentIndex={currentIndex} 越界");
            return;
        }

        slot = Mathf.Max(1, slot);

        var st = answers[currentIndex];
        st.hasAnswer = true;
        st.selectedSlot = slot;
        answers[currentIndex] = st;

        Debug.Log($"[ALLCONTROL] SetAnswerByIndex：题[{currentIndex}] id='{st.itemId}' 写入答案 slot={slot}");
        OnAnswerChanged?.Invoke(currentIndex, true);
        Record("AnswerSet", "slot=" + slot);

        TryAutoFinishCurrent("afterAnswer");
    }

    public void SetSelfConfidenceByIndex(int itemIndex, int level)
    {
        if (answers == null || answers.Count == 0) return;
        if (itemIndex < 0 || itemIndex >= answers.Count) return;

        level = Mathf.Max(1, level);

        var st = answers[itemIndex];
        st.selfConfidence = level;
        answers[itemIndex] = st;

        Debug.Log($"[ALLCONTROL] SetSelfConfidenceByIndex：题[{itemIndex}] id='{st.itemId}' 写入 selfConfidence={level}");
    }

    public bool TryGetSelfConfidenceForCurrent(out int level)
    {
        level = -1;
        if (answers == null || answers.Count == 0) return false;
        if (currentIndex < 0 || currentIndex >= answers.Count) return false;

        level = answers[currentIndex].selfConfidence;
        return level >= 0;
    }

    public bool TryGetSelfConfidenceByIndex(int itemIndex, out int level)
    {
        level = -1;
        if (answers == null || answers.Count == 0) return false;
        if (itemIndex < 0 || itemIndex >= answers.Count) return false;

        level = answers[itemIndex].selfConfidence;
        return level >= 0;
    }
}
