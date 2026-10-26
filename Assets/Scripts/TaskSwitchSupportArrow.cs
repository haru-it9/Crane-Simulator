using UnityEngine;
using UnityEngine.UI;

public enum TaskSwitchDirectionPresentation
{
    SingleArrow,
    DistanceChevrons
}

/// <summary>A mesh arrow, independent of the font's Unicode-arrow glyph coverage.</summary>
[AddComponentMenu("UI/Task Switch Support Arrow")]
public class TaskSwitchSupportArrow : MaskableGraphic
{
    private TaskSwitchDirectionPresentation presentation;
    private int chevronCount = 1, maximumChevrons = 3;

    public int ChevronCount => chevronCount;

    public void SetPresentation(TaskSwitchDirectionPresentation style, int count, int maximum)
    {
        maximum = Mathf.Clamp(maximum, 1, 3);
        count = Mathf.Clamp(count, 1, maximum);
        if (presentation == style && chevronCount == count && maximumChevrons == maximum) return;
        presentation = style;
        chevronCount = count;
        maximumChevrons = maximum;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vertices)
    {
        vertices.Clear();
        Rect r = rectTransform.rect;
        if (presentation == TaskSwitchDirectionPresentation.DistanceChevrons)
        {
            // Keep each glyph the same size; fewer glyphs occupy less space around the centre.
            float pitch = r.height / maximumChevrons;
            float start = r.center.y - chevronCount * pitch * 0.5f;
            for (int i = 0; i < chevronCount; i++)
            {
                float bottom = start + i * pitch + pitch * 0.1f;
                float tip = bottom + pitch * 0.8f;
                float thickness = pitch * 0.22f;
                int offset = i * 6;
                AddVertex(vertices, r.xMin, bottom + thickness);
                AddVertex(vertices, r.center.x, tip);
                AddVertex(vertices, r.xMax, bottom + thickness);
                AddVertex(vertices, r.xMax, bottom);
                AddVertex(vertices, r.center.x, tip - thickness);
                AddVertex(vertices, r.xMin, bottom);
                vertices.AddTriangle(offset, offset + 4, offset + 1);
                vertices.AddTriangle(offset, offset + 5, offset + 4);
                vertices.AddTriangle(offset + 1, offset + 3, offset + 2);
                vertices.AddTriangle(offset + 1, offset + 4, offset + 3);
            }
            return;
        }
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
