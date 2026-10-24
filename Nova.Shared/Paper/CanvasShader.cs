namespace V12TwoDog.Paper
{
    /// <summary>
    /// Godot <c>canvas_item</c> port of Quill's canonical <c>Canvas.slang</c>
    /// (Anthology: <c>Quill/Quill/Shaders/Canvas.slang</c>), the same shader the
    /// OpenTK sample compiles. Every uniform keeps its Quill name so the renderer
    /// can feed <c>DrawCall</c> state straight through.
    ///
    /// Differences from the OpenTK GL port:
    ///  - Godot transforms canvas vertices itself, so the projection matrix is gone.
    ///  - <c>VERTEX</c> is already in 2D canvas space (top-left origin, Y down), which
    ///    matches Quill's pixel space, so <c>fragPos</c> passes through untouched.
    ///  - Blending is <c>blend_premul_alpha</c> (src + dst*(1-srcA)), matching Quill's
    ///    <c>Blend One, OneMinusSrcAlpha</c>; Quill premultiplies vertex colors.
    ///  - The backdrop-blur branch is dropped (no framebuffer read-back in this host).
    /// </summary>
    public static class CanvasShader
    {
        public const string Code = @"shader_type canvas_item;
render_mode blend_premul_alpha, unshaded;

// --- scissor ---
uniform vec4 scissorTransform = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec2 scissorTranslation = vec2(0.0, 0.0);
uniform vec2 scissorExt = vec2(-1.0, -1.0);

// --- brush ---
uniform vec4 brushTransform = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec2 brushTranslation = vec2(0.0, 0.0);
uniform int brushType = 0;
uniform vec4 brushColor1 = vec4(1.0);
uniform vec4 brushColor2 = vec4(1.0);
uniform vec4 brushParams = vec4(0.0);
uniform vec2 brushParams2 = vec2(0.0);

// --- texture ---
uniform vec4 textureTransform = vec4(1.0, 0.0, 0.0, 1.0);
uniform vec2 textureTranslation = vec2(0.0, 0.0);

// --- text ---
uniform vec2 atlasTexelSize = vec2(0.0009765625, 0.0009765625);
uniform float sdfPxRange = 4.0;

uniform sampler2D tex0 : filter_linear, repeat_disable;
uniform sampler2D fontTex : filter_linear, repeat_disable;

varying vec2 vFragPos;
varying vec2 vFragUV;
varying vec4 vFragColor;

vec2 apply_transform(vec4 t, vec2 translation, vec2 p) {
    return vec2(dot(t.xy, p), dot(t.zw, p)) + translation;
}

float brush_factor(vec2 p) {
    vec2 q = apply_transform(brushTransform, brushTranslation, p);
    if (brushType == 1) {
        vec2 start = brushParams.xy;
        vec2 line = brushParams.zw - start;
        float len = length(line);
        if (len < 0.001) { return 0.0; }
        return clamp(dot(q - start, line) / (len * len), 0.0, 1.0);
    }
    if (brushType == 2) {
        return clamp(smoothstep(brushParams.z, brushParams.w, length(q - brushParams.xy)), 0.0, 1.0);
    }
    if (brushType == 3) {
        vec2 center = brushParams.xy;
        vec2 halfSize = brushParams.zw;
        float radius = brushParams2.x;
        float feather = brushParams2.y;
        if (halfSize.x < 0.001 || halfSize.y < 0.001) { return 0.0; }
        vec2 d = abs(q - center) - (halfSize - vec2(radius));
        float dist = min(max(d.x, d.y), 0.0) + length(max(d, vec2(0.0))) - radius;
        return smoothstep(-feather * 0.5, feather * 0.5, dist);
    }
    return 0.0;
}

float scissor_mask(vec2 p) {
    if (scissorExt.x < 0.0 || scissorExt.y < 0.0) { return 1.0; }
    vec2 smoothEdges = scissorExt - abs(apply_transform(scissorTransform, scissorTranslation, p));
    return clamp(smoothEdges.x, 0.0, 1.0) * clamp(smoothEdges.y, 0.0, 1.0);
}

float sdf_screen_px_range(vec2 uv) {
    vec2 unitRange = vec2(sdfPxRange) * atlasTexelSize;
    vec2 screenTexSize = vec2(1.0) / fwidth(uv);
    vec2 range = unitRange * screenTexSize;
    return max(min(range.x, range.y), 1.0);
}

void vertex() {
    vFragPos = VERTEX.xy;
    vFragUV = UV;
    vFragColor = COLOR;
}

void fragment() {
    float mask = scissor_mask(vFragPos);

    vec4 color = vFragColor;
    if (brushType > 0) {
        color = mix(brushColor1, brushColor2, vec4(brush_factor(vFragPos)));
    }

    vec4 outColor;

    // Text mode: UV.x >= 2 marks a glyph-atlas sample (Quill offsets glyph UVs by 2).
    if (vFragUV.x >= 2.0) {
        vec2 uv = vFragUV - vec2(2.0);
        float sd = texture(fontTex, uv).r;
        float screenPxDistance = sdf_screen_px_range(uv) * (sd - 0.5);
        float coverage = clamp(screenPxDistance + 0.5, 0.0, 1.0);
        outColor = color * coverage * mask;
    } else {
        // Shape edge anti-aliasing: coverage baked into the fringe vertices lives in UV.x.
        float edgeAlpha = clamp(vFragUV.x, 0.0, 1.0);
        vec4 fill = color * texture(tex0, apply_transform(textureTransform, textureTranslation, vFragPos));
        outColor = fill * edgeAlpha * mask;
    }

    COLOR = outColor;
}
";
    }
}
