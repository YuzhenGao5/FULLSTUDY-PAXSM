using UnityEngine;

namespace AnyUI.Library
{
    [ExecuteInEditMode]
    public class AnyUICurvedSurface : MonoBehaviour
    {
        [Header("Wiring")]
        public MeshFilter DisplayObject;
        public SkinnedMeshRenderer Curve;

        [Header("Edit Shape")]
        public bool EditShape = true;
        [Range(0f, 100f)] public float CurveHorizontal = 0f;
        [Range(0f, 100f)] public float CurveVertical = 0f;
        [Range(0f, 100f)] public float Width = 0f;

        // 编辑模式临时 mesh（不保存到资源）
        private Mesh _editMesh;

        void OnEnable()
        {
            EnsureMesh();
            Bake();
        }

        void Awake()
        {
            EnsureMesh();
            Bake();
        }

        void OnValidate()
        {
            if (!EditShape) return;
            EnsureMesh();
            Bake();
        }

        void EnsureMesh()
        {
            if (!DisplayObject || !Curve) return;

            if (!Application.isPlaying)
            {
                // 编辑模式：确保 DisplayObject.sharedMesh 有一个可写的临时 mesh
                if (DisplayObject.sharedMesh == null)
                {
                    if (_editMesh == null)
                    {
                        _editMesh = new Mesh();
                        _editMesh.name = $"{gameObject.name}_EditBakedMesh";
                        _editMesh.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                    }
                    DisplayObject.sharedMesh = _editMesh;
                }
            }
            else
            {
                // 运行模式：用实例 mesh
                if (DisplayObject.mesh == null)
                {
                    var runtime = new Mesh();
                    runtime.name = $"{gameObject.name}_RuntimeBakedMesh";
                    DisplayObject.mesh = runtime;
                }
            }
        }

        void Bake()
        {
            if (!DisplayObject || !Curve) return;

            Curve.SetBlendShapeWeight(0, CurveHorizontal);
            Curve.SetBlendShapeWeight(1, CurveVertical);
            Curve.SetBlendShapeWeight(2, Width);

            var target = Application.isPlaying ? DisplayObject.mesh : DisplayObject.sharedMesh;
            if (target == null) return;

            Curve.BakeMesh(target);
        }
    }
}
