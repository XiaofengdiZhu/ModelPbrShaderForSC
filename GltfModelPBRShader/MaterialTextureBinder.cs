using System;
using System.Collections.Generic;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using Shader = Engine.Graphics.Shader;

namespace Game {
    public static class MaterialTextureBinder {
        static readonly Dictionary<int, SamplerState> _appliedSamplers = new();
        static readonly Dictionary<int, int[]> _slotUniformLocations = new();

        static readonly (MaterialTextureSlot slot, string texUniform, string samplerUniform)[] SlotUniforms = [
            (MaterialTextureSlot.BaseColor, "u_BaseColorTexture", "u_BaseColorSampler"),
            (MaterialTextureSlot.MetallicRoughness, "u_MetallicRoughnessTexture", "u_MetallicRoughnessSampler"),
            (MaterialTextureSlot.Normal, "u_NormalTexture", "u_NormalSampler"),
            (MaterialTextureSlot.Occlusion, "u_OcclusionTexture", "u_OcclusionSampler"),
            (MaterialTextureSlot.Emissive, "u_EmissiveTexture", "u_EmissiveSampler"),
            (MaterialTextureSlot.ClearCoat, "u_ClearcoatTexture", "u_ClearcoatSampler"),
            (MaterialTextureSlot.ClearCoatRoughness, "u_ClearcoatRoughnessTexture", "u_ClearcoatRoughnessSampler"),
            (MaterialTextureSlot.ClearCoatNormal, "u_ClearcoatNormalTexture", "u_ClearcoatNormalSampler"),
            (MaterialTextureSlot.Iridescence, "u_IridescenceTexture", "u_IridescenceSampler"),
            (MaterialTextureSlot.IridescenceThickness, "u_IridescenceThicknessTexture", "u_IridescenceThicknessSampler"),
            (MaterialTextureSlot.Transmission, "u_TransmissionTexture", "u_TransmissionSampler"),
            (MaterialTextureSlot.Thickness, "u_ThicknessTexture", "u_ThicknessSampler"),
            (MaterialTextureSlot.SheenColor, "u_SheenColorTexture", "u_SheenColorSampler"),
            (MaterialTextureSlot.SheenRoughness, "u_SheenRoughnessTexture", "u_SheenRoughnessSampler"),
            (MaterialTextureSlot.Specular, "u_SpecularTexture", "u_SpecularSampler"),
            (MaterialTextureSlot.SpecularColor, "u_SpecularColorTexture", "u_SpecularColorSampler"),
            (MaterialTextureSlot.Anisotropy, "u_AnisotropyTexture", "u_AnisotropySampler"),
            (MaterialTextureSlot.DiffuseTransmission, "u_DiffuseTransmissionTexture", "u_DiffuseTransmissionSampler"),
            (MaterialTextureSlot.DiffuseTransmissionColor, "u_DiffuseTransmissionColorTexture", "u_DiffuseTransmissionColorSampler"),
            (MaterialTextureSlot.Diffuse, "u_DiffuseTexture", "u_DiffuseSampler"),
            (MaterialTextureSlot.SpecularGlossiness, "u_SpecularGlossinessTexture", "u_SpecularGlossinessSampler"),
            (MaterialTextureSlot.IBLLambertian, "u_LambertianEnvTexture", "u_LambertianEnvSampler"),
            (MaterialTextureSlot.IBLGGX, "u_GGXEnvTexture", "u_GGXEnvSampler"),
            (MaterialTextureSlot.IBLCharlie, "u_CharlieEnvTexture", "u_CharlieEnvSampler"),
            (MaterialTextureSlot.IBLGGXLUT, "u_GGXLUT", "u_GGXLUTSampler"),
            (MaterialTextureSlot.IBLCharlieLUT, "u_CharlieLUT", "u_CharlieLUTSampler")
        ];

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

        public static void BindMaterialTextures(ModelMaterial material, Texture2D[] textures) {
            BindTexture(material.BaseColorTexture, textures, MaterialTextureSlot.BaseColor);
            BindTexture(material.MetallicRoughnessTexture, textures, MaterialTextureSlot.MetallicRoughness);
            BindTexture(material.NormalTexture, textures, MaterialTextureSlot.Normal);
            BindTexture(material.OcclusionTexture, textures, MaterialTextureSlot.Occlusion);
            BindTexture(material.EmissiveTexture, textures, MaterialTextureSlot.Emissive);

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

        static void BindTexture(ModelMaterialTexture matTex, Texture2D[] textures, MaterialTextureSlot slot) {
            if (matTex?.HasTexture != true) {
                return;
            }
            int index = matTex.TextureIndex;
            if (index < 0 || index >= textures.Length) {
                return;
            }
            Texture2D texture = textures[index];
            if (texture == null) {
                return;
            }
            BindTexture2D(texture.NativeHandle, slot);
            ApplySamplerState(texture);
        }

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

            if (_appliedSamplers.TryGetValue(handle, out SamplerState cached) && cached == sampler) {
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

        static void BindTexture2D(IntPtr textureHandle, MaterialTextureSlot slot) {
            TextureUnit unit = (TextureUnit)((int)TextureUnit.Texture0 + (int)slot);
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(TextureTarget.Texture2D, (int)textureHandle, true);
        }

        public static void ResetFrameState() {
            _appliedSamplers.Clear();
        }

        public static void ClearAllCaches() {
            _appliedSamplers.Clear();
            _slotUniformLocations.Clear();
        }

        public static void BindIBLTextures(CubemapTexture lambertianTexture, CubemapTexture ggxTexture, CubemapTexture charlieTexture, Texture2D ggxLut, Texture2D charlieLut) {
            BindCubemapTexture(lambertianTexture, MaterialTextureSlot.IBLLambertian);
            BindCubemapTexture(ggxTexture, MaterialTextureSlot.IBLGGX);
            BindCubemapTexture(charlieTexture, MaterialTextureSlot.IBLCharlie);
            BindTexture2D(ggxLut, MaterialTextureSlot.IBLGGXLUT);
            BindTexture2D(charlieLut, MaterialTextureSlot.IBLCharlieLUT);
        }

        static void BindCubemapTexture(CubemapTexture texture, MaterialTextureSlot slot) {
            if (texture == null) {
                return;
            }
            TextureUnit unit = (TextureUnit)((int)TextureUnit.Texture0 + (int)slot);
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(TextureTarget.TextureCubeMap, texture.m_texture, true);
        }
    }
}
