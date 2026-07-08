using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Replace Image with this to get a smooth 90-degree curved UI bar.
/// This component IS an Image (inherits Image).
/// </summary>
[ExecuteAlways]
public class CurvedImage90 : Image
{
    [Range(1, 80)]
    public int segments = 24;

    [Range(0f, 120f)]
    public float curveAngleDegrees = 90f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();

        Rect r = rectTransform.rect;
        float w = Mathf.Max(0.0001f, r.width);
        float h = Mathf.Max(0.0001f, r.height);

        float angleRad = curveAngleDegrees * Mathf.Deg2Rad;
        if (Mathf.Abs(angleRad) < 0.0001f)
        {
            // fallback flat quad
            base.OnPopulateMesh(vh);
            return;
        }

        float centerX = (r.xMin + r.xMax) * 0.5f;
        float radius = Mathf.Max(1f, w / angleRad);

        // two rows of vertices: bottom & top
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            float x = Mathf.Lerp(r.xMin, r.xMax, t);

            float xLocal = x - centerX;
            float a = xLocal / radius;

            float curvedX = Mathf.Sin(a) * radius + centerX;
            float curvedZ = radius - Mathf.Cos(a) * radius;

            // bottom
            AddVert(vh,
                new Vector3(curvedX, r.yMin, curvedZ),
                color,
                new Vector2(t, 0f));

            // top
            AddVert(vh,
                new Vector3(curvedX, r.yMax, curvedZ),
                color,
                new Vector2(t, 1f));
        }

        // triangles
        for (int i = 0; i < segments; i++)
        {
            int idx = i * 2;

            // bottom-left, top-left, top-right
            vh.AddTriangle(idx, idx + 1, idx + 3);
            // bottom-left, top-right, bottom-right
            vh.AddTriangle(idx, idx + 3, idx + 2);
        }
    }

    void AddVert(VertexHelper vh, Vector3 pos, Color32 col, Vector2 uv)
    {
        UIVertex v = UIVertex.simpleVert;
        v.position = pos;
        v.color = col;
        v.uv0 = uv;
        vh.AddVert(v);
    }
}
