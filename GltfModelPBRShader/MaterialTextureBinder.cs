using System;
using System.Collections.Generic;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using Shader = Engine.Graphics.Shader;

namespace Game {
    /// <summary>
    /// 材质纹理绑定器
    /// 负责将材质纹理绑定到着色器的纹理单元
    /// 支持动态槽位：着色器没有的 uniform 会静默跳过
    /// </summary>
    /// <remarks>
    /// UV 变换传递方式因着色器设计而异（uniform vs UBO），
    /// 此类不处理 UV 变换，由使用者根据着色器设计自行处理。
    /// </remarks>
    public static class MaterialTextureBinder {
        // 缓存已应用的 sampler，避免重复 GL 调用
        static readonly Dictionary<int, SamplerState> _appliedSamplers = new();

        // 缓存纹理槽位 uniform location，避免每帧重复 GetUniformLocation
        static readonly Dictionary<int, int[]> _slotUniformLocations = new();

        /// <summary>
        /// 纹理槽位 uniform 映射
        /// (槽位, 纹理 uniform 名称, 采样器 uniform 名称)
        /// </summary>
        static readonly (MaterialTextureSlot slot, string texUniform, string samplerUniform)[] SlotUniforms = [
            // Core PBR textures
            (MaterialTextureSlot.BaseColor, "u_BaseColorTexture", "u_BaseColorSampler"),
            (MaterialTextureSlot.MetallicRoughness, "u_MetallicRoughnessTexture", "u_MetallicRoughnessSampler"),
            (MaterialTextureSlot.Normal, "u_NormalTexture", "u_NormalSampler"),
            (MaterialTextureSlot.Occlusion, "u_OcclusionTexture", "u_OcclusionSampler"),
            (MaterialTextureSlot.Emissive, "u_EmissiveTexture", "u_EmissiveSampler"),

            // ClearCoat
            (MaterialTextureSlot.ClearCoat, "u_ClearcoatTexture", "u_ClearcoatSampler"),
            (MaterialTextureSlot.ClearCoatRoughness, "u_ClearcoatRoughnessTexture", "u_ClearcoatRoughnessSampler"),
            (MaterialTextureSlot.ClearCoatNormal, "u_ClearcoatNormalTexture", "u_ClearcoatNormalSampler"),

            // Iridescence
            (MaterialTextureSlot.Iridescence, "u_IridescenceTexture", "u_IridescenceSampler"),
            (MaterialTextureSlot.IridescenceThickness, "u_IridescenceThicknessTexture", "u_IridescenceThicknessSampler"),

            // Transmission
            (MaterialTextureSlot.Transmission, "u_TransmissionTexture", "u_TransmissionSampler"),

            // Volume
            (MaterialTextureSlot.Thickness, "u_ThicknessTexture", "u_ThicknessSampler"),

            // Sheen
            (MaterialTextureSlot.SheenColor, "u_SheenColorTexture", "u_SheenColorSampler"),
            (MaterialTextureSlot.SheenRoughness, "u_SheenRoughnessTexture", "u_SheenRoughnessSampler"),

            // Specular
            (MaterialTextureSlot.Specular, "u_SpecularTexture", "u_SpecularSampler"),
            (MaterialTextureSlot.SpecularColor, "u_SpecularColorTexture", "u_SpecularColorSampler"),

            // Anisotropy
            (MaterialTextureSlot.Anisotropy, "u_AnisotropyTexture", "u_AnisotropySampler"),

            // Diffuse Transmission
            (MaterialTextureSlot.DiffuseTransmission, "u_DiffuseTransmissionTexture", "u_DiffuseTransmissionSampler"),
            (MaterialTextureSlot.DiffuseTransmissionColor, "u_DiffuseTransmissionColorTexture", "u_DiffuseTransmissionColorSampler"),

            // SpecularGlossiness workflow
            (MaterialTextureSlot.Diffuse, "u_DiffuseTexture", "u_DiffuseSampler"),
            (MaterialTextureSlot.SpecularGlossiness, "u_SpecularGlossinessTexture", "u_SpecularGlossinessSampler"),

            // IBL textures
            (MaterialTextureSlot.IBLLambertian, "u_LambertianEnvTexture", "u_LambertianEnvSampler"),
            (MaterialTextureSlot.IBLGGX, "u_GGXEnvTexture", "u_GGXEnvSampler"),
            (MaterialTextureSlot.IBLCharlie, "u_CharlieEnvTexture", "u_CharlieEnvSampler"),
            (MaterialTextureSlot.IBLGGXLUT, "u_GGXLUT", "u_GGXLUTSampler"),
            (MaterialTextureSlot.IBLCharlieLUT, "u_CharlieLUT", "u_CharlieLUTSampler")
        ];

        /// <summary>
        /// 设置纹理槽位 uniform（不存在的槽位静默跳过）
        /// uniform location 按 program handle 缓存，避免重复 GL 查询
        /// </summary>
        public static void SetTextureSlotUniforms(Shader shader) {
            int programHandle = shader.m_program;
            if (!_slotUniformLocations.TryGetValue(programHandle, out int[] locations)) {
                uint program = (uint)programHandle;
                locations = new int[SlotUniforms.Length * 2];
                for (int i = 0; i < SlotUniforms.Length; i++) {
                    locations[i * 2] = GLWrapper.GL.GetUniformLocation(program, SlotUniforms[i].texUniform);
                    locations[i * 2 + 1] = GLWrapper.GL.GetUniformLocation(program, SlotUniforms[i].samplerUniform);
                }
                _slotUniformLocations[programHandle] = locations;
            }
            for (int i = 0; i < SlotUniforms.Length; i++) {
                int slotValue = (int)SlotUniforms[i].slot;
                int texLoc = locations[i * 2];
                if (texLoc >= 0) {
                    GLWrapper.GL.Uniform1(texLoc, slotValue);
                }
                int samplerLoc = locations[i * 2 + 1];
                if (samplerLoc >= 0) {
                    GLWrapper.GL.Uniform1(samplerLoc, slotValue);
                }
            }
        }

        /// <summary>
        /// 绑定材质纹理到 GPU
        /// </summary>
        /// <param name="material">材质数据</param>
        /// <param name="textures">纹理数组（来自 ModelData.Textures 或 Model.Textures）</param>
        public static void BindMaterialTextures(ModelMaterial material, Texture2D[] textures) {
            // Core PBR textures
            BindTexture(material.BaseColorTexture, textures, MaterialTextureSlot.BaseColor);
            BindTexture(material.MetallicRoughnessTexture, textures, MaterialTextureSlot.MetallicRoughness);
            BindTexture(material.NormalTexture, textures, MaterialTextureSlot.Normal);
            BindTexture(material.OcclusionTexture, textures, MaterialTextureSlot.Occlusion);
            BindTexture(material.EmissiveTexture, textures, MaterialTextureSlot.Emissive);

            // Extension textures
            if (material.ClearCoat?.IsEnabled == true) {
                BindTexture(material.ClearCoat.Texture, textures, MaterialTextureSlot.ClearCoat);
                BindTexture(material.ClearCoat.RoughnessTexture, textures, MaterialTextureSlot.ClearCoatRoughness);
                BindTexture(material.ClearCoat.NormalTexture, textures, MaterialTextureSlot.ClearCoatNormal);
            }
            if (material.Iridescence?.IsEnabled == true) {
                BindTexture(material.Iridescence.Texture, textures, MaterialTextureSlot.Iridescence);
                BindTexture(material.Iridescence.ThicknessTexture, textures, MaterialTextureSlot.IridescenceThickness);
            }
            if (material.Transmission?.IsEnabled == true) {
                BindTexture(material.Transmission.Texture, textures, MaterialTextureSlot.Transmission);
            }
            if (material.Volume?.IsEnabled == true) {
                BindTexture(material.Volume.ThicknessTexture, textures, MaterialTextureSlot.Thickness);
            }
            if (material.Sheen?.IsEnabled == true) {
                BindTexture(material.Sheen.ColorTexture, textures, MaterialTextureSlot.SheenColor);
                BindTexture(material.Sheen.RoughnessTexture, textures, MaterialTextureSlot.SheenRoughness);
            }
            if (material.Specular?.IsEnabled == true) {
                BindTexture(material.Specular.SpecularTexture, textures, MaterialTextureSlot.Specular);
                BindTexture(material.Specular.SpecularColorTexture, textures, MaterialTextureSlot.SpecularColor);
            }
            if (material.Anisotropy?.IsEnabled == true) {
                BindTexture(material.Anisotropy.AnisotropyTexture, textures, MaterialTextureSlot.Anisotropy);
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                BindTexture(material.DiffuseTransmission.Texture, textures, MaterialTextureSlot.DiffuseTransmission);
                BindTexture(material.DiffuseTransmission.ColorTexture, textures, MaterialTextureSlot.DiffuseTransmissionColor);
            }
            if (material.SpecularGlossiness?.IsEnabled == true) {
                BindTexture(material.SpecularGlossiness.DiffuseTexture, textures, MaterialTextureSlot.Diffuse);
                BindTexture(material.SpecularGlossiness.SpecularGlossinessTexture, textures, MaterialTextureSlot.SpecularGlossiness);
            }
        }

        /// <summary>
        /// 绑定单个纹理到指定槽位
        /// </summary>
        static void BindTexture(ModelMaterialTexture matTex, Texture2D[] textures, MaterialTextureSlot slot) {
            if (matTex?.HasTexture != true) {
                return;
            }
            int index = matTex.TextureIndex;
            if (index < 0
                || index >= textures.Length) {
                return;
            }
            Texture2D texture = textures[index];
            if (texture == null) {
                return;
            }

            // 先绑定纹理到指定纹理单元，再设 sampler 参数
            BindTexture2D(texture.NativeHandle, slot);
            ApplySamplerState(texture);
        }

        /// <summary>
        /// 绑定 Texture2D 到指定槽位，应用 SamplerState 采样参数
        /// </summary>
        public static void BindTexture2D(Texture2D texture, MaterialTextureSlot slot) {
            if (texture == null) {
                return;
            }
            BindTexture2D(texture.NativeHandle, slot);
            ApplySamplerState(texture);
        }

        static void ApplySamplerState(Texture2D texture) {
            int handle = (int)texture.NativeHandle;
            SamplerState sampler = texture.SamplerState;

            // 跳过已应用相同 sampler 的纹理
            if (_appliedSamplers.TryGetValue(handle, out SamplerState cached)
                && cached == sampler) {
                return;
            }
            _appliedSamplers[handle] = sampler;
            bool hasMipmap = texture.MipLevelsCount > 1;
            TextureMinFilter minFilter;
            TextureMagFilter magFilter;
            TextureWrapMode wrapS, wrapT;
            if (sampler != null) {
                minFilter = GLWrapper.TranslateTextureFilterModeMin(sampler.FilterMode, hasMipmap);
                magFilter = GLWrapper.TranslateTextureFilterModeMag(sampler.FilterMode);
                wrapS = GLWrapper.TranslateTextureAddressMode(sampler.AddressModeU);
                wrapT = GLWrapper.TranslateTextureAddressMode(sampler.AddressModeV);
            }
            else {
                minFilter = hasMipmap ? TextureMinFilter.LinearMipmapLinear : TextureMinFilter.Nearest;
                magFilter = hasMipmap ? TextureMagFilter.Linear : TextureMagFilter.Nearest;
                wrapS = TextureWrapMode.Repeat;
                wrapT = TextureWrapMode.Repeat;
            }
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)minFilter);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)magFilter);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)wrapS);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)wrapT);
        }

        /// <summary>
        /// 绑定纹理句柄到指定槽位（Texture2D 类型）
        /// </summary>
        static void BindTexture2D(IntPtr textureHandle, MaterialTextureSlot slot) {
            TextureUnit unit = (TextureUnit)((int)TextureUnit.Texture0 + (int)slot);
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(TextureTarget.Texture2D, (int)textureHandle, true);
        }

        /// <summary>
        /// 重置帧级缓存（每帧 BeginFrame 调用）
        /// 防止 texture handle 复用导致 stale sampler state
        /// </summary>
        public static void ResetFrameState() {
            _appliedSamplers.Clear();
        }

        /// <summary>
        /// 绑定 IBL 纹理（用于 PBR 渲染）
        /// </summary>
        public static void BindIBLTextures(uint lambertianTexture, uint ggxTexture, uint charlieTexture, uint ggxLut, uint charlieLut) {
            BindTextureHandle(lambertianTexture, TextureTarget.TextureCubeMap, MaterialTextureSlot.IBLLambertian);
            BindTextureHandle(ggxTexture, TextureTarget.TextureCubeMap, MaterialTextureSlot.IBLGGX);
            BindTextureHandle(charlieTexture, TextureTarget.TextureCubeMap, MaterialTextureSlot.IBLCharlie);
            BindTextureHandle(ggxLut, TextureTarget.Texture2D, MaterialTextureSlot.IBLGGXLUT);
            BindTextureHandle(charlieLut, TextureTarget.Texture2D, MaterialTextureSlot.IBLCharlieLUT);
        }

        /// <summary>
        /// 绑定纹理句柄到指定槽位
        /// </summary>
        static void BindTextureHandle(uint textureHandle, TextureTarget target, MaterialTextureSlot slot) {
            if (textureHandle == 0) {
                return;
            }
            TextureUnit unit = (TextureUnit)((int)TextureUnit.Texture0 + (int)slot);
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(target, (int)textureHandle, true);
        }
    }
}