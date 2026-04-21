using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Game {
    #region UBO Data Structures

    /// <summary>
    /// 场景数据 UBO（每帧更新一次）
    /// std140 布局：必须按 16 字节对齐
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SceneData {
        // Camera
        public Vector4 CameraPos; // 16 bytes (vec4，但只用 xyz)

        // Environment
        public float Exposure; // 4 bytes
        public float EnvironmentStrength; // 4 bytes - IBL 环境贴图强度
        public int MipCount; // 4 bytes - IBL mipmap count
        public float Padding0;

        // EnvRotation (mat3 stored as 3 vec4s for std140 alignment)
        // Each column of mat3 is stored in a vec4, using only xyz
        public Vector4 EnvRotationCol0; // 16 bytes, offset 32
        public Vector4 EnvRotationCol1; // 16 bytes, offset 48
        public Vector4 EnvRotationCol2; // 16 bytes, offset 64

        // Total: 80 bytes
    }

    /// <summary>
    /// 材质核心数据 UBO（高频更新，每个材质更新）
    /// Binding Point: 1
    /// std140 布局
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialCoreData {
        // ============ PBR Core ============
        public Vector4 BaseColorFactor; // 16 bytes, offset 0
        public Vector4 EmissiveFactor; // 16 bytes, offset 16

        public float MetallicFactor; // 4 bytes, offset 32
        public float RoughnessFactor; // 4 bytes, offset 36
        public float NormalScale; // 4 bytes, offset 40
        public float OcclusionStrength; // 4 bytes, offset 44

        // ============ Alpha ============
        public int AlphaMode; // 4 bytes, offset 48
        public float AlphaCutoff; // 4 bytes, offset 52
        public int UseGeneratedTangents; // 4 bytes, offset 56
        public float CorePadding0; // 4 bytes, offset 60

        // ============ UV Indices ============
        public int BaseColorUVSet; // 4 bytes, offset 64
        public int MetallicRoughnessUVSet; // 4 bytes, offset 68
        public int NormalUVSet; // 4 bytes, offset 72
        public int OcclusionUVSet; // 4 bytes, offset 76
        public int EmissiveUVSet; // 4 bytes, offset 80
        public int DiffuseUVSet; // 4 bytes, offset 84
        public int SpecularGlossinessUVSet; // 4 bytes, offset 88
        public int CoreUVPadding0; // 4 bytes, offset 92

        // ============ Flags ============
        public int ExtensionFlags; // 4 bytes, offset 96
        public int TextureFlags; // 4 bytes, offset 100
        public int FlagsPadding0; // 4 bytes, offset 104
        public int FlagsPadding1; // 4 bytes, offset 108

        // Total: 112 bytes
    }

    /// <summary>
    /// 材质扩展数据 UBO（仅在启用扩展时更新）
    /// Binding Point: 6
    /// std140 布局
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MaterialExtensionData {
        // ============ IOR ============
        public float Ior; // 4 bytes, offset 0
        public float IorPadding0; // 4 bytes, offset 4
        public float IorPadding1; // 4 bytes, offset 8
        public float IorPadding2; // 4 bytes, offset 12

        // ============ Emissive Strength ============
        public float EmissiveStrength; // 4 bytes, offset 16
        public float EmissiveStrengthPadding0; // 4 bytes, offset 20
        public float EmissiveStrengthPadding1; // 4 bytes, offset 24
        public float EmissiveStrengthPadding2; // 4 bytes, offset 28

        // ============ Specular ============
        public float SpecularFactor; // 4 bytes, offset 32
        public float SpecularPadding0; // 4 bytes, offset 36
        public float SpecularPadding1; // 4 bytes, offset 40
        public float SpecularPadding2; // 4 bytes, offset 44
        public Vector4 SpecularColorFactor; // 16 bytes, offset 48

        // ============ Sheen ============
        public Vector4 SheenColorFactor; // 16 bytes, offset 64
        public float SheenRoughnessFactor; // 4 bytes, offset 80
        public float SheenPadding0; // 4 bytes, offset 84
        public float SheenPadding1; // 4 bytes, offset 88
        public float SheenPadding2; // 4 bytes, offset 92

        // ============ ClearCoat ============
        public float ClearCoatFactor; // 4 bytes, offset 96
        public float ClearCoatRoughness; // 4 bytes, offset 100
        public float ClearCoatNormalScale; // 4 bytes, offset 104
        public float ClearCoatPadding0; // 4 bytes, offset 108

        // ============ Transmission ============
        public float TransmissionFactor; // 4 bytes, offset 112
        public float TransmissionPadding0; // 4 bytes, offset 116
        public float TransmissionPadding1; // 4 bytes, offset 120
        public float TransmissionPadding2; // 4 bytes, offset 124

        // ============ Volume ============
        public float ThicknessFactor; // 4 bytes, offset 128
        public float AttenuationDistance; // 4 bytes, offset 132
        public float VolumePadding0; // 4 bytes, offset 136
        public float VolumePadding1; // 4 bytes, offset 140
        public Vector4 AttenuationColor; // 16 bytes, offset 144

        // ============ Iridescence ============
        public float IridescenceFactor; // 4 bytes, offset 160
        public float IridescenceIor; // 4 bytes, offset 164
        public float IridescenceThicknessMin; // 4 bytes, offset 168
        public float IridescenceThicknessMax; // 4 bytes, offset 172

        // ============ Dispersion ============
        public float Dispersion; // 4 bytes, offset 176
        public float DispersionPadding0; // 4 bytes, offset 180
        public float DispersionPadding1; // 4 bytes, offset 184
        public float DispersionPadding2; // 4 bytes, offset 188

        // ============ Diffuse Transmission ============
        public float DiffuseTransmissionFactor; // 4 bytes, offset 192
        public float DiffuseTransmissionPadding0; // 4 bytes, offset 196
        public float DiffuseTransmissionPadding1; // 4 bytes, offset 200
        public float DiffuseTransmissionPadding2; // 4 bytes, offset 204
        public Vector4 DiffuseTransmissionColorFactor; // 16 bytes, offset 208

        // ============ Anisotropy ============
        public Vector4 Anisotropy; // 16 bytes, offset 224

        // ============ Extension UV Sets ============
        public int ClearCoatUVSet; // 4 bytes, offset 240
        public int ClearCoatRoughnessUVSet; // 4 bytes, offset 244
        public int ClearCoatNormalUVSet; // 4 bytes, offset 248
        public int IridescenceUVSet; // 4 bytes, offset 252
        public int IridescenceThicknessUVSet; // 4 bytes, offset 256
        public int SheenColorUVSet; // 4 bytes, offset 260
        public int SheenRoughnessUVSet; // 4 bytes, offset 264
        public int SpecularUVSet; // 4 bytes, offset 268
        public int SpecularColorUVSet; // 4 bytes, offset 272
        public int TransmissionUVSet; // 4 bytes, offset 276
        public int ThicknessUVSet; // 4 bytes, offset 280
        public int DiffuseTransmissionUVSet; // 4 bytes, offset 284
        public int DiffuseTransmissionColorUVSet; // 4 bytes, offset 288
        public int AnisotropyUVSet; // 4 bytes, offset 292
        public int UVSetPadding0; // 4 bytes, offset 296
        public int UVSetPadding1; // 4 bytes, offset 300

        // ============ SpecularGlossiness ============
        public Vector4 SpecularFactorSG; // 16 bytes, offset 304
        public float GlossinessFactor; // 4 bytes, offset 320
        public float SGPad0; // 4 bytes, offset 324
        public float SGPad1; // 4 bytes, offset 328
        public float SGPad2; // 4 bytes, offset 332

        // Total: 336 bytes
    }

    /// <summary>
    /// 纹理标志位（对应官方 HAS_XXX_MAP defines）
    /// </summary>
    [Flags]
    public enum TextureFlags {
        None = 0,

        // Core textures
        BaseColor = 1 << 0,
        MetallicRoughness = 1 << 1,
        Normal = 1 << 2,
        Occlusion = 1 << 3,
        Emissive = 1 << 4,

        // Clearcoat textures
        ClearCoat = 1 << 5,
        ClearCoatRoughness = 1 << 6,
        ClearCoatNormal = 1 << 7,

        // Iridescence textures
        Iridescence = 1 << 8,
        IridescenceThickness = 1 << 9,

        // Sheen textures
        SheenColor = 1 << 10,
        SheenRoughness = 1 << 11,

        // Specular textures
        Specular = 1 << 12,
        SpecularColor = 1 << 13,

        // Transmission textures
        Transmission = 1 << 14,

        // Volume textures
        Thickness = 1 << 15,

        // Diffuse Transmission textures
        DiffuseTransmission = 1 << 16,
        DiffuseTransmissionColor = 1 << 17,

        // Anisotropy texture
        Anisotropy = 1 << 18
    }

    /// <summary>
    /// 材质扩展标志位（对应官方 MATERIAL_XXX defines）
    /// </summary>
    [Flags]
    public enum ExtensionFlags {
        None = 0,
        MetallicRoughness = 1 << 0, // MATERIAL_METALLICROUGHNESS
        SpecularGlossiness = 1 << 1, // MATERIAL_SPECULARGLOSSINESS
        ClearCoat = 1 << 2, // MATERIAL_CLEARCOAT
        Sheen = 1 << 3, // MATERIAL_SHEEN
        Specular = 1 << 4, // MATERIAL_SPECULAR
        Transmission = 1 << 5, // MATERIAL_TRANSMISSION
        Volume = 1 << 6, // MATERIAL_VOLUME
        Iridescence = 1 << 7, // MATERIAL_IRIDESCENCE
        Ior = 1 << 8, // MATERIAL_IOR
        Anisotropy = 1 << 9, // MATERIAL_ANISOTROPY
        EmissiveStrength = 1 << 10, // MATERIAL_EMISSIVE_STRENGTH
        Dispersion = 1 << 11, // MATERIAL_DISPERSION
        DiffuseTransmission = 1 << 12, // MATERIAL_DIFFUSE_TRANSMISSION
        VolumeScatter = 1 << 13, // MATERIAL_VOLUME_SCATTER
        Unlit = 1 << 14 // MATERIAL_UNLIT
    }

    /// <summary>
    /// 材质默认值（来自 glTF 规范）
    /// </summary>
    public static class MaterialDefaults {
        public const float MetallicFactor = 1.0f;
        public const float RoughnessFactor = 1.0f;
        public const float NormalScale = 1.0f;
        public const float OcclusionStrength = 1.0f;
        public const float AlphaCutoff = 0.5f;
        public const float Ior = 1.5f;
        public const float EmissiveStrength = 1.0f;
        public const float SpecularFactor = 1.0f;
        public const float ClearCoatFactor = 0.0f;
        public const float ClearCoatRoughnessFactor = 0.0f;
        public const float TransmissionFactor = 0.0f;
        public const float ThicknessFactor = 0.0f;
        public const float IridescenceFactor = 0.0f;
        public const float IridescenceIor = 1.3f;
        public const float Dispersion = 0.0f;
        public const float DiffuseTransmissionFactor = 0.0f;
        public const float SheenRoughnessFactor = 0.0f;
    }

    #endregion

    #region Render State Data Structure

    /// <summary>
    /// 渲染状态数据 UBO（每帧更新一次）
    /// Binding Point: 3
    /// std140 布局
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct RenderStateData {
        public Matrix4x4 ViewProjectionMatrix; // 64 bytes, offset 0
        public Matrix4x4 ViewMatrix; // 64 bytes, offset 64
        public Matrix4x4 ProjectionMatrix; // 64 bytes, offset 128
        public Matrix4x4 ModelMatrix; // 64 bytes, offset 192

        public Matrix4x4 NormalMatrix; // 64 bytes, offset 256
        // Total: 320 bytes
    }

    #endregion

    #region Light Data Structures

    /// <summary>
    /// 光源数据结构（std140 布局）
    /// 必须与着色器中的 Light 结构体匹配
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightData {
        public Vector3 Direction; // 12 bytes
        public float Range; // 4 bytes

        public Vector3 Color; // 12 bytes
        public float Intensity; // 4 bytes

        public Vector3 Position; // 12 bytes
        public float InnerConeCos; // 4 bytes

        public float OuterConeCos; // 4 bytes
        public int Type; // 4 bytes (0=Directional, 1=Point, 2=Spot)
        public float Pad0; // 4 bytes
        public float Pad1; // 4 bytes

        // Total: 64 bytes (4 * vec4)
    }

    /// <summary>
    /// 光源数组 UBO（最多支持 8 个光源）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LightsData {
        public int LightCount; // 4 bytes
        public int Pad0; // 4 bytes
        public int Pad1; // 4 bytes
        public int Pad2; // 4 bytes

        // 固定数组，最多 8 个光源
        public LightData Light0;
        public LightData Light1;
        public LightData Light2;
        public LightData Light3;
        public LightData Light4;
        public LightData Light5;
        public LightData Light6;
        public LightData Light7;

        // Total: 16 + 8 * 64 = 528 bytes
    }

    #endregion

    #region UV Transform Data Structure

    /// <summary>
    /// UV 变换矩阵数据 UBO（每个材质更新）
    /// Binding Point: 4
    /// std140 布局：mat3 需要 48 字节（每列 16 字节对齐，使用 vec4 存储 xyz）
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UVTransformData {
        // Core textures
        public UVMatrix3 NormalUVTransform; // 48 bytes
        public UVMatrix3 EmissiveUVTransform; // 48 bytes
        public UVMatrix3 OcclusionUVTransform; // 48 bytes

        // MetallicRoughness
        public UVMatrix3 BaseColorUVTransform; // 48 bytes
        public UVMatrix3 MetallicRoughnessUVTransform; // 48 bytes

        // SpecularGlossiness
        public UVMatrix3 DiffuseUVTransform; // 48 bytes
        public UVMatrix3 SpecularGlossinessUVTransform; // 48 bytes

        // ClearCoat
        public UVMatrix3 ClearcoatUVTransform; // 48 bytes
        public UVMatrix3 ClearcoatRoughnessUVTransform; // 48 bytes
        public UVMatrix3 ClearcoatNormalUVTransform; // 48 bytes

        // Sheen
        public UVMatrix3 SheenColorUVTransform; // 48 bytes
        public UVMatrix3 SheenRoughnessUVTransform; // 48 bytes

        // Specular
        public UVMatrix3 SpecularUVTransform; // 48 bytes
        public UVMatrix3 SpecularColorUVTransform; // 48 bytes

        // Transmission
        public UVMatrix3 TransmissionUVTransform; // 48 bytes

        // Volume
        public UVMatrix3 ThicknessUVTransform; // 48 bytes

        // Iridescence
        public UVMatrix3 IridescenceUVTransform; // 48 bytes
        public UVMatrix3 IridescenceThicknessUVTransform; // 48 bytes

        // Diffuse Transmission
        public UVMatrix3 DiffuseTransmissionUVTransform; // 48 bytes
        public UVMatrix3 DiffuseTransmissionColorUVTransform; // 48 bytes

        // Anisotropy
        public UVMatrix3 AnisotropyUVTransform; // 48 bytes

        // Total: 21 * 48 = 1008 bytes
    }

    /// <summary>
    /// UV 变换矩阵（mat3 存储为 3 个 vec4，用于 std140 布局）
    /// </summary>
    /// <remarks>
    /// GLSL mat3 是列主序。Col0/Col1/Col2 对应 mat3 的三列。
    /// 使用 MaterialUboBuilder.BuildUVMatrix 从 Matrix3x2 构建正确的 UV 变换。
    /// </remarks>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct UVMatrix3 {
        public Vector4 Col0; // xyz = first column of mat3
        public Vector4 Col1; // xyz = second column of mat3
        public Vector4 Col2; // xyz = third column of mat3

        public static UVMatrix3 Identity => new(new Vector4(1f, 0f, 0f, 0f), new Vector4(0f, 1f, 0f, 0f), new Vector4(0f, 0f, 1f, 0f));

        public UVMatrix3(Vector4 col0, Vector4 col1, Vector4 col2) {
            Col0 = col0;
            Col1 = col1;
            Col2 = col2;
        }
    }

    #endregion

    #region Volume Scatter Data Structure

    /// <summary>
    /// Volume Scatter 数据 UBO（材质启用 Volume Scatter 时更新）
    /// Binding Point: 5
    /// std140 布局
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VolumeScatterData {
        public Vector4 MultiScatterColor; // 16 bytes, offset 0 (xyz used)
        public float MinRadius; // 4 bytes, offset 16
        public int MaterialID; // 4 bytes, offset 20
        public int FramebufferWidth; // 4 bytes, offset 24
        public int FramebufferHeight; // 4 bytes, offset 28
        // Total: 32 bytes

        // ScatterSamples 数组太大，保持使用独立 uniform
        // 因为 std140 布局下 55 个 vec3 需要 55 * 16 = 880 bytes
    }

    #endregion
}