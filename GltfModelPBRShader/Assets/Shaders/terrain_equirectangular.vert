#version 300 es

uniform vec3 u_CaptureCenter;    // 捕获点的世界坐标
uniform float u_FarPlane;        // 最大渲染距离

in vec3 a_position;  // 地形顶点为世界空间坐标
in vec2 a_texCoord;
in vec4 a_color;

out vec2 v_texCoord;
out vec4 v_color;
out float v_depth;

void main() {
    vec3 worldPos = a_position;

    // 从捕获中心出发的方向向量
    vec3 dir = normalize(worldPos - u_CaptureCenter);

    // 等距矩形投影
    float u = atan(dir.z, dir.x) / (2.0 * 3.14159265) + 0.5;
    float v_coord = asin(clamp(dir.y, -1.0, 1.0)) / 3.14159265 + 0.5;

    // 映射至 NDC [-1, 1]
    gl_Position = vec4(u * 2.0 - 1.0, v_coord * 2.0 - 1.0, 0.0, 1.0);

    // 透传
    v_texCoord = a_texCoord;
    v_color = a_color;

    // 基于距离的深度值（用于雾效）
    v_depth = length(worldPos - u_CaptureCenter) / u_FarPlane;
}
