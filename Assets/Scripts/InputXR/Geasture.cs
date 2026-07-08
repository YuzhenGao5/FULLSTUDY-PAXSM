// 文件名：AutoQuestControllerGripPose.cs
using UnityEngine;

/// <summary>
/// 挂在右手机模根节点（例如 RightHandQuestVisualForController）
/// 不用在 Inspector 里拖任何骨骼引用：
/// - 自动在子物体里根据名字查找 Thumb/Index/Middle/Ring/Little 的各关节
/// - 一次性把手捏成“握着 Meta 控制器，食指扣扳机，拇指压在按钮区靠近食指指尖”的姿势
///
/// 用法：
/// 1. 把这个脚本挂在右手模型根节点上
/// 2. 确保骨骼命名类似：R_ThumbMetacarpal / R_IndexProximal / R_MiddleDistal …
/// 3. 勾上 applyOnEnable（默认勾着），进入 Play 或在 Editor 里都会自动捏手一次
/// 4. 满意之后对 prefab 点 Apply，然后可以把脚本删掉
///
/// 注意：这是“在当前 localRotation 基础上 *= 追加角度”，
/// 建议先让手是放松/张开的默认姿势再用，撤回就 Ctrl+Z 或重载 prefab。
/// </summary>
[ExecuteAlways]
public class AutoQuestControllerGripPose : MonoBehaviour
{
    [Header("整体弯曲强度")]
    [Tooltip("1 = 默认，0.5 = 弯少一点，1.5 = 更紧一点")]
    [Range(0.2f, 2.0f)]
    public float gripStrength = 1.0f;

    [Header("何时自动应用")]
    [Tooltip("启用时自动捏手（编辑器 + 运行时都会执行一次）")]
    public bool applyOnEnable = true;

    [Tooltip("Inspector 改参数或重新选中时自动重新寻找骨骼")]
    public bool autoRefindBonesOnValidate = true;

    [Header("调试")]
    public bool logDebug = false;

    // —— 内部骨骼引用（自动查找） ——
    Transform thumbMeta, thumbProx, thumbInter, thumbDist;
    Transform indexMeta, indexProx, indexInter, indexDist;
    Transform middleMeta, middleProx, middleInter, middleDist;
    Transform ringMeta, ringProx, ringInter, ringDist;
    Transform littleMeta, littleProx, littleInter, littleDist;

    void OnEnable()
    {
        if (autoRefindBonesOnValidate || !HasAnyBone())
            AutoFindBones();

        if (applyOnEnable)
            ApplyGripPose();
    }

    void OnValidate()
    {
        if (!Application.isEditor) return;

        if (autoRefindBonesOnValidate)
            AutoFindBones();
    }

    // ================= 自动找骨骼 =================

    void AutoFindBones()
    {
        Transform[] all = GetComponentsInChildren<Transform>(true);
        foreach (var t in all)
        {
            string n = t.name.ToLowerInvariant();

            // 拇指 Thumb
            if      (ContainsAll(n, "thumb", "metacarpal")) thumbMeta  = t;
            else if (ContainsAll(n, "thumb", "prox"))       thumbProx  = t;
            else if (ContainsAll(n, "thumb", "inter"))      thumbInter = t;
            else if (ContainsAll(n, "thumb", "dist"))       thumbDist  = t;

            // 食指 Index
            else if (ContainsAll(n, "index", "metacarpal")) indexMeta  = t;
            else if (ContainsAll(n, "index", "prox"))       indexProx  = t;
            else if (ContainsAll(n, "index", "inter"))      indexInter = t;
            else if (ContainsAll(n, "index", "dist"))       indexDist  = t;

            // 中指 Middle
            else if (ContainsAll(n, "middle", "metacarpal")) middleMeta  = t;
            else if (ContainsAll(n, "middle", "prox"))       middleProx  = t;
            else if (ContainsAll(n, "middle", "inter"))      middleInter = t;
            else if (ContainsAll(n, "middle", "dist"))       middleDist  = t;

            // 无名指 Ring
            else if (ContainsAll(n, "ring", "metacarpal")) ringMeta  = t;
            else if (ContainsAll(n, "ring", "prox"))       ringProx  = t;
            else if (ContainsAll(n, "ring", "inter"))      ringInter = t;
            else if (ContainsAll(n, "ring", "dist"))       ringDist  = t;

            // 小指 Little / Pinky
            else if ((n.Contains("little") || n.Contains("pinky")) && n.Contains("metacarpal")) littleMeta  = t;
            else if ((n.Contains("little") || n.Contains("pinky")) && n.Contains("prox"))       littleProx  = t;
            else if ((n.Contains("little") || n.Contains("pinky")) && n.Contains("inter"))      littleInter = t;
            else if ((n.Contains("little") || n.Contains("pinky")) && n.Contains("dist"))       littleDist  = t;
        }

        if (logDebug)
            Debug.Log("[AutoQuestControllerGripPose] Bone auto-detect finished.", this);
    }

    bool ContainsAll(string src, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (!src.Contains(k)) return false;
        }
        return true;
    }

    bool HasAnyBone()
    {
        return thumbMeta || thumbProx || thumbInter || thumbDist ||
               indexMeta || indexProx || indexInter || indexDist ||
               middleMeta || middleProx || middleInter || middleDist ||
               ringMeta || ringProx || ringInter || ringDist ||
               littleMeta || littleProx || littleInter || littleDist;
    }

    // ================= 对外：一次性捏手 =================

    [ContextMenu("Apply Grip Pose (一次性捏手)")]
    public void ApplyGripPose()
    {
        if (!HasAnyBone())
        {
            AutoFindBones();
            if (!HasAnyBone())
            {
                Debug.LogWarning(
                    "[AutoQuestControllerGripPose] 找不到任何手指骨骼，请确认命名是否包含 Thumb/Index/Middle/Ring/Little + Metacarpal/Prox/Inter/Dist。",
                    this);
                return;
            }
        }

        // 拇指：压在按钮壳上，朝食指指尖方向收拢
        ApplyThumbGrip();

        // 食指：指尖触碰确认键，整体弯曲较小
        ApplyFingerGrip(
            indexMeta, indexProx, indexInter, indexDist,
            base1: 22f, base2: 34f, base3: 26f
        );

        // 中指：包住柄身
        ApplyFingerGrip(
            middleMeta, middleProx, middleInter, middleDist,
            base1: 42f, base2: 54f, base3: 36f
        );

        // 无名指：略比中指更紧
        ApplyFingerGrip(
            ringMeta, ringProx, ringInter, ringDist,
            base1: 48f, base2: 58f, base3: 40f
        );

        // 小指：最紧，贴住底部
        ApplyFingerGrip(
            littleMeta, littleProx, littleInter, littleDist,
            base1: 52f, base2: 62f, base3: 44f
        );

        if (logDebug)
            Debug.Log("[AutoQuestControllerGripPose] 已应用握 Meta 控制器姿势。", this);
    }

    // ================= 具体指头弯曲逻辑 =================

    /// <summary>
    /// 拇指：按你的照片那种姿势——贴在上侧按钮区，往食指指尖方向靠近。
    /// 这里把原来反向的 Y 轴翻过来。
    /// </summary>
    void ApplyThumbGrip()
    {
        if (thumbMeta != null)
        {
            // 根关节：略微前倾 + 向内扣，让拇指从壳的外沿收向按钮中心
            thumbMeta.localRotation *= Quaternion.Euler(
                10f * gripStrength,      // X：往前
                -32f * gripStrength,     // Y：向内（朝食指方向）❗反向
                6f  * gripStrength       // Z：轻微抬起
            );
        }
        if (thumbProx != null)
        {
            // 近节：主要往掌心 + 食指方向收
            thumbProx.localRotation *= Quaternion.Euler(
                0f,
                -26f * gripStrength,     // 往里
                8f  * gripStrength
            );
        }
        if (thumbInter != null)
        {
            // 中节：轻微弯曲，指肚靠在壳面上
            thumbInter.localRotation *= Quaternion.Euler(
                0f,
                -16f * gripStrength,
                4f  * gripStrength
            );
        }
        if (thumbDist != null)
        {
            // 末节：基本伸直，只是让指尖再朝食指挪一点
            thumbDist.localRotation *= Quaternion.Euler(
                0f,
                -12f * gripStrength,
                2f  * gripStrength
            );
        }
    }

    /// <summary>
    /// 通用四指弯曲：从近节到远节逐渐增大，meta 再稍微往掌心扣一点。
    /// </summary>
    void ApplyFingerGrip(
        Transform meta, Transform prox, Transform inter, Transform dist,
        float base1, float base2, float base3)
    {
        if (meta != null)
        {
            // 整根手指略微朝掌心内收
            meta.localRotation *= Quaternion.Euler(
                0f,
                6f * gripStrength,
                0f
            );
        }

        if (prox != null)
        {
            prox.localRotation *= Quaternion.Euler(
                base1 * gripStrength, 0f, 0f
            );
        }
        if (inter != null)
        {
            inter.localRotation *= Quaternion.Euler(
                base2 * gripStrength, 0f, 0f
            );
        }
        if (dist != null)
        {
            dist.localRotation *= Quaternion.Euler(
                base3 * gripStrength, 0f, 0f
            );
        }
    }
}
