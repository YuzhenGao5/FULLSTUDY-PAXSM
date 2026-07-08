using System.Collections;
using UnityEngine;

public class QE_ModeSwitcher : MonoBehaviour
{
    [Header("Data")]
    public SimpleJsonReader reader;   // 拖：Reader (Simple Json Reader)

    [Header("Panels (场景里的子物体)")]
    public GameObject cardsMode;      // 拖：Viewport 下的 Cardsmode
    public GameObject sliderMode;     // 拖：Viewport 下的 Slidermode

    // 用协程，等一帧，保证 SimpleJsonReader.Start() 已经跑完
    IEnumerator Start()
    {
        yield return null;   // 等一帧
        ApplyByDefaultMode();
    }

    /// <summary>
    /// 根据 cfg.default_mode 只开一个层级：
    ///  - "slider" → 只开 sliderMode
    ///  - 其他      → 只开 cardsMode
    /// </summary>
    public void ApplyByDefaultMode()
    {
        if (reader == null)
        {
            Debug.LogError("[QE_ModeSwitcher] reader == null");
            SetActiveStates(cardsOn: true, sliderOn: false);
            return;
        }

        var cfg = reader.data;
        if (cfg == null)
        {
            Debug.LogError("[QE_ModeSwitcher] reader.data == null（JSON 还没读到？）");
            SetActiveStates(cardsOn: true, sliderOn: false);
            return;
        }

        string rawMode = cfg.default_mode;
        string mode = string.IsNullOrEmpty(rawMode) ? "cards" : rawMode.ToLowerInvariant();

        bool useSlider = (mode == "slider");

        SetActiveStates(cardsOn: !useSlider, sliderOn: useSlider);

        Debug.Log($"[QE_ModeSwitcher] ApplyByDefaultMode: default_mode(raw)='{rawMode}', " +
                  $"normalized='{mode}', useSlider={useSlider}, " +
                  $"cardsMode.activeSelf={cardsMode?.activeSelf}, " +
                  $"sliderMode.activeSelf={sliderMode?.activeSelf}");
    }

    void SetActiveStates(bool cardsOn, bool sliderOn)
    {
        if (cardsMode != null)
            cardsMode.SetActive(cardsOn);
        else
            Debug.LogWarning("[QE_ModeSwitcher] cardsMode 未绑定（Viewport 下的 Cardsmode 没拖上来）");

        if (sliderMode != null)
            sliderMode.SetActive(sliderOn);
        else
            Debug.LogWarning("[QE_ModeSwitcher] sliderMode 未绑定（Viewport 下的 Slidermode 没拖上来）");
    }

    // ================== 新增：按题目切模式 ==================

    /// <summary>
    /// 根据“第几题”的 mode 切换：
    ///  - 优先用 items[index].mode
    ///  - 没写就退回 cfg.default_mode
    ///  - 都没有就用 cards
    /// </summary>
    public void ApplyModeForItem(int index)
    {
        if (reader == null || reader.data == null)
        {
            Debug.LogWarning("[QE_ModeSwitcher] ApplyModeForItem: cfg 为空，退回默认模式");
            ApplyByDefaultMode();
            return;
        }

        var cfg = reader.data;

        if (cfg.items == null || cfg.items.Count == 0)
        {
            Debug.LogWarning("[QE_ModeSwitcher] ApplyModeForItem: cfg.items 为空，退回默认模式");
            ApplyByDefaultMode();
            return;
        }

        index = Mathf.Clamp(index, 0, cfg.items.Count - 1);
        var item = cfg.items[index];

        // 先看 item.mode，再看 default_mode，最后兜底 "cards"
        string rawMode =
            !string.IsNullOrEmpty(item.mode)      ? item.mode :
            !string.IsNullOrEmpty(cfg.default_mode) ? cfg.default_mode :
            "cards";

        string mode = rawMode.ToLowerInvariant();
        bool useSlider = (mode == "slider");

        SetActiveStates(cardsOn: !useSlider, sliderOn: useSlider);

        Debug.Log($"[QE_ModeSwitcher] ApplyModeForItem[{index}]: item.id='{item.id}', " +
                  $"item.mode='{item.mode}', default_mode='{cfg.default_mode}', " +
                  $"finalMode='{mode}', useSlider={useSlider}");
    }
}
