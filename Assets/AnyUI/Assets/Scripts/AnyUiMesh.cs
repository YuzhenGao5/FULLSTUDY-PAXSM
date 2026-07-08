using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AnyUI
{
    public enum AnyUiResolution
    {
        _1k = 1024,
        _2k = 2048,
        _4k = 4096,
        _8k = 8192
    }

    /// <summary>
    /// A Raycaster to convey input events (occuring on a 3d model) to a given canvas
    /// 这里只做事件转发，不创建/不修改/不销毁任何相机。
    /// </summary>
    public class AnyUiMesh : BaseRaycaster
    {
        [Tooltip("Which canvas should be projected on this object?")]
        public Canvas CanvasToProject;

        [Tooltip("_1k means the original canvas will be projected onto a 1024 x Y texture on the mesh")]
        public AnyUiResolution ProjectionResolution = AnyUiResolution._1k;

        [Tooltip("Material to use for the projected UI.")]
        public Material UseMaterial;

        [Tooltip("If you want to add the canvas-material to this object's material list instead of replacing it, set the check mark")]
        public bool UseMaterialLayering = true;

        [Tooltip("If you need a camera other than the 'Main Camera' to interact with the projected canvas, set it here")]
        public Camera UseCamera;

        public override Camera eventCamera
        {
            get
            {
                return UseCamera != null ? UseCamera : Camera.main;
            }
        }

        private AnyUiCanvas receiver;

        protected override void Start()
        {
            base.Start();
            receiver = CanvasToProject != null ? CanvasToProject.GetComponent<AnyUiCanvas>() : null;
        }

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            if (eventData == null) return;
            if (CanvasToProject == null) return;
            if (receiver == null)
            {
                receiver = CanvasToProject.GetComponent<AnyUiCanvas>();
                if (receiver == null) return; // ✅ 核心防爆
            }

            var c = GetComponent<Collider>();
            if (c == null) return;

            receiver.InputPossible = false;

            Ray rCurrent = eventCamera != null
                ? eventCamera.ScreenPointToRay(eventData.position)
                : new Ray(Vector3.zero, Vector3.zero);

            Ray rLast = eventCamera != null
                ? eventCamera.ScreenPointToRay(eventData.position - eventData.delta)
                : new Ray(Vector3.zero, Vector3.zero);

            Ray rPress = eventCamera != null
                ? eventCamera.ScreenPointToRay(eventData.pressPosition)
                : new Ray(Vector3.zero, Vector3.zero);

            RaycastHit hit;

            if (rCurrent.direction != Vector3.zero && c.Raycast(rCurrent, out hit, float.MaxValue))
            {
                receiver.InputPossible = true;

                PointerEventData pData = eventData;

                Vector2 guiPos = hit.textureCoord;
                Vector3 screenPoint = receiver.eventCamera != null
                    ? receiver.eventCamera.ViewportToScreenPoint(guiPos)
                    : new Vector3(guiPos.x * Screen.width, guiPos.y * Screen.height, 0);

                pData.position = new Vector2(screenPoint.x, screenPoint.y);

                if (rLast.direction != Vector3.zero && c.Raycast(rLast, out hit, float.MaxValue))
                {
                    guiPos = hit.textureCoord;
                    Vector3 lastScreenPoint = receiver.eventCamera != null
                        ? receiver.eventCamera.ViewportToScreenPoint(guiPos)
                        : new Vector3(guiPos.x * Screen.width, guiPos.y * Screen.height, 0);

                    pData.delta = new Vector2(screenPoint.x - lastScreenPoint.x, screenPoint.y - lastScreenPoint.y);
                }

                if (rPress.direction != Vector3.zero && c.Raycast(rPress, out hit, float.MaxValue))
                {
                    guiPos = hit.textureCoord;
                    Vector3 pressScreenPoint = receiver.eventCamera != null
                        ? receiver.eventCamera.ViewportToScreenPoint(guiPos)
                        : new Vector3(guiPos.x * Screen.width, guiPos.y * Screen.height, 0);

                    pData.pressPosition = new Vector2(pressScreenPoint.x, pressScreenPoint.y);
                }

                var results = new List<RaycastResult>();
                receiver.setPointerEventDataHashMask(pData.GetHashCode());
                receiver.Raycast(pData, results);
                resultAppendList.AddRange(results);
            }
        }
    }
}
