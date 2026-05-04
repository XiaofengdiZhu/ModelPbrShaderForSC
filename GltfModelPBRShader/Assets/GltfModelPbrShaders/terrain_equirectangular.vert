// <Semantic Name='POSITION' Attribute='a_position' />
// <Semantic Name='COLOR' Attribute='a_color' />
// <Semantic Name='TEXCOORD' Attribute='a_texcoord' />

#version 300 es

uniform vec3 u_CaptureCenter;
uniform float u_FarPlane;

in vec3 a_position;
in vec2 a_texcoord;
in vec4 a_color;

out vec2 v_texcoord;
out vec4 v_color;
out float v_depth;

void main() {
    vec3 worldPos = a_position;

    // Equirectangular projection
    // dir: 从捕获中心指向顶点的方向向量
    vec3 dir = normalize(worldPos - u_CaptureCenter);

    // u: 水平角度
    // Survivalcraft 坐标系：+X 右，+Z 前，+Y 上
    // 等距矩形贴图：u=0.5 对应前方 (+Z)
    // atan(-dir.z, dir.x) 让前方映射到 u=0.5
    float u = atan(-dir.z, dir.x) / 6.28318530718 + 0.5;

    // v: 垂直角度，翻转使 v=0 对应顶部（天顶）
    // asin(dir.y) 给出仰角，-pi/2 到 pi/2
    // 翻转: v = 0.5 - asin(dir.y) / pi
    float v_coord = 0.5 - asin(clamp(dir.y, -1.0, 1.0)) / 3.14159265359;

    // 深度值用于遮挡测试：近处物体遮挡远处物体
    v_depth = length(worldPos - u_CaptureCenter) / u_FarPlane;

    // 将深度映射到 NDC z 范围 [-1, 1]，使深度测试生效
    gl_Position = vec4(u * 2.0 - 1.0, v_coord * 2.0 - 1.0, v_depth * 2.0 - 1.0, 1.0);

    v_texcoord = a_texcoord;
    v_color = a_color;
}
