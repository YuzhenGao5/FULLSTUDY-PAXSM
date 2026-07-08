using UnityEngine;

public class KnobInitialPlacer : MonoBehaviour
{
    [Header("References")]
    [Tooltip("面板上的锚点（QE_Container 下的某个 RectTransform，如 KnobMount）")]
    public RectTransform surfaceMount;      // 表面基准
    [Tooltip("你的旋钮根结点（KnobRig 或 Knob）")]
    public Transform knobRoot;              // 旋钮
    [Tooltip("用户相机（XR 的 Main Camera）。留空则自动用 Camera.main")]
    public Transform lookCamera;            // 用户相机

    [Header("Offsets (relative to surfaceMount axes)")]
    [Tooltip("X=沿右方向，Y=沿上方向，Z=沿表面法线(正值=从面板凸出)")]
    public Vector3 localOffsets = new Vector3(0f, 0f, 0.02f);   // 2cm 浮在表面前
    [Tooltip("额外欧拉角微调（度）")]
    public Vector3 eulerOffset = Vector3.zero;

    [Header("Facing")]
    [Tooltip("仅绕 surfaceMount 的 Up 轴朝向相机（推荐，避免俯仰/翻滚）")]
    public bool yawOnly = true;
    [Tooltip("若发现旋钮被放到面板背面，勾上翻转法线")]
    public bool flipNormal = false;

#if UNITY_EDITOR
    [Header("Editor Preview")]
    [Tooltip("编辑器中每次改 Inspector 都预览一次（不进入播放）")]
    public bool editorPreview = false;
    void OnValidate()
    {
        if (!Application.isPlaying && editorPreview)
        {
            PlaceNow();
        }
    }
#endif

    void Start()
    {
        PlaceNow();
    }

    [ContextMenu("Place Now")]
    public void PlaceNow()
    {
        if (!surfaceMount || !knobRoot) return;

        // 找到相机
        if (!lookCamera)
        {
            var cam = Camera.main;
            if (cam) lookCamera = cam.transform;
        }

        // 1) 先算“表面上的”世界位置（沿 mount 局部轴偏移）
        Vector3 worldPos =
              surfaceMount.position
            + surfaceMount.right   * localOffsets.x
            + surfaceMount.up      * localOffsets.y
            + (flipNormal ? -surfaceMount.forward : surfaceMount.forward) * localOffsets.z;

        // 2) 箭头朝向用户
        Quaternion worldRot;
        if (lookCamera)
        {
            // 旋钮希望正面（其 forward）指向用户
            Vector3 toCam = (lookCamera.position - worldPos);

            if (yawOnly)
            {
                // 只绕 surfaceMount 的 up 轴：把 toCam 投影到“以 up 为法线的平面”上
                Vector3 dir = Vector3.ProjectOnPlane(toCam, surfaceMount.up);
                if (dir.sqrMagnitude < 1e-6f)
                    dir = (flipNormal ? -surfaceMount.forward : surfaceMount.forward); // 兜底

                worldRot = Quaternion.LookRotation(dir.normalized, surfaceMount.up);
            }
            else
            {
                // 完全朝向相机（可能带俯仰）
                worldRot = Quaternion.LookRotation(toCam.normalized, surfaceMount.up);
            }
        }
        else
        {
            // 没相机就让正面对着面板法线
            Vector3 fwd = flipNormal ? -surfaceMount.forward : surfaceMount.forward;
            worldRot = Quaternion.LookRotation(fwd, surfaceMount.up);
        }

        knobRoot.SetPositionAndRotation(worldPos, worldRot * Quaternion.Euler(eulerOffset));
    }
}
