using System;
using System.Numerics;
using Engine.Media;

namespace Game {
    /// <summary>
    /// 材质 UBO 数据构建器
    /// 从 ModelMaterial 构建 UBO 数据结构
    /// </summary>
    public static class MaterialUboBuilder {
        /// <summary>
        /// 构建材质核心数据用于 UBO 更新
        /// </summary>
        public static MaterialCoreData BuildMaterialCoreData(ModelMaterial material, bool useGeneratedTangents) {
            bool useSG = material?.SpecularGlossiness?.IsEnabled == true;
            return new MaterialCoreData {
                // PBR Core
                BaseColorFactor = useSG ? material.SpecularGlossiness.DiffuseFactor : material?.BaseColorFactor ?? Vector4.One,
                EmissiveFactor = new Vector4(material?.EmissiveFactor ?? Vector3.Zero, 0f),
                MetallicFactor = useSG ? 0f : material?.MetallicFactor ?? MaterialDefaults.MetallicFactor,
                RoughnessFactor = useSG ? 1f - material.SpecularGlossiness.GlossinessFactor : material?.RoughnessFactor ?? MaterialDefaults.RoughnessFactor,
                NormalScale = material?.NormalScale ?? MaterialDefaults.NormalScale,
                OcclusionStrength = material?.OcclusionStrength ?? MaterialDefaults.OcclusionStrength,

                // Alpha
                AlphaMode = (int)(material?.AlphaMode ?? ModelAlphaMode.Opaque),
                AlphaCutoff = material?.AlphaCutoff ?? MaterialDefaults.AlphaCutoff,
                UseGeneratedTangents = useGeneratedTangents ? 1 : 0,
                CorePadding0 = 0f,

                // UV Sets (Core)
                BaseColorUVSet = material?.BaseColorTexture?.UVIndex ?? 0,
                MetallicRoughnessUVSet = material?.MetallicRoughnessTexture?.UVIndex ?? 0,
                NormalUVSet = material?.NormalTexture?.UVIndex ?? 0,
                OcclusionUVSet = material?.OcclusionTexture?.UVIndex ?? 0,
                EmissiveUVSet = material?.EmissiveTexture?.UVIndex ?? 0,
                DiffuseUVSet = material?.SpecularGlossiness?.DiffuseTexture?.UVIndex ?? 0,
                SpecularGlossinessUVSet = material?.SpecularGlossiness?.SpecularGlossinessTexture?.UVIndex ?? 0,
                CoreUVPadding0 = 0,

                // Flags
                ExtensionFlags = (int)BuildExtensionFlags(material),
                TextureFlags = (int)BuildTextureFlags(material),
                FlagsPadding0 = 0,
                FlagsPadding1 = 0
            };
        }

        /// <summary>
        /// 构建材质扩展数据用于 UBO 更新
        /// </summary>
        public static MaterialExtensionData BuildMaterialExtensionData(ModelMaterial material) {
            bool useSG = material?.SpecularGlossiness?.IsEnabled == true;
            return new MaterialExtensionData {
                // IOR
                Ior = material?.Ior?.IsEnabled == true ? material.Ior.Ior : MaterialDefaults.Ior,
                IorPadding0 = 0f,
                IorPadding1 = 0f,
                IorPadding2 = 0f,

                // Emissive Strength
                EmissiveStrength = material?.EmissiveStrength?.IsEnabled == true
                    ? material.EmissiveStrength.EmissiveStrength
                    : MaterialDefaults.EmissiveStrength,
                EmissiveStrengthPadding0 = 0f,
                EmissiveStrengthPadding1 = 0f,
                EmissiveStrengthPadding2 = 0f,

                // Specular
                SpecularFactor = useSG ? 1f :
                    material?.Specular?.IsEnabled == true ? material.Specular.SpecularFactor : MaterialDefaults.SpecularFactor,
                SpecularPadding0 = 0f,
                SpecularPadding1 = 0f,
                SpecularPadding2 = 0f,
                SpecularColorFactor = useSG ? new Vector4(material.SpecularGlossiness.SpecularFactor, 1f) :
                    material?.Specular?.IsEnabled == true ? new Vector4(material.Specular.SpecularColorFactor, 1f) : Vector4.One,

                // Sheen
                SheenColorFactor = material?.Sheen?.IsEnabled == true ? new Vector4(material.Sheen.ColorFactor, 1f) : Vector4.Zero,
                SheenRoughnessFactor = material?.Sheen?.IsEnabled == true ? material.Sheen.RoughnessFactor : MaterialDefaults.SheenRoughnessFactor,
                SheenPadding0 = 0f,
                SheenPadding1 = 0f,
                SheenPadding2 = 0f,

                // ClearCoat
                ClearCoatFactor = material?.ClearCoat?.IsEnabled == true ? material.ClearCoat.Factor : 0f,
                ClearCoatRoughness = material?.ClearCoat?.IsEnabled == true ? material.ClearCoat.RoughnessFactor : 0f,
                ClearCoatNormalScale = material?.ClearCoat?.IsEnabled == true ? material.ClearCoat.NormalScale : 1f,
                ClearCoatPadding0 = 0f,

                // Transmission
                TransmissionFactor = material?.Transmission?.IsEnabled == true ? material.Transmission.Factor : 0f,
                TransmissionPadding0 = 0f,
                TransmissionPadding1 = 0f,
                TransmissionPadding2 = 0f,

                // Volume
                ThicknessFactor = material?.Volume?.IsEnabled == true ? material.Volume.ThicknessFactor : 0f,
                AttenuationDistance = material?.Volume?.IsEnabled == true ? material.Volume.AttenuationDistance : 0f,
                VolumePadding0 = 0f,
                VolumePadding1 = 0f,
                AttenuationColor = material?.Volume?.IsEnabled == true ? new Vector4(material.Volume.AttenuationColor, 1f) : Vector4.One,

                // Iridescence
                IridescenceFactor = material?.Iridescence?.IsEnabled == true ? material.Iridescence.Factor : 0f,
                IridescenceIor = material?.Iridescence?.IsEnabled == true ? material.Iridescence.IOR : 1.3f,
                IridescenceThicknessMin = material?.Iridescence?.IsEnabled == true ? material.Iridescence.ThicknessMinimum : 100f,
                IridescenceThicknessMax = material?.Iridescence?.IsEnabled == true ? material.Iridescence.ThicknessMaximum : 400f,

                // Dispersion
                Dispersion = material?.Dispersion?.IsEnabled == true ? material.Dispersion.Dispersion : MaterialDefaults.Dispersion,
                DispersionPadding0 = 0f,
                DispersionPadding1 = 0f,
                DispersionPadding2 = 0f,

                // Diffuse Transmission
                DiffuseTransmissionFactor = material?.DiffuseTransmission?.IsEnabled == true
                    ? material.DiffuseTransmission.Factor
                    : MaterialDefaults.DiffuseTransmissionFactor,
                DiffuseTransmissionPadding0 = 0f,
                DiffuseTransmissionPadding1 = 0f,
                DiffuseTransmissionPadding2 = 0f,
                DiffuseTransmissionColorFactor = material?.DiffuseTransmission?.IsEnabled == true
                    ? new Vector4(material.DiffuseTransmission.ColorFactor, 1f)
                    : Vector4.One,

                // Anisotropy
                Anisotropy = material?.Anisotropy?.IsEnabled == true
                    ? new Vector4(
                        MathF.Cos(material.Anisotropy.AnisotropyRotation),
                        MathF.Sin(material.Anisotropy.AnisotropyRotation),
                        material.Anisotropy.AnisotropyStrength,
                        0f
                    )
                    : Vector4.Zero,

                // Extension UV Sets
                ClearCoatUVSet = material?.ClearCoat?.Texture?.UVIndex ?? 0,
                ClearCoatRoughnessUVSet = material?.ClearCoat?.RoughnessTexture?.UVIndex ?? 0,
                ClearCoatNormalUVSet = material?.ClearCoat?.NormalTexture?.UVIndex ?? 0,
                IridescenceUVSet = material?.Iridescence?.Texture?.UVIndex ?? 0,
                IridescenceThicknessUVSet = material?.Iridescence?.ThicknessTexture?.UVIndex ?? 0,
                SheenColorUVSet = material?.Sheen?.ColorTexture?.UVIndex ?? 0,
                SheenRoughnessUVSet = material?.Sheen?.RoughnessTexture?.UVIndex ?? 0,
                SpecularUVSet = material?.Specular?.SpecularTexture?.UVIndex ?? 0,
                SpecularColorUVSet = material?.Specular?.SpecularColorTexture?.UVIndex ?? 0,
                TransmissionUVSet = material?.Transmission?.Texture?.UVIndex ?? 0,
                ThicknessUVSet = material?.Volume?.ThicknessTexture?.UVIndex ?? 0,
                DiffuseTransmissionUVSet = material?.DiffuseTransmission?.Texture?.UVIndex ?? 0,
                DiffuseTransmissionColorUVSet = material?.DiffuseTransmission?.ColorTexture?.UVIndex ?? 0,
                AnisotropyUVSet = material?.Anisotropy?.AnisotropyTexture?.UVIndex ?? 0,
                UVSetPadding0 = 0,
                UVSetPadding1 = 0,

                // SpecularGlossiness
                SpecularFactorSG = useSG ? new Vector4(material.SpecularGlossiness.SpecularFactor, 1f) : Vector4.One,
                GlossinessFactor = useSG ? material.SpecularGlossiness.GlossinessFactor : 1f,
                SGPad0 = 0f,
                SGPad1 = 0f,
                SGPad2 = 0f
            };
        }

        /// <summary>
        /// 检查材质是否有启用任何扩展
        /// </summary>
        public static bool HasExtensions(ModelMaterial material) {
            if (material == null) {
                return false;
            }
            return material.Ior?.IsEnabled == true
                || material.EmissiveStrength?.IsEnabled == true
                || material.Specular?.IsEnabled == true
                || material.Sheen?.IsEnabled == true
                || material.ClearCoat?.IsEnabled == true
                || material.Transmission?.IsEnabled == true
                || material.Volume?.IsEnabled == true
                || material.Iridescence?.IsEnabled == true
                || material.Dispersion?.IsEnabled == true
                || material.DiffuseTransmission?.IsEnabled == true
                || material.Anisotropy?.IsEnabled == true
                || material.SpecularGlossiness?.IsEnabled == true;
        }

        /// <summary>
        /// 构建扩展标志位
        /// </summary>
        public static ExtensionFlags BuildExtensionFlags(ModelMaterial material) {
            if (material == null) {
                return ExtensionFlags.MetallicRoughness;
            }
            ExtensionFlags flags = ExtensionFlags.MetallicRoughness;
            if (material.ClearCoat?.IsEnabled == true) flags |= ExtensionFlags.ClearCoat;
            if (material.Iridescence?.IsEnabled == true) flags |= ExtensionFlags.Iridescence;
            if (material.Transmission?.IsEnabled == true) flags |= ExtensionFlags.Transmission;
            if (material.Volume?.IsEnabled == true) flags |= ExtensionFlags.Volume;
            if (material.Sheen?.IsEnabled == true) flags |= ExtensionFlags.Sheen;
            if (material.Specular?.IsEnabled == true) flags |= ExtensionFlags.Specular;
            if (material.Ior?.IsEnabled == true) flags |= ExtensionFlags.Ior;
            if (material.EmissiveStrength?.IsEnabled == true) flags |= ExtensionFlags.EmissiveStrength;
            if (material.Dispersion?.IsEnabled == true) flags |= ExtensionFlags.Dispersion;
            if (material.Anisotropy?.IsEnabled == true) flags |= ExtensionFlags.Anisotropy;
            if (material.DiffuseTransmission?.IsEnabled == true) flags |= ExtensionFlags.DiffuseTransmission;
            if (material.VolumeScatter?.IsEnabled == true) flags |= ExtensionFlags.VolumeScatter;
            if (material.Unlit?.IsEnabled == true) flags |= ExtensionFlags.Unlit;
            if (material.SpecularGlossiness?.IsEnabled == true) flags |= ExtensionFlags.SpecularGlossiness;
            return flags;
        }

        /// <summary>
        /// 构建纹理标志位
        /// </summary>
        public static TextureFlags BuildTextureFlags(ModelMaterial material) {
            if (material == null) {
                return TextureFlags.None;
            }
            TextureFlags flags = TextureFlags.None;
            if (material.BaseColorTexture != null) flags |= TextureFlags.BaseColor;
            if (material.MetallicRoughnessTexture != null) flags |= TextureFlags.MetallicRoughness;
            if (material.NormalTexture != null) flags |= TextureFlags.Normal;
            if (material.OcclusionTexture != null) flags |= TextureFlags.Occlusion;
            if (material.EmissiveTexture != null) flags |= TextureFlags.Emissive;

            if (material.ClearCoat?.IsEnabled == true) {
                if (material.ClearCoat.Texture != null) flags |= TextureFlags.ClearCoat;
                if (material.ClearCoat.RoughnessTexture != null) flags |= TextureFlags.ClearCoatRoughness;
                if (material.ClearCoat.NormalTexture != null) flags |= TextureFlags.ClearCoatNormal;
            }
            if (material.Iridescence?.IsEnabled == true) {
                if (material.Iridescence.Texture != null) flags |= TextureFlags.Iridescence;
                if (material.Iridescence.ThicknessTexture != null) flags |= TextureFlags.IridescenceThickness;
            }
            if (material.Transmission?.IsEnabled == true && material.Transmission.Texture != null) {
                flags |= TextureFlags.Transmission;
            }
            if (material.Volume?.IsEnabled == true && material.Volume.ThicknessTexture != null) {
                flags |= TextureFlags.Thickness;
            }
            if (material.Sheen?.IsEnabled == true) {
                if (material.Sheen.ColorTexture != null) flags |= TextureFlags.SheenColor;
                if (material.Sheen.RoughnessTexture != null) flags |= TextureFlags.SheenRoughness;
            }
            if (material.Specular?.IsEnabled == true) {
                if (material.Specular.SpecularTexture != null) flags |= TextureFlags.Specular;
                if (material.Specular.SpecularColorTexture != null) flags |= TextureFlags.SpecularColor;
            }
            if (material.Anisotropy?.IsEnabled == true && material.Anisotropy.AnisotropyTexture != null) {
                flags |= TextureFlags.Anisotropy;
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                if (material.DiffuseTransmission.Texture != null) flags |= TextureFlags.DiffuseTransmission;
                if (material.DiffuseTransmission.ColorTexture != null) flags |= TextureFlags.DiffuseTransmissionColor;
            }
            return flags;
        }

        /// <summary>
        /// 构建 UV 变换数据
        /// </summary>
        public static UVTransformData BuildUVTransformData(ModelMaterial material) {
            UVTransformData data = new();
            if (material == null) {
                return data;
            }

            // Core textures
            data.NormalUVTransform = BuildUVMatrix(material.NormalTexture);
            data.EmissiveUVTransform = BuildUVMatrix(material.EmissiveTexture);
            data.OcclusionUVTransform = BuildUVMatrix(material.OcclusionTexture);
            data.BaseColorUVTransform = BuildUVMatrix(material.BaseColorTexture);
            data.MetallicRoughnessUVTransform = BuildUVMatrix(material.MetallicRoughnessTexture);

            // SpecularGlossiness
            if (material.SpecularGlossiness?.IsEnabled == true) {
                data.DiffuseUVTransform = BuildUVMatrix(material.SpecularGlossiness.DiffuseTexture);
                data.SpecularGlossinessUVTransform = BuildUVMatrix(material.SpecularGlossiness.SpecularGlossinessTexture);
            }

            // ClearCoat
            if (material.ClearCoat?.IsEnabled == true) {
                data.ClearcoatUVTransform = BuildUVMatrix(material.ClearCoat.Texture);
                data.ClearcoatRoughnessUVTransform = BuildUVMatrix(material.ClearCoat.RoughnessTexture);
                data.ClearcoatNormalUVTransform = BuildUVMatrix(material.ClearCoat.NormalTexture);
            }

            // Sheen
            if (material.Sheen?.IsEnabled == true) {
                data.SheenColorUVTransform = BuildUVMatrix(material.Sheen.ColorTexture);
                data.SheenRoughnessUVTransform = BuildUVMatrix(material.Sheen.RoughnessTexture);
            }

            // Specular
            if (material.Specular?.IsEnabled == true) {
                data.SpecularUVTransform = BuildUVMatrix(material.Specular.SpecularTexture);
                data.SpecularColorUVTransform = BuildUVMatrix(material.Specular.SpecularColorTexture);
            }

            // Transmission
            if (material.Transmission?.IsEnabled == true) {
                data.TransmissionUVTransform = BuildUVMatrix(material.Transmission.Texture);
            }

            // Volume
            if (material.Volume?.IsEnabled == true) {
                data.ThicknessUVTransform = BuildUVMatrix(material.Volume.ThicknessTexture);
            }

            // Iridescence
            if (material.Iridescence?.IsEnabled == true) {
                data.IridescenceUVTransform = BuildUVMatrix(material.Iridescence.Texture);
                data.IridescenceThicknessUVTransform = BuildUVMatrix(material.Iridescence.ThicknessTexture);
            }

            // Diffuse Transmission
            if (material.DiffuseTransmission?.IsEnabled == true) {
                data.DiffuseTransmissionUVTransform = BuildUVMatrix(material.DiffuseTransmission.Texture);
                data.DiffuseTransmissionColorUVTransform = BuildUVMatrix(material.DiffuseTransmission.ColorTexture);
            }

            // Anisotropy
            if (material.Anisotropy?.IsEnabled == true) {
                data.AnisotropyUVTransform = BuildUVMatrix(material.Anisotropy.AnisotropyTexture);
            }

            return data;
        }

        static UVMatrix3 BuildUVMatrix(ModelMaterialTexture texture) {
            if (texture?.HasUVTransform != true) {
                return UVMatrix3.Identity;
            }
            // 从 Matrix3x2 转换为 UVMatrix3
            Matrix3x2 m = texture.UVTransform;
            return new UVMatrix3(
                new Vector4(m.M11, m.M12, 0f, 0f),
                new Vector4(m.M21, m.M22, 0f, 0f),
                new Vector4(m.M31, m.M32, 1f, 0f)
            );
        }
    }
}
