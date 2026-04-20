// ============================================================================
// IBL (Image-Based Lighting) Module
// Based on glTF-Sample-Renderer implementation
// ============================================================================

// IBL Samplers (must be defined before including this file)
// uniform samplerCube u_LambertianEnvSampler;
// uniform samplerCube u_GGXEnvSampler;
// uniform samplerCube u_CharlieEnvSampler;
// uniform sampler2D u_GGXLUT;
// uniform sampler2D u_CharlieLUT;
// uniform int u_MipCount;
// uniform float u_EnvIntensity;
// uniform mat3 u_EnvRotation;

// Clamped dot product to avoid numerical issues
float clampedDot(vec3 a, vec3 b) {
    return clamp(dot(a, b), 0.001, 1.0);
}

// Get diffuse irradiance from Lambertian cubemap
vec3 getDiffuseLight(vec3 n) {
    vec3 irradiance = texture(u_LambertianEnvSampler, u_EnvRotation * n).rgb;
    return irradiance * u_EnvIntensity;
}

// Get specular radiance from GGX prefiltered cubemap
vec3 getIBLRadianceGGX(vec3 n, vec3 v, float roughness) {
    float NdotV = clampedDot(n, v);
    float lod = roughness * float(u_MipCount - 1);
    vec3 reflection = normalize(reflect(-v, n));
    vec3 specularLight = textureLod(u_GGXEnvSampler, u_EnvRotation * reflection, lod).rgb;
    return specularLight * u_EnvIntensity;
}

// Get specular sample with explicit LOD
vec4 getSpecularSample(vec3 reflection, float lod) {
    vec4 sampleColor = textureLod(u_GGXEnvSampler, u_EnvRotation * reflection, lod);
    return vec4(sampleColor.rgb * u_EnvIntensity, sampleColor.a);
}

// Get sheen sample from Charlie prefiltered cubemap
vec4 getSheenSample(vec3 reflection, float lod) {
    vec4 sampleColor = textureLod(u_CharlieEnvSampler, u_EnvRotation * reflection, lod);
    return vec4(sampleColor.rgb * u_EnvIntensity, sampleColor.a);
}

// IBL GGX Fresnel using BRDF LUT
// Implements single scattering + multiple scattering approximation from Fdez-Aguera
vec3 getIBLGGXFresnel(vec3 n, vec3 v, float roughness, vec3 F0, float specularWeight) {
    float NdotV = clampedDot(n, v);
    vec2 brdfSamplePoint = clamp(vec2(NdotV, roughness), vec2(0.0, 0.0), vec2(1.0, 1.0));
    vec2 f_ab = texture(u_GGXLUT, brdfSamplePoint).rg;

    // Roughness dependent Fresnel
    vec3 Fr = max(vec3(1.0 - roughness), F0) - F0;
    vec3 k_S = F0 + Fr * pow(1.0 - NdotV, 5.0);
    vec3 FssEss = specularWeight * (k_S * f_ab.x + f_ab.y);

    // Multiple scattering approximation
    float Ems = (1.0 - (f_ab.x + f_ab.y));
    vec3 F_avg = specularWeight * (F0 + (1.0 - F0) / 21.0);
    vec3 FmsEms = Ems * FssEss * F_avg / (1.0 - F_avg * Ems);

    return FssEss + FmsEms;
}

// Simplified version without specularWeight
vec3 getIBLGGXFresnel(vec3 n, vec3 v, float roughness, vec3 F0) {
    return getIBLGGXFresnel(n, v, roughness, F0, 1.0);
}

// IBL Sheen (Charlie distribution)
vec3 getIBLRadianceCharlie(vec3 n, vec3 v, float sheenRoughness, vec3 sheenColor) {
    float NdotV = clampedDot(n, v);
    float lod = sheenRoughness * float(u_MipCount - 1);
    vec3 reflection = normalize(reflect(-v, n));

    vec2 brdfSamplePoint = clamp(vec2(NdotV, sheenRoughness), vec2(0.0, 0.0), vec2(1.0, 1.0));
    float brdf = texture(u_CharlieLUT, brdfSamplePoint).b;
    vec3 sheenSample = textureLod(u_CharlieEnvSampler, u_EnvRotation * reflection, lod).rgb;

    return sheenSample * sheenColor * brdf * u_EnvIntensity;
}

// Combined IBL contribution for standard PBR materials
vec3 getIBLContribution(vec3 n, vec3 v, float roughness, vec3 F0, vec3 diffuseColor, float metallic, float specularWeight) {
    // Diffuse IBL (Lambertian irradiance)
    vec3 diffuseLight = getDiffuseLight(n);
    vec3 diffuseContrib = (1.0 - metallic) * diffuseLight * diffuseColor;

    // Specular IBL (GGX prefiltered with Fresnel from LUT)
    vec3 specularLight = getIBLRadianceGGX(n, v, roughness);
    vec3 fresnel = getIBLGGXFresnel(n, v, roughness, F0, specularWeight);
    vec3 specularContrib = specularLight * fresnel;

    return diffuseContrib + specularContrib;
}
