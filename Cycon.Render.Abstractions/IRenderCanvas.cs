using System.Collections.Generic;

namespace Cycon.Render;

public interface IRenderCanvas
{
    void PushClipRect(in RectPx rectPx);

    void PopClipRect();

    void FillRect(in RectPx rectPx, int rgba);

    void SetColorWrite(bool enabled);

    void SetDepthState(bool enabled, bool writeEnabled, DepthFunc func);

    void ClearDepth(float depth01);

    void DrawTriangles3D(IReadOnlyList<SolidVertex3D> vertices);

    void DrawVignette(in RectPx rectPx, float strength01, float inner, float outer);
}

