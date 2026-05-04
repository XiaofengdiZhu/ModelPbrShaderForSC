#version 300 es
precision highp float;

in vec2 v_texCoord;
in vec4 v_color;
in float v_depth;

uniform sampler2D u_texture;
uniform vec3 u_fogColor;
uniform float u_fogDensity;

#ifdef ALPHATESTED
uniform float u_alphaThreshold;
#endif

out vec4 fragColor;

void main() {
    vec4 texColor = texture(u_texture, v_texCoord);
    vec4 color = texColor * v_color;

#ifdef ALPHATESTED
    // Alpha test for foliage and other cutout geometry
    if (color.a < u_alphaThreshold) {
        discard;
    }
#endif

    // Optional distance fog
    if (u_fogDensity > 0.0) {
        float fogFactor = 1.0 - exp(-u_fogDensity * v_depth * v_depth);
        color.rgb = mix(color.rgb, u_fogColor, clamp(fogFactor, 0.0, 1.0));
    }

    fragColor = color;
}
