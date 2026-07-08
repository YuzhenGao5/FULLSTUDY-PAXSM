// 文件名：FollowTargetWorld.cs
using UnityEngine;

public class FollowTargetWorld : MonoBehaviour
{
    [Header("拖你要跟随的那个物体（必须是有追踪的 Transform）")]
    public Transform target;

    [Header("是否跟随旋转")]
    public bool followRotation = true;

    void LateUpdate()
    {
        if (!target) return;

        transform.position = target.position;

        if (followRotation)
            transform.rotation = target.rotation;
    }
}
