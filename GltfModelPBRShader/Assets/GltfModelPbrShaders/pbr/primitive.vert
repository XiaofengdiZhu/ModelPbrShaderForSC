// Debug vertex output constants
#ifndef DEBUG_VERT_NONE
#define DEBUG_VERT_NONE 0
#endif
#ifndef DEBUG_VERT_TANGENT_W
#define DEBUG_VERT_TANGENT_W 1
#endif

// Default DEBUG_VERT to DEBUG_VERT_NONE if not defined
#ifndef DEBUG_VERT
#define DEBUG_VERT DEBUG_VERT_NONE
#endif

#include <animation.glsl>
#include <ubos.glsl>


in vec3 a_position;
out vec3 v_Position;

#ifdef HAS_NORMAL_VEC3
in vec3 a_normal;
#endif

#ifdef HAS_NORMAL_VEC3
#ifdef HAS_TANGENT_VEC4
in vec4 a_tangent;
out mat3 v_TBN;
#else
out vec3 v_Normal;
#endif
#endif

#if DEBUG_VERT == DEBUG_VERT_TANGENT_W
out float v_TangentWSign;
#endif

#ifdef USE_INSTANCING
out vec2 v_instanceLight;
#endif

#ifdef HAS_TEXCOORD_0_VEC2
in vec2 a_texcoord_0;
#endif

#ifdef HAS_TEXCOORD_1_VEC2
in vec2 a_texcoord_1;
#endif

out vec2 v_texcoord_0;
out vec2 v_texcoord_1;

#ifdef HAS_COLOR_0_VEC3
in vec3 a_color_0;
out vec3 v_Color;
#endif

#ifdef HAS_COLOR_0_VEC4
in vec4 a_color_0;
out vec4 v_Color;
#endif

#ifdef USE_INSTANCING
in mat4 a_instance_model_matrix;
in vec2 a_instance_light;
#endif

#ifdef HAS_VERT_NORMAL_UV_TRANSFORM
uniform mat3 u_vertNormalUVTransform;
#endif

vec4 getPosition()
{
    vec4 pos = vec4(a_position, 1.0);

    #ifdef USE_MORPHING
    pos += getTargetPosition(gl_VertexID);
    #endif

    #ifdef USE_SKINNING
    pos = getSkinningMatrix() * pos;
    #endif

    return pos;
}


#ifdef HAS_NORMAL_VEC3
vec3 getNormal()
{
    vec3 normal = a_normal;

    #ifdef USE_MORPHING
    normal += getTargetNormal(gl_VertexID);
    #endif

    #ifdef USE_SKINNING
    normal = mat3(getSkinningNormalMatrix()) * normal;
    #endif

    return normalize(normal);
}
#endif

#ifdef HAS_NORMAL_VEC3
#ifdef HAS_TANGENT_VEC4
vec3 getTangent()
{
    vec3 tangent = a_tangent.xyz;

    #ifdef USE_MORPHING
    tangent += getTargetTangent(gl_VertexID);
    #endif

    #ifdef USE_SKINNING
    tangent = mat3(getSkinningMatrix()) * tangent;
    #endif

    return normalize(tangent);
}
#endif
#endif


void main()
{
    gl_PointSize = 1.0f;
    #ifdef USE_INSTANCING
    mat4 modelMatrix = a_instance_model_matrix;
    // if you want to use non-uniform scale, replace the below line with `mat4 normalMatrix = transpose(inverse(modelMatrix));
    mat4 normalMatrix = mat4(mat3(modelMatrix));
    #else
    mat4 modelMatrix = u_ModelMatrix;
    mat4 normalMatrix = u_NormalMatrix;
    #endif

    vec4 localPos = getPosition();

    // v_Position in view space (for lighting)
    vec4 pos = modelMatrix * localPos;
    v_Position = vec3(pos.xyz) / pos.w;

    #if DEBUG_VERT == DEBUG_VERT_TANGENT_W
    v_TangentWSign = 1.0f;
    #endif

    #ifdef HAS_NORMAL_VEC3
    #ifdef HAS_TANGENT_VEC4
    vec3 tangent = getTangent();
    vec3 normalW = normalize(vec3(normalMatrix * vec4(getNormal(), 0.0)));
    vec3 tangentW = vec3(modelMatrix * vec4(tangent, 0.0));
    vec3 bitangentW = cross(normalW, tangentW) * a_tangent.w;
    #if DEBUG_VERT == DEBUG_VERT_TANGENT_W
    v_TangentWSign = a_tangent.w;
    #endif
    #ifdef HAS_VERT_NORMAL_UV_TRANSFORM
    tangentW = u_vertNormalUVTransform * tangentW;
    bitangentW = u_vertNormalUVTransform * bitangentW;
    #endif

    bitangentW = normalize(bitangentW);
    tangentW = normalize(tangentW);

    v_TBN = mat3(tangentW, bitangentW, normalW);
    #else
    v_Normal = normalize(vec3(normalMatrix * vec4(getNormal(), 0.0)));
    #endif
    #endif

    v_texcoord_0 = vec2(0.0, 0.0);
    v_texcoord_1 = vec2(0.0, 0.0);

    #ifdef HAS_TEXCOORD_0_VEC2
    v_texcoord_0 = a_texcoord_0;
    #endif

    #ifdef HAS_TEXCOORD_1_VEC2
    v_texcoord_1 = a_texcoord_1;
    #endif

    #ifdef USE_MORPHING
    v_texcoord_0 += getTargetTexCoord0(gl_VertexID);
    v_texcoord_1 += getTargetTexCoord1(gl_VertexID);
    #endif


    #if defined(HAS_COLOR_0_VEC3)
    v_Color = a_color_0;
    #if defined(USE_MORPHING)
    v_Color = clamp(v_Color + getTargetColor0(gl_VertexID).xyz, 0.0f, 1.0f);
    #endif
    #endif

    #if defined(HAS_COLOR_0_VEC4)
    v_Color = a_color_0;
    #if defined(USE_MORPHING)
    v_Color = clamp(v_Color + getTargetColor0(gl_VertexID), 0.0f, 1.0f);
    #endif
    #endif

    // Non-instanced: WVP already contains per-model world transform
    // Instanced: modelMatrix from per-instance attribute, Projection shared
#ifdef USE_INSTANCING
    gl_Position = u_ProjectionMatrix * modelMatrix * localPos;
    v_instanceLight = a_instance_light;
#else
    gl_Position = u_ViewProjectionMatrix * localPos;
#endif
    OPENGL_POSITION_FIX;
}
