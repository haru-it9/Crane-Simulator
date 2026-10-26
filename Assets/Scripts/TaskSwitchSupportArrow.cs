using UnityEngine;
using UnityEngine.UI;

/// <summary>A mesh arrow, independent of the font's Unicode-arrow glyph coverage.</summary>
[AddComponentMenu("UI/Task Switch Support Arrow")]
public class TaskSwitchSupportArrow : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        Rect r = rectTransform.rect;
        float middle = r.center.x;
        float neck = r.yMin + r.height * 0.55f;
        float halfShaft = r.width * 0.17f;
        AddVertex(vertices, middle - halfShaft, r.yMin);
        AddVertex(vertices, middle + halfShaft, r.yMin);
        AddVertex(vertices, middle + halfShaft, neck);
        AddVertex(vertices, middle - halfShaft, neck);
        vertices.AddTriangle(0, 1, 2);
        vertices.AddTriangle(2, 3, 0);
        AddVertex(vertices, r.xMin, neck);
        AddVertex(vertices, r.xMax, neck);
        AddVertex(vertices, middle, r.yMax);
        vertices.AddTriangle(4, 5, 6);
    }

    private void AddVertex(VertexHelper vertices, float x, float y)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = new Vector3(x, y, 0f);
        vertex.color = color;
        vertices.AddVert(vertex);
    }
}
