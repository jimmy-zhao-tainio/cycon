using System.Numerics;

namespace Cycon.Render;

public interface IRenderCanvas
{
    void SetCullState(bool enabled, bool frontFaceCcw);

    void PushClipRect(in RectPx rectPx);

    void PopClipRect();

    void FillRect(in RectPx rectPx, int rgba);

    void SetColorWrite(bool enabled);

    void SetDepthState(bool enabled, bool writeEnabled, DepthFunc func);

    void ClearDepth(float depth01);

    void ReleaseMesh3D(int meshId);

    /// <summary>
     /// Draw a previously uploaded mesh into the current clip/scissor using the given viewport rect and transforms.
     /// </summary>
    void DrawMesh3D(
        int meshId,
        float[] vertexData,
        int vertexCount,
        in RectPx viewportRectPx,
        in Matrix4x4 model,
        in Matrix4x4 view,
        in Matrix4x4 proj,
        in Vector3 lightDirView,
        in Scene3DRenderSettings settings);

    void DrawVignette(in RectPx rectPx, float strength01, float inner, float outer);
}
