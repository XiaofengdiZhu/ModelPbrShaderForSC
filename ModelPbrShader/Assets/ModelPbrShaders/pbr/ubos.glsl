// shaders/pbr/ubos.glsl
// UBO 定义文件，供 pbr.frag 包含
// 必须与 UniformBuffer.cs 中的结构体完全匹配

// ============================================================================
// SceneData UBO - 场景级数据 (Binding Point 0)
// Total: 80 bytes
// ============================================================================
layout(std140) uniform SceneData {
    vec4 CameraPos;// offset 0, xyz: camera position
    float Exposure;// offset 16
    float EnvironmentStrength;// offset 20, IBL 环境贴图强度
    int MipCount;// offset 24, IBL mipmap count
    float _Padding0;// offset 28
    // EnvRotation stored as mat3 (3 columns, each in a vec4)
    vec4 EnvRotationCol0;// offset 32, xyz: first column
    vec4 EnvRotationCol1;// offset 48, xyz: second column
    vec4 EnvRotationCol2;// offset 64, xyz: third column
} scene;

// ============================================================================
// MaterialCoreData UBO - 材质核心数据 (Binding Point 1)
// Total: 112 bytes
// ============================================================================
layout(std140) uniform MaterialCoreData {
    // ============ PBR Core ============
    vec4 BaseColorFactor;     // offset 0
    vec4 EmissiveFactor;      // offset 16

    float MetallicFactor;     // offset 32
    float RoughnessFactor;    // offset 36
    float NormalScale;        // offset 40
    float OcclusionStrength;  // offset 44

    // ============ Alpha ============
    int AlphaMode;            // offset 48
    float AlphaCutoff;        // offset 52
    int UseGeneratedTangents; // offset 56
    float _CorePad0;          // offset 60

    // ============ UV Indices ============
    int BaseColorUVSet;       // offset 64
    int MetallicRoughnessUVSet; // offset 68
    int NormalUVSet;          // offset 72
    int OcclusionUVSet;       // offset 76
    int EmissiveUVSet;        // offset 80
    int DiffuseUVSet;         // offset 84
    int SpecularGlossinessUVSet; // offset 88
    int _CoreUVPad0;          // offset 92

    // ============ Flags ============
    int ExtensionFlags;       // offset 96
    int TextureFlags;         // offset 100
    int _FlagsPad0;           // offset 104
    int _FlagsPad1;           // offset 108
} MaterialCore;

// ============================================================================
// MaterialExtensionData UBO - 材质扩展数据 (Binding Point 6)
// Total: 336 bytes
// ============================================================================
layout(std140) uniform MaterialExtensionData {
    // ============ IOR ============
    float Ior;                // offset 0
    float _IorPad0;           // offset 4
    float _IorPad1;           // offset 8
    float _IorPad2;           // offset 12

    // ============ Emissive Strength ============
    float EmissiveStrength;   // offset 16
    float _EmissiveStrPad0;   // offset 20
    float _EmissiveStrPad1;   // offset 24
    float _EmissiveStrPad2;   // offset 28

    // ============ Specular ============
    float SpecularFactor;     // offset 32
    float _SpecularPad0;      // offset 36
    float _SpecularPad1;      // offset 40
    float _SpecularPad2;      // offset 44
    vec4 SpecularColorFactor; // offset 48

    // ============ Sheen ============
    vec4 SheenColorFactor;    // offset 64
    float SheenRoughnessFactor; // offset 80
    float _SheenPad0;         // offset 84
    float _SheenPad1;         // offset 88
    float _SheenPad2;         // offset 92

    // ============ ClearCoat ============
    float ClearCoatFactor;    // offset 96
    float ClearCoatRoughness; // offset 100
    float ClearCoatNormalScale; // offset 104
    float _ClearCoatPad0;     // offset 108

    // ============ Transmission ============
    float TransmissionFactor; // offset 112
    float _TransmissionPad0;  // offset 116
    float _TransmissionPad1;  // offset 120
    float _TransmissionPad2;  // offset 124

    // ============ Volume ============
    float ThicknessFactor;    // offset 128
    float AttenuationDistance; // offset 132
    float _VolumePad0;        // offset 136
    float _VolumePad1;        // offset 140
    vec4 AttenuationColor;    // offset 144

    // ============ Iridescence ============
    float IridescenceFactor;  // offset 160
    float IridescenceIor;     // offset 164
    float IridescenceThicknessMin; // offset 168
    float IridescenceThicknessMax; // offset 172

    // ============ Dispersion ============
    float Dispersion;         // offset 176
    float _DispersionPad0;    // offset 180
    float _DispersionPad1;    // offset 184
    float _DispersionPad2;    // offset 188

    // ============ Diffuse Transmission ============
    float DiffuseTransmissionFactor; // offset 192
    float _DiffTransPad0;     // offset 196
    float _DiffTransPad1;     // offset 200
    float _DiffTransPad2;     // offset 204
    vec4 DiffuseTransmissionColorFactor; // offset 208

    // ============ Anisotropy ============
    vec4 Anisotropy;          // offset 224

    // ============ Extension UV Sets ============
    int ClearCoatUVSet;       // offset 240
    int ClearCoatRoughnessUVSet; // offset 244
    int ClearCoatNormalUVSet; // offset 248
    int IridescenceUVSet;     // offset 252
    int IridescenceThicknessUVSet; // offset 256
    int SheenColorUVSet;      // offset 260
    int SheenRoughnessUVSet;  // offset 264
    int SpecularUVSet;        // offset 268
    int SpecularColorUVSet;   // offset 272
    int TransmissionUVSet;    // offset 276
    int ThicknessUVSet;       // offset 280
    int DiffuseTransmissionUVSet; // offset 284
    int DiffuseTransmissionColorUVSet; // offset 288
    int AnisotropyUVSet;      // offset 292
    int _UVSetPad0;           // offset 296
    int _UVSetPad1;           // offset 300

    // ============ SpecularGlossiness ============
    vec4 SpecularFactorSG;    // offset 304
    float GlossinessFactor;   // offset 320
    float _SGPad0;            // offset 324
    float _SGPad1;            // offset 328
    float _SGPad2;            // offset 332
} MaterialExt;

// ============================================================================
// LightsData UBO - 多光源支持 (Binding Point 2)
// Total: 528 bytes (16 + 8 * 64)
// ============================================================================
const int LIGHT_COUNT = 8;
const int LightType_Directional = 0;
const int LightType_Point = 1;
const int LightType_Spot = 2;
#define LIGHT_TYPE_CONSTANTS_DEFINED

struct Light {
    vec3 direction;// offset 0
    float range;// offset 12
    vec3 color;// offset 16
    float intensity;// offset 28
    vec3 position;// offset 32
    float innerConeCos;// offset 44
    float outerConeCos;// offset 48
    int type;// offset 52
    float pad0;// offset 56
    float pad1;// offset 60
// Total: 64 bytes
};
#define LIGHT_STRUCT_DEFINED

layout(std140) uniform LightsData {
    int LightCount;// offset 0
    int pad0;// offset 4
    int pad1;// offset 8
    int pad2;// offset 12
    Light lights[LIGHT_COUNT];// offset 16
} lightsData;

// ============================================================================
// 便捷访问宏（兼容官方 uniform 名称）
// ============================================================================
// 官方 uniform 名称映射
#define u_Camera scene.CameraPos.xyz
#define u_Exposure scene.Exposure
#define u_EnvIntensity scene.EnvironmentStrength
#define u_MipCount scene.MipCount
// EnvRotation: construct mat3 from 3 vec4 columns (using xyz)
#define u_EnvRotation mat3(scene.EnvRotationCol0.xyz, scene.EnvRotationCol1.xyz, scene.EnvRotationCol2.xyz)
#define u_MetallicFactor MaterialCore.MetallicFactor
#define u_RoughnessFactor MaterialCore.RoughnessFactor
#define u_BaseColorFactor MaterialCore.BaseColorFactor
#define u_EmissiveFactor MaterialCore.EmissiveFactor.xyz
#define u_NormalScale MaterialCore.NormalScale
#define u_OcclusionStrength MaterialCore.OcclusionStrength
#define u_AlphaCutoff MaterialCore.AlphaCutoff

// Extension factors (from MaterialExt)
#define u_Ior MaterialExt.Ior
#define u_EmissiveStrength MaterialExt.EmissiveStrength
#define u_ClearcoatFactor MaterialExt.ClearCoatFactor
#define u_ClearcoatRoughnessFactor MaterialExt.ClearCoatRoughness
#define u_ClearcoatNormalScale MaterialExt.ClearCoatNormalScale
#define u_SheenColorFactor MaterialExt.SheenColorFactor.xyz
#define u_SheenRoughnessFactor MaterialExt.SheenRoughnessFactor
#define u_KHR_materials_specular_specularFactor MaterialExt.SpecularFactor
#define u_KHR_materials_specular_specularColorFactor MaterialExt.SpecularColorFactor.xyz
#define u_TransmissionFactor MaterialExt.TransmissionFactor
#define u_ThicknessFactor MaterialExt.ThicknessFactor
#define u_AttenuationColor MaterialExt.AttenuationColor.xyz
#define u_AttenuationDistance MaterialExt.AttenuationDistance
#define u_IridescenceFactor MaterialExt.IridescenceFactor
#define u_IridescenceIor MaterialExt.IridescenceIor
#define u_IridescenceThicknessMinimum MaterialExt.IridescenceThicknessMin
#define u_IridescenceThicknessMaximum MaterialExt.IridescenceThicknessMax
#define u_Dispersion MaterialExt.Dispersion
#define u_DiffuseTransmissionFactor MaterialExt.DiffuseTransmissionFactor
#define u_DiffuseTransmissionColorFactor MaterialExt.DiffuseTransmissionColorFactor.xyz
#define u_Anisotropy MaterialExt.Anisotropy

// UV Set 访问 (Core)
#define u_BaseColorUVSet MaterialCore.BaseColorUVSet
#define u_MetallicRoughnessUVSet MaterialCore.MetallicRoughnessUVSet
#define u_NormalUVSet MaterialCore.NormalUVSet
#define u_OcclusionUVSet MaterialCore.OcclusionUVSet
#define u_EmissiveUVSet MaterialCore.EmissiveUVSet
#define u_DiffuseUVSet MaterialCore.DiffuseUVSet
#define u_SpecularGlossinessUVSet MaterialCore.SpecularGlossinessUVSet

// UV Set 访问 (Extension)
#define u_ClearcoatUVSet MaterialExt.ClearCoatUVSet
#define u_ClearcoatRoughnessUVSet MaterialExt.ClearCoatRoughnessUVSet
#define u_ClearcoatNormalUVSet MaterialExt.ClearCoatNormalUVSet
#define u_IridescenceUVSet MaterialExt.IridescenceUVSet
#define u_IridescenceThicknessUVSet MaterialExt.IridescenceThicknessUVSet
#define u_SheenColorUVSet MaterialExt.SheenColorUVSet
#define u_SheenRoughnessUVSet MaterialExt.SheenRoughnessUVSet
#define u_SpecularUVSet MaterialExt.SpecularUVSet
#define u_SpecularColorUVSet MaterialExt.SpecularColorUVSet
#define u_TransmissionUVSet MaterialExt.TransmissionUVSet
#define u_ThicknessUVSet MaterialExt.ThicknessUVSet
#define u_DiffuseTransmissionUVSet MaterialExt.DiffuseTransmissionUVSet
#define u_DiffuseTransmissionColorUVSet MaterialExt.DiffuseTransmissionColorUVSet
#define u_AnisotropyUVSet MaterialExt.AnisotropyUVSet

// Flags
#define u_ExtensionFlags MaterialCore.ExtensionFlags
#define u_TextureFlags MaterialCore.TextureFlags

// Light access
#define u_Lights lightsData.lights
#define u_LightCount lightsData.LightCount

// ============================================================================
// RenderStateData UBO - 渲染状态矩阵 (Binding Point 3)
// Total: 320 bytes
// ============================================================================
layout(std140) uniform RenderStateData {
    mat4 ViewProjectionMatrix;  // offset 0
    mat4 ViewMatrix;            // offset 64
    mat4 ProjectionMatrix;      // offset 128
    mat4 ModelMatrix;           // offset 192
    mat4 NormalMatrix;          // offset 256
} renderState;

// 便捷访问宏
#define u_ViewProjectionMatrix renderState.ViewProjectionMatrix
#define u_ViewMatrix renderState.ViewMatrix
#define u_ProjectionMatrix renderState.ProjectionMatrix
#define u_ModelMatrix renderState.ModelMatrix
#define u_NormalMatrix renderState.NormalMatrix

// ============================================================================
// UVTransformData UBO - UV 变换矩阵 (Binding Point 4)
// Total: 1008 bytes (21 * 48)
// ============================================================================
// Helper: mat3 from 3 vec4 columns
#define MAKE_MAT3(col0, col1, col2) mat3((col0).xyz, (col1).xyz, (col2).xyz)

struct UVMatrix3 {
    vec4 col0;
    vec4 col1;
    vec4 col2;
};

layout(std140) uniform UVTransformData {
    // Core textures
    UVMatrix3 NormalUVTransform;
    UVMatrix3 EmissiveUVTransform;
    UVMatrix3 OcclusionUVTransform;

    // MetallicRoughness
    UVMatrix3 BaseColorUVTransform;
    UVMatrix3 MetallicRoughnessUVTransform;

    // SpecularGlossiness
    UVMatrix3 DiffuseUVTransform;
    UVMatrix3 SpecularGlossinessUVTransform;

    // ClearCoat
    UVMatrix3 ClearcoatUVTransform;
    UVMatrix3 ClearcoatRoughnessUVTransform;
    UVMatrix3 ClearcoatNormalUVTransform;

    // Sheen
    UVMatrix3 SheenColorUVTransform;
    UVMatrix3 SheenRoughnessUVTransform;

    // Specular
    UVMatrix3 SpecularUVTransform;
    UVMatrix3 SpecularColorUVTransform;

    // Transmission
    UVMatrix3 TransmissionUVTransform;

    // Volume
    UVMatrix3 ThicknessUVTransform;

    // Iridescence
    UVMatrix3 IridescenceUVTransform;
    UVMatrix3 IridescenceThicknessUVTransform;

    // Diffuse Transmission
    UVMatrix3 DiffuseTransmissionUVTransform;
    UVMatrix3 DiffuseTransmissionColorUVTransform;

    // Anisotropy
    UVMatrix3 AnisotropyUVTransform;
} uvTransform;

// UV Transform 便捷访问宏
#define u_NormalUVTransform MAKE_MAT3(uvTransform.NormalUVTransform.col0, uvTransform.NormalUVTransform.col1, uvTransform.NormalUVTransform.col2)
#define u_EmissiveUVTransform MAKE_MAT3(uvTransform.EmissiveUVTransform.col0, uvTransform.EmissiveUVTransform.col1, uvTransform.EmissiveUVTransform.col2)
#define u_OcclusionUVTransform MAKE_MAT3(uvTransform.OcclusionUVTransform.col0, uvTransform.OcclusionUVTransform.col1, uvTransform.OcclusionUVTransform.col2)
#define u_BaseColorUVTransform MAKE_MAT3(uvTransform.BaseColorUVTransform.col0, uvTransform.BaseColorUVTransform.col1, uvTransform.BaseColorUVTransform.col2)
#define u_MetallicRoughnessUVTransform MAKE_MAT3(uvTransform.MetallicRoughnessUVTransform.col0, uvTransform.MetallicRoughnessUVTransform.col1, uvTransform.MetallicRoughnessUVTransform.col2)
#define u_DiffuseUVTransform MAKE_MAT3(uvTransform.DiffuseUVTransform.col0, uvTransform.DiffuseUVTransform.col1, uvTransform.DiffuseUVTransform.col2)
#define u_SpecularGlossinessUVTransform MAKE_MAT3(uvTransform.SpecularGlossinessUVTransform.col0, uvTransform.SpecularGlossinessUVTransform.col1, uvTransform.SpecularGlossinessUVTransform.col2)
#define u_ClearcoatUVTransform MAKE_MAT3(uvTransform.ClearcoatUVTransform.col0, uvTransform.ClearcoatUVTransform.col1, uvTransform.ClearcoatUVTransform.col2)
#define u_ClearcoatRoughnessUVTransform MAKE_MAT3(uvTransform.ClearcoatRoughnessUVTransform.col0, uvTransform.ClearcoatRoughnessUVTransform.col1, uvTransform.ClearcoatRoughnessUVTransform.col2)
#define u_ClearcoatNormalUVTransform MAKE_MAT3(uvTransform.ClearcoatNormalUVTransform.col0, uvTransform.ClearcoatNormalUVTransform.col1, uvTransform.ClearcoatNormalUVTransform.col2)
#define u_SheenColorUVTransform MAKE_MAT3(uvTransform.SheenColorUVTransform.col0, uvTransform.SheenColorUVTransform.col1, uvTransform.SheenColorUVTransform.col2)
#define u_SheenRoughnessUVTransform MAKE_MAT3(uvTransform.SheenRoughnessUVTransform.col0, uvTransform.SheenRoughnessUVTransform.col1, uvTransform.SheenRoughnessUVTransform.col2)
#define u_SpecularUVTransform MAKE_MAT3(uvTransform.SpecularUVTransform.col0, uvTransform.SpecularUVTransform.col1, uvTransform.SpecularUVTransform.col2)
#define u_SpecularColorUVTransform MAKE_MAT3(uvTransform.SpecularColorUVTransform.col0, uvTransform.SpecularColorUVTransform.col1, uvTransform.SpecularColorUVTransform.col2)
#define u_TransmissionUVTransform MAKE_MAT3(uvTransform.TransmissionUVTransform.col0, uvTransform.TransmissionUVTransform.col1, uvTransform.TransmissionUVTransform.col2)
#define u_ThicknessUVTransform MAKE_MAT3(uvTransform.ThicknessUVTransform.col0, uvTransform.ThicknessUVTransform.col1, uvTransform.ThicknessUVTransform.col2)
#define u_IridescenceUVTransform MAKE_MAT3(uvTransform.IridescenceUVTransform.col0, uvTransform.IridescenceUVTransform.col1, uvTransform.IridescenceUVTransform.col2)
#define u_IridescenceThicknessUVTransform MAKE_MAT3(uvTransform.IridescenceThicknessUVTransform.col0, uvTransform.IridescenceThicknessUVTransform.col1, uvTransform.IridescenceThicknessUVTransform.col2)
#define u_DiffuseTransmissionUVTransform MAKE_MAT3(uvTransform.DiffuseTransmissionUVTransform.col0, uvTransform.DiffuseTransmissionUVTransform.col1, uvTransform.DiffuseTransmissionUVTransform.col2)
#define u_DiffuseTransmissionColorUVTransform MAKE_MAT3(uvTransform.DiffuseTransmissionColorUVTransform.col0, uvTransform.DiffuseTransmissionColorUVTransform.col1, uvTransform.DiffuseTransmissionColorUVTransform.col2)
#define u_AnisotropyUVTransform MAKE_MAT3(uvTransform.AnisotropyUVTransform.col0, uvTransform.AnisotropyUVTransform.col1, uvTransform.AnisotropyUVTransform.col2)

// ============================================================================
// VolumeScatterData UBO - Volume Scatter 数据 (Binding Point 5)
// Total: 32 bytes
// ============================================================================
#ifdef MATERIAL_VOLUME_SCATTER
layout(std140) uniform VolumeScatterData {
    vec4 MultiScatterColor;  // offset 0, xyz: multi-scatter color
    float MinRadius;         // offset 16
    int MaterialID;          // offset 20
    int FramebufferWidth;    // offset 24
    int FramebufferHeight;   // offset 28
} volumeScatter;

// Volume Scatter 便捷访问宏
#define u_MultiScatterColor volumeScatter.MultiScatterColor.xyz
#define u_MinRadius volumeScatter.MinRadius
#define u_MaterialID volumeScatter.MaterialID
// u_FramebufferSize 使用 ivec2 构造
#define u_FramebufferSize ivec2(volumeScatter.FramebufferWidth, volumeScatter.FramebufferHeight)
#endif
