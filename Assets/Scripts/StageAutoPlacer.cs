using UnityEngine;

public class QE_AutoPlaceTwoObjects : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XR Origin -> Main Camera")]
    public Transform head;

    [Tooltip("问卷主屏幕（QE_Container）")]
    public Transform screen;

    [Tooltip("控制面板（Knob 控制器台）")]
    public Transform controlBoard;

    // ----------- 屏幕参数（最佳阅读区） -----------
    [Header("Screen Placement (Recommended)")]
    [Tooltip("屏幕距离（最佳 0.55~0.70m）")]
    public float screenDistance = 0.62f;

    [Tooltip("比视线低（最佳 -0.10m）")]
    public float screenHeightOffset = -0.10f;

    [Tooltip("向下俯角（最佳 -6° ~ -12°）")]
    public float screenPitch = -8f;


    // ----------- 控制台参数（Knob 板） -----------
    [Header("Control Board Placement")]
    [Tooltip("屏幕下方多少米（0.20~0.35m）")]
    public float boardDownFromScreen = 0.28f;

    [Tooltip("控制板离用户更近（0.20~0.35m）")]
    public float boardForwardFromScreen = 0.25f;

    [Tooltip("控制板倾斜角（向上/向用户）")]
    public float boardTilt = 25f;


    void Start()
    {
        if (!head || !screen || !controlBoard)
        {
            Debug.LogError("[QE_AutoPlaceTwoObjects] 引用未设置！");
            return;
        }

        PlaceScreen();
        PlaceControlBoard();

        enabled = false;  // 运行一次即可
    }


    // ======================
    //   摆放屏幕
    // ======================
    void PlaceScreen()
    {
        // 水平 forward
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
            forward = head.forward;

        // 位置
        Vector3 pos = head.position + forward * screenDistance;
        pos.y = head.position.y + screenHeightOffset;

        screen.position = pos;

        // 朝向
        Quaternion rot = Quaternion.LookRotation(forward, Vector3.up);
        rot *= Quaternion.Euler(screenPitch, 0, 0);

        screen.rotation = rot;

        Debug.Log("[QE_AutoPlace] Screen placed.");
    }


    // ======================
    //   摆放控制面板（Knob）
    // ======================
    void PlaceControlBoard()
    {
        // 基于屏幕位置决定控制板位置
        Vector3 pos =
            screen.position
            + (-screen.up) * boardDownFromScreen        // 屏幕下方
            + (-screen.forward) * boardForwardFromScreen;  // 更靠用户

        controlBoard.position = pos;

        // 控制板朝向：
        //   1. 与屏幕相同的 yaw
        //   2. 加一个向上倾的角度（hand-friendly）
        Quaternion rot = screen.rotation;
        rot *= Quaternion.Euler(boardTilt, 0, 0);

        controlBoard.rotation = rot;

        Debug.Log("[QE_AutoPlace] Control Board placed.");
    }
}
