using System.Collections.Generic;
using Cycon.Render;
using Cycon.Rendering.Commands;

namespace Cycon.Rendering.Renderer;

internal sealed class RenderCanvas : IRenderCanvas
{
    private readonly RenderFrame _frame;

    public RenderCanvas(RenderFrame frame)
    {
        _frame = frame;
    }

    public void PushClipRect(in RectPx rectPx) =>
        _frame.Add(new PushClip(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height));

    public void PopClipRect() =>
        _frame.Add(new PopClip());

    public void FillRect(in RectPx rectPx, int rgba) =>
        _frame.Add(new DrawQuad(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height, rgba));

    public void SetColorWrite(bool enabled) =>
        _frame.Add(new SetColorWrite(enabled));

    public void SetDepthState(bool enabled, bool writeEnabled, DepthFunc func) =>
        _frame.Add(new SetDepthState(enabled, writeEnabled, Map(func)));

    public void ClearDepth(float depth01) =>
        _frame.Add(new ClearDepth(depth01));

    public void DrawTriangles3D(IReadOnlyList<SolidVertex3D> vertices) =>
        _frame.Add(new DrawTriangles3D(vertices));

    public void DrawVignette(in RectPx rectPx, float strength01, float inner, float outer) =>
        _frame.Add(new DrawVignetteQuad(rectPx.X, rectPx.Y, rectPx.Width, rectPx.Height, strength01, inner, outer));

    private static DepthFuncKind Map(DepthFunc func) =>
        func switch
        {
            DepthFunc.Less => DepthFuncKind.Less,
            DepthFunc.Lequal => DepthFuncKind.Lequal,
            DepthFunc.Greater => DepthFuncKind.Greater,
            DepthFunc.Always => DepthFuncKind.Always,
            _ => DepthFuncKind.Less
        };
}
