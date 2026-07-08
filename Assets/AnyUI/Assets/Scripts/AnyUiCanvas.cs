using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AnyUI
{
    /// <summary>
    /// GraphicRaycaster which only accepts input events that have a certain hash value
    /// 不再销毁相机/RT，避免误伤 XR 主相机。
    /// </summary>
    [ExecuteInEditMode]
    public class AnyUiCanvas : GraphicRaycaster
    {
        private int pointerEventDataHashMask;
        public bool InputPossible { get; set; }

        // ✅ 不强制依赖 Canvas.worldCamera
        // GraphicRaycaster 会用这个相机做转换；我们给一个安全 fallback
        public override Camera eventCamera
        {
            get
            {
                var c = GetComponent<Canvas>();
                if (c != null && c.worldCamera != null) return c.worldCamera;
                return Camera.main; // 仅 fallback，不会修改任何设置
            }
        }

        public void setPointerEventDataHashMask(int h)
        {
            pointerEventDataHashMask = h;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (eventData == null) return;

            if (eventData.GetHashCode() == pointerEventDataHashMask && InputPossible)
            {
                base.Raycast(eventData, resultAppendList);
            }
        }

        // ✅ 删除所有 OnDestroy 里的 Editor 删除/销毁相机逻辑
        // 避免误删 XR Main Camera
    }
}
