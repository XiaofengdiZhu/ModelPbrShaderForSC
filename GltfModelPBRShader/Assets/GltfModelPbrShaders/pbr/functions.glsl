#ifndef FUNCTIONS_GLSL
#define FUNCTIONS_GLSL

// ============================================================================
// Debug output constants (must match GltfState.DebugOutput)
// ============================================================================
#define DEBUG_NONE 0
#define DEBUG_UV_0 1
#define DEBUG_UV_1 2
#define DEBUG_NORMAL_TEXTURE 3
#define DEBUG_NORMAL_SHADING 4
#define DEBUG_NORMAL_GEOMETRY 5
#define DEBUG_TANGENT 6
#define DEBUG_TANGENT_W 7
#define DEBUG_BITANGENT 8
#define DEBUG_ALPHA 9
#define DEBUG_OCCLUSION 10
#define DEBUG_EMISSIVE 11
#define DEBUG_METALLIC 12
#define DEBUG_ROUGHNESS 13
#define DEBUG_BASE_COLOR 14
#define DEBUG_CLEARCOAT_FACTOR 15
#define DEBUG_CLEARCOAT_ROUGHNESS 16
#define DEBUG_CLEARCOAT_NORMAL 17
#define DEBUG_SHEEN_COLOR 18
#define DEBUG_SHEEN_ROUGHNESS 19
#define DEBUG_SPECULAR_FACTOR 20
#define DEBUG_SPECULAR_COLOR 21
#define DEBUG_TRANSMISSION_FACTOR 22
#define DEBUG_VOLUME_THICKNESS 23
#define DEBUG_IRIDESCENCE_FACTOR 24
#define DEBUG_IRIDESCENCE_THICKNESS 25
#define DEBUG_ANISOTROPIC_STRENGTH 26
#define DEBUG_ANISOTROPIC_DIRECTION 27
#define DEBUG_DIFFUSE_TRANSMISSION_FACTOR 28
#define DEBUG_DIFFUSE_TRANSMISSION_COLOR_FACTOR 29
#define DEBUG_VOLUME_SCATTER_MULTI_SCATTER_COLOR 30
#define DEBUG_VOLUME_SCATTER_SINGLE_SCATTER_COLOR 31
#define DEBUG_IBL_RAW 32
#define DEBUG_NDOTV 33
#define DEBUG_FRESNEL_DIELECTRIC 34
#define DEBUG_IBL_DIFFUSE 35
#define DEBUG_IBL_SPECULAR 36

// Default DEBUG to DEBUG_NONE if not defined
#ifndef DEBUG
#define DEBUG DEBUG_NONE
#endif

// ============================================================================
// Alpha mode constants
// ============================================================================
#define ALPHAMODE_OPAQUE 0
#define ALPHAMODE_MASK 1
#define ALPHAMODE_BLEND 2

// Default ALPHAMODE if not defined
#ifndef ALPHAMODE
#define ALPHAMODE ALPHAMODE_OPAQUE
#endif

const float M_PI = 3.141592653589793;


in vec3 v_Position;


#ifdef HAS_NORMAL_VEC3
#ifdef HAS_TANGENT_VEC4
in mat3 v_TBN;
#else
in vec3 v_Normal;
#endif
#endif


#ifdef HAS_COLOR_0_VEC3
in vec3 v_Color;
#endif
#ifdef HAS_COLOR_0_VEC4
in vec4 v_Color;
#endif


vec4 getVertexColor()
{
    vec4 color = vec4(1.0);

    #ifdef HAS_COLOR_0_VEC3
    color.rgb = v_Color.rgb;
    #endif
    #ifdef HAS_COLOR_0_VEC4
    color = v_Color;
    #endif

    return color;
}


struct NormalInfo {
    vec3 ng;// Geometry normal
    vec3 t;// Geometry tangent
    vec3 b;// Geometry bitangent
    vec3 n;// Shading normal
    vec3 ntex;// Normal from texture, scaling is accounted for.
#if DEBUG == DEBUG_TANGENT_W
    float tangentWSign;// W component of the tangent attribute, used to determine handedness of TBN matrix
#endif
};


float clampedDot(vec3 x, vec3 y)
{
    return clamp(dot(x, y), 0.0, 1.0);
}


float max3(vec3 v)
{
    return max(max(v.x, v.y), v.z);
}


float sq(float t)
{
    return t * t;
}

vec2 sq(vec2 t)
{
    return t * t;
}

vec3 sq(vec3 t)
{
    return t * t;
}

vec4 sq(vec4 t)
{
    return t * t;
}


float applyIorToRoughness(float roughness, float ior)
{
    // Scale roughness with IOR so that an IOR of 1.0 results in no microfacet refraction and
    // an IOR of 1.5 results in the default amount of microfacet refraction.
    return roughness * clamp(ior * 2.0 - 2.0, 0.0, 1.0);
}

vec3 rgb_mix(vec3 base, vec3 layer, vec3 rgb_alpha)
{
    float rgb_alpha_max = max(rgb_alpha.r, max(rgb_alpha.g, rgb_alpha.b));
    return (1.0 - rgb_alpha_max) * base + rgb_alpha * layer;
}

#endif// FUNCTIONS_GLSL

