namespace Game {
    /// <summary>
    /// Debug 渲染通道（值必须匹配 functions.glsl 中的定义）
    /// </summary>
    public enum DebugChannel {
        None = 0,
        UV0 = 1,
        UV1 = 2,
        NormalMap = 3,
        Normal = 4, // DEBUG_NORMAL_SHADING
        GeometricNormal = 5,
        Tangent = 6,
        TangentW = 7,
        Bitangent = 8,
        BaseColorAlpha = 9,
        Occlusion = 10,
        Emissive = 11,
        Metallic = 12,
        Roughness = 13,
        BaseColor = 14,
        Clearcoat = 15,
        ClearcoatRoughness = 16,
        ClearcoatNormal = 17,
        Sheen = 18,
        SheenRoughness = 19,
        SpecularFactor = 20,
        SpecularColor = 21,
        Transmission = 22,
        VolumeThickness = 23,
        IridescenceFactor = 24,
        IridescenceThickness = 25,
        AnisotropicStrength = 26,
        AnisotropicDirection = 27,
        DiffuseTransmissionFactor = 28,
        DiffuseTransmissionColorFactor = 29,
        VolumeScatterMultiScatterColor = 30,
        VolumeScatterSingleScatterColor = 31,
        IblRaw = 32,
        NdotV = 33,
        FresnelDielectric = 34,
        IblDiffuse = 35,
        IblSpecular = 36
    }
}