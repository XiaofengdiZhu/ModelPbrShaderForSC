using System;
using System.Collections.Generic;
using System.Numerics;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Shader = Engine.Graphics.Shader;
using Vector3 = Engine.Vector3;
using Matrix = Engine.Matrix;

namespace Game {
    partial class GltfPbrRenderer {
        static Matrix GetBoneTransformForEntry(PartRenderEntry entry) {
            ComponentModel cm = entry.ModelData.ComponentModel;
            int boneIndex = entry.Mesh.ParentBone?.Index ?? 0;
            if (boneIndex < cm.AbsoluteBoneTransformsForCamera.Length) {
                return cm.AbsoluteBoneTransformsForCamera[boneIndex];
            }
            return Matrix.Identity;
        }

        static Matrix4x4 GetWorldMatrixForEntry(PartRenderEntry entry) => GetBoneTransformForEntry(entry);

        ModelMaterial GetEffectiveMaterial(PartRenderEntry entry) {
            if (entry.Material != null) {
                return entry.Material;
            }
            if (entry.TextureOverride != null) {
                return entry.TextureOverride is RenderTarget2D ? DefaultDielectricMaskMaterial : DefaultDielectricMaterial;
            }
            return DefaultDielectricMaterial;
        }

        JointTexture GetOrCreateJointTexture(Model model) {
            if (model.Skin == null) {
                return null;
            }
            int jointCount = Math.Min(model.Skin.JointCount, SubsystemModelsRenderer.MaxJointsCount);
            if (!_jointTextures.TryGetValue(model, out JointTexture tex)
                || tex.MaxJointCount < jointCount) {
                tex?.Dispose();
                tex = new JointTexture(jointCount);
                _jointTextures[model] = tex;
            }
            return tex;
        }

        void SetPerFrameUniforms(Shader shader, SubsystemModelsRenderer.ModelData modelData) {
            int programHandle = shader.m_program;
            if (!_glymulLocationCache.TryGetValue(programHandle, out int glymulLoc)) {
                glymulLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_glymul");
                _glymulLocationCache[programHandle] = glymulLoc;
            }
            if (glymulLoc >= 0) {
                GLWrapper.GL.Uniform1(glymulLoc, Display.RenderTarget != null ? -1f : 1f);
            }
            if (!_terrainLightLocCache.TryGetValue(programHandle, out int terrainLightLoc)) {
                terrainLightLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_TerrainLight");
                _terrainLightLocCache[programHandle] = terrainLightLoc;
            }
            if (terrainLightLoc >= 0) {
                GLWrapper.GL.Uniform1(terrainLightLoc, modelData.Light);
            }
            if (!_celestialBodyVisibleLocCache.TryGetValue(programHandle, out int celestialBodyVisibleLoc)) {
                celestialBodyVisibleLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_CelestialBody");
                _celestialBodyVisibleLocCache[programHandle] = celestialBodyVisibleLoc;
            }
            if (celestialBodyVisibleLoc >= 0) {
                GLWrapper.GL.Uniform1(celestialBodyVisibleLoc, GetCelestialBodyVisible(modelData) ? 1f : 0f);
            }
            if (!_iblStrengthLocCache.TryGetValue(programHandle, out int iblStrengthLoc)) {
                iblStrengthLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_IblStrength");
                _iblStrengthLocCache[programHandle] = iblStrengthLoc;
            }
            if (iblStrengthLoc >= 0) {
                GLWrapper.GL.Uniform1(iblStrengthLoc, CalculateIblStrength(modelData));
            }
            UpdateLightsUBO(modelData.Light);
        }

        void SetPerFrameUniformsBatch(Shader shader) {
            int programHandle = shader.m_program;
            if (!_glymulLocationCache.TryGetValue(programHandle, out int glymulLoc)) {
                glymulLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_glymul");
                _glymulLocationCache[programHandle] = glymulLoc;
            }
            if (glymulLoc >= 0) {
                GLWrapper.GL.Uniform1(glymulLoc, Display.RenderTarget != null ? -1f : 1f);
            }
        }

        void BindTexturesForEntry(PartRenderEntry entry, ModelMaterial effectiveMaterial, Shader shader) {
            if (entry.TextureOverride != null) {
                MaterialTextureBinder.BindTexture2D(entry.TextureOverride, MaterialTextureSlot.BaseColor);
            }
            else {
                Model model = entry.ModelData.ComponentModel?.Model;
                if (model != null) {
                    BindMaterialTextures(model, effectiveMaterial, shader, null);
                }
            }
            MaterialTextureBinder.SetTextureSlotUniforms(shader);
        }

        void BindIBLTextures() {
            MaterialTextureBinder.BindIBLTextures(
                IblSampler.LambertianTexture,
                IblSampler.GGXTexture,
                IblSampler.SheenTexture,
                IblSampler.GGXLut,
                IblSampler.CharlieLut
            );
        }

        void UpdateMaterialUBOs(ModelMaterial material, bool useGeneratedTangents) {
            if (material == null) {
                return;
            }
            int extensionFlags = (int)MaterialUboBuilder.BuildExtensionFlags(material);
            if (LastMaterial != material
                || LastMaterialVersion != material.Version) {
                LastMaterialVersion = material.Version;
                MaterialCoreData coreData = MaterialUboBuilder.BuildMaterialCoreData(material, useGeneratedTangents);
                _materialCoreUBO.Update(ref coreData);
                MaterialExtensionData extData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                _materialExtUBO.Update(ref extData);
                LastMaterial = material;
                LastExtensionFlags = extensionFlags;
                UvTransformDirty = true;
            }
            else if (LastExtensionFlags != extensionFlags) {
                MaterialExtensionData extData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                _materialExtUBO.Update(ref extData);
                LastExtensionFlags = extensionFlags;
            }
        }

        void QueueShadowsAndDrawExtras() {
            SubsystemShadows shadows = _subsystemModelsRenderer.m_subsystemShadows;
            foreach (SubsystemModelsRenderer.ModelData md in _allModels) {
                bool isUnderwater = md.ComponentModel.RenderingMode == ModelRenderingMode.TransparentAfterWater;
                if (!isUnderwater
                    && md.ComponentBody != null
                    && md.ComponentModel.CastsShadow) {
                    Vector3 shadowPosition = md.ComponentBody.Position + new Vector3(0f, 0.02f, 0f);
                    BoundingBox boundingBox = md.ComponentBody.BoundingBox;
                    float shadowDiameter = 2.25f * (boundingBox.Max.X - boundingBox.Min.X);
                    shadows.QueueShadow(_camera, shadowPosition, shadowDiameter, md.ComponentModel.Opacity ?? 1f);
                }
                ModsManager.HookAction(
                    "OnModelRendererDrawExtra",
                    modLoader => {
                        modLoader.OnModelRendererDrawExtra(_subsystemModelsRenderer, md, _camera, null);
                        return false;
                    }
                );
                md.ComponentModel.DrawExtras(_camera);
            }
        }
    }
}