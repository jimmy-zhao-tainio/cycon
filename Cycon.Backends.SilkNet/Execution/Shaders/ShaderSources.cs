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

    public const string FragmentImage = @"#version 330 core
in vec2 vTexCoord;
in vec4 vColor;

out vec4 FragColor;

uniform sampler2D uImage;

void main()
{
    vec4 t = texture(uImage, vTexCoord);
    FragColor = vec4(t.rgb, t.a) * vColor;
}
";

    // Dual-filter Kawase-style blur tap (4 samples). Intended for multi-resolution downsample/upsample chains.
    public const string FragmentKawaseBlur = @"#version 330 core
in vec2 vTexCoord;

out vec4 FragColor;

uniform sampler2D uImage;
uniform vec2 uTexel;   // 1/sourceWidth, 1/sourceHeight
uniform float uOffset; // offset in source texels

void main()
{
    vec2 o = uTexel * uOffset;
    vec4 c = texture(uImage, vTexCoord + vec2( o.x,  o.y));
    c += texture(uImage, vTexCoord + vec2(-o.x,  o.y));
    c += texture(uImage, vTexCoord + vec2( o.x, -o.y));
    c += texture(uImage, vTexCoord + vec2(-o.x, -o.y));
    FragColor = c * 0.25;
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

    public const string VertexMesh3D = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;

uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;

out vec3 vNormalView;
out vec3 vPosView;

void main()
{
    vec4 worldPos = uModel * vec4(aPos, 1.0);
    vec4 viewPos = uView * worldPos;
    vPosView = viewPos.xyz;

    // Model is identity for now; keep the correct shape for future.
    mat3 normalMat = mat3(uView * uModel);
    vNormalView = normalize(normalMat * aNormal);

    gl_Position = uProj * viewPos;
}
";

    public const string FragmentMesh3D = @"#version 330 core
in vec3 vNormalView;
in vec3 vPosView;

uniform vec3 uLightDirView; // normalized, view space
uniform float uAmbient;
uniform float uDiffuseStrength;
uniform float uToneGamma;
uniform float uToneGain;
uniform float uToneLift;
uniform int uUnlit;

out vec4 FragColor;

float ApplyTone(float b)
{
    b = clamp(b, 0.0, 1.0);
    float g = uToneGamma <= 0.0 ? 1.0 : uToneGamma;
    b = pow(b, g);
    b = (b * uToneGain) + uToneLift;
    return clamp(b, 0.0, 1.0);
}

void main()
{
    float b;
    if (uUnlit != 0)
    {
        b = 1.0;
    }
    else
    {
        float ndotl = abs(dot(normalize(vNormalView), normalize(uLightDirView)));
        b = uAmbient + (uDiffuseStrength * ndotl);
    }

    b = ApplyTone(b);

    FragColor = vec4(vec3(b), 1.0);
}
";
}
