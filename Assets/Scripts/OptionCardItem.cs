using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

[RequireComponent(typeof(Button))]
public class OptionCardItem : MonoBehaviour
{
    [Header("Slots")]
    public Button button;                 // 根或子节点上的 Button
    public TextMeshProUGUI label;         // 选项显示文本
    public Image bg;                      // 可选：背景（用来高亮）

    [Header("Visual")]
    public Color normalBg   = new(1,1,1,0.12f);
    public Color selectedBg = new(1,1,1,0.25f);
    public Color confirmedBg= new(1,1,1,0.45f);

    [Header("Index")]
    public int index = -1;                // 外部克隆时赋值

    [System.Serializable] public class IntEvent : UnityEvent<int>{}

    [Header("Events")]
    public IntEvent onSelected;           // 第一次点击/被选中
    public IntEvent onConfirmed;          // 再次点击/确认时触发

    bool _isSelected = false;
    bool _isConfirmed = false;

    void Reset()
    {
        if (!button) button = GetComponent<Button>();
        if (!label)  label  = GetComponentInChildren<TextMeshProUGUI>(true);
        if (!bg)     bg     = GetComponent<Image>(); // 没单独BG就用根Image
    }

    void Awake()
    {
        if (!button) button = GetComponent<Button>();
        button.onClick.AddListener(OnClick);
        Debug.Log($"[OptionCardItem] Awake on '{gameObject.name}' (index={index})");
        ApplyVisual();
    }

    void OnClick()
    {
        if (!_isSelected)
        {
            _isSelected = true;
            _isConfirmed = false;
            ApplyVisual();
            Debug.Log($"[OptionCardItem] SELECT index={index}, label='{(label? label.text : "")}'");
            onSelected?.Invoke(index);
        }
        else
        {
            _isConfirmed = true;
            ApplyVisual();
            Debug.Log($"[OptionCardItem] CONFIRM index={index}, label='{(label? label.text : "")}'");
            onConfirmed?.Invoke(index);
        }
    }

    // —— 外部可调用的 API ——
    public void SetLabel(string text)
    {
        if (label) label.text = text ?? "";
        Debug.Log($"[OptionCardItem] SetLabel index={index} -> '{(text ?? "")}'");
    }

    public void SetIndex(int i)
    {
        index = i;
        Debug.Log($"[OptionCardItem] SetIndex -> {index}");
    }

    public void SetSelected(bool on)
    {
        _isSelected = on;
        if (!on) _isConfirmed = false;
        ApplyVisual();
        Debug.Log($"[OptionCardItem] SetSelected index={index} -> {on}");
    }

    public void SetConfirmed(bool on)
    {
        _isSelected = on || _isSelected;
        _isConfirmed = on;
        ApplyVisual();
        Debug.Log($"[OptionCardItem] SetConfirmed index={index} -> {on}");
    }

    public void ResetState()
    {
        _isSelected = false;
        _isConfirmed = false;
        ApplyVisual();
        Debug.Log($"[OptionCardItem] ResetState index={index}");
    }

    void ApplyVisual()
    {
        if (!bg) return;
        if (_isConfirmed)      bg.color = confirmedBg;
        else if (_isSelected)  bg.color = selectedBg;
        else                   bg.color = normalBg;
    }
}
