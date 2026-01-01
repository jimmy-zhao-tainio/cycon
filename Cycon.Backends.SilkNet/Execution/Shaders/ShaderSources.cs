namespace Cycon.Backends.SilkNet.Execution.Shaders;

public static class ShaderSources
{
    public const string Vertex = @"#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;
layout(location = 2) in vec4 aColor;

uniform vec2 uViewport;

out vec2 vTexCoord;
out vec4 vColor;

void main()
{
    vec2 ndc = vec2((aPosition.x / uViewport.x) * 2.0 - 1.0,
                    1.0 - (aPosition.y / uViewport.y) * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
}
";

    public const string FragmentGlyph = @"#version 330 core
in vec2 vTexCoord;
in vec4 vColor;

out vec4 FragColor;

uniform sampler2D uAtlas;

void main()
{
    vec4 t = texture(uAtlas, vTexCoord);
    float alpha = max(t.r, max(t.g, t.b));
    FragColor = vec4(vColor.rgb, vColor.a * alpha);
}
";

    public const string FragmentQuad = @"#version 330 core
in vec4 vColor;

out vec4 FragColor;

void main()
{
    FragColor = vColor;
}
";

    public const string Vertex3D = @"#version 330 core
layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aDepth;
layout(location = 2) in vec4 aColor;

uniform vec2 uViewport;

out vec4 vColor;

void main()
{
    vec2 ndc = vec2((aPosition.x / uViewport.x) * 2.0 - 1.0,
                    1.0 - (aPosition.y / uViewport.y) * 2.0);
    float depth = clamp(aDepth.x, 0.0, 1.0);
    float z = depth * 2.0 - 1.0;
    gl_Position = vec4(ndc, z, 1.0);
    vColor = aColor;
}
";

    public const string FragmentVignette = @"#version 330 core
uniform vec2 uViewport;
uniform vec4 uRect;   // x,y,w,h in TOP-LEFT pixel space
uniform vec3 uParams; // strength, inner, outer

out vec4 FragColor;

void main()
{
    // gl_FragCoord is bottom-left origin; convert to top-left.
    vec2 p = vec2(gl_FragCoord.x, uViewport.y - gl_FragCoord.y);
    vec2 uv = (p - uRect.xy) / uRect.zw;

    // Outside: transparent.
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
    {
        FragColor = vec4(0.0);
        return;
    }

    vec2 centered = uv - vec2(0.5, 0.5);
    float aspect = uRect.z / max(1.0, uRect.w);
    centered.x *= aspect;

    float r = length(centered);
    float v = smoothstep(uParams.y, uParams.z, r);
    float alpha = clamp(uParams.x * v, 0.0, 1.0);
    FragColor = vec4(0.0, 0.0, 0.0, alpha);
}
";
}
