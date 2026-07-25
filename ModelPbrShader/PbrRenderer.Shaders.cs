using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Shader = Engine.Graphics.Shader;

namespace Game {
    partial class PbrRenderer {
        #region Shader Compilation

        static readonly int WidgetShaderHashSalt = "__WIDGET__".GetHashCode();
        static readonly int WidgetAlphaMaskHashSalt = "__WIDGET_ALPHA_MASK__".GetHashCode();
        static readonly int WidgetTextureOverrideHashSalt = "__WIDGET_TEXTURE_OVERRIDE__".GetHashCode();

        protected override Shader CreateShaderVariant(ModelMesh mesh, ModelMaterial material, RenderContext context) =>
            CreateShaderVariantInternal(mesh, material, context, false);

        protected override Shader GetOrCreateShader(ModelMesh mesh, ModelMaterial material, RenderContext context) {
            int materialHash = ComputeMaterialHash(material) * 31 + ComputeMorphHash(mesh);
            int contextHash = AdjustContextHashForMaterial(CachedContextHash, material, context);
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) {
                return shader;
            }
            return CreateShaderVariant(mesh, material, context);
        }

        Shader GetOrCreateWidgetShader(ModelMesh mesh, ModelMaterial material, RenderContext context, bool forceAlphaMask, bool hasTextureOverride) {
            int materialHash = ComputeMaterialHash(material) * 31 + ComputeMorphHash(mesh);
            materialHash = materialHash * 31 + WidgetShaderHashSalt;
            materialHash = materialHash * 31 + ComputeWidgetVertexLayoutHash(mesh);
            if (forceAlphaMask) {
                materialHash = materialHash * 31 + WidgetAlphaMaskHashSalt;
            }
            if (hasTextureOverride) {
                materialHash = materialHash * 31 + WidgetTextureOverrideHashSalt;
            }
            int contextHash = AdjustContextHashForMaterial(CachedContextHash, material, context);
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) {
                return shader;
            }
            return CreateShaderVariantInternal(
                mesh,
                material,
                context,
                false,
                hasTextureOverride,
                forceAlphaMask ? ModelAlphaMode.Mask : null
            );
        }

        Shader GetOrCreateInstancedShader(ModelMesh mesh, ModelMaterial material, RenderContext context, bool hasTextureOverride = false) {
            int materialHash = ComputeMaterialHash(material) * 31 + InstancedHashSalt;
            if (hasTextureOverride) {
                materialHash = materialHash * 31 + "__TEX_OVERRIDE__".GetHashCode();
            }
            int contextHash = AdjustContextHashForMaterial(CachedContextHash, material, context);
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) {
                return shader;
            }
            return CreateShaderVariantInternal(mesh, material, context, true, hasTextureOverride);
        }

        Shader CreateShaderVariantInternal(ModelMesh mesh,
            ModelMaterial material,
            RenderContext context,
            bool isInstanced,
            bool hasTextureOverride = false,
            ModelAlphaMode? alphaModeOverride = null) {
            ShaderDefines defines = new();
            AddVertexAttributeDefines(defines, mesh);
            if (isInstanced) {
                defines.Add("USE_INSTANCING");
            }
            if (material != null) {
                AddMaterialDefines(defines, material);
            }
            if (hasTextureOverride) {
                defines.Add("HAS_BASE_COLOR_MAP");
            }
            bool useIBL = context.UseIBL || material?.DiffuseTransmission?.IsEnabled == true;
            if (useIBL) {
                defines.Add("USE_IBL");
            }
            if (context.HasPunctualLight
                && material?.Unlit?.IsEnabled != true) {
                defines.Add("USE_PUNCTUAL");
            }
            if (context.UseLinearOutput) {
                defines.Add("LINEAR_OUTPUT");
            }
            else {
                AddToneMapDefine(defines, context.ToneMapMode);
            }
            if (context.DebugChannel != DebugChannel.None) {
                defines.AddRaw($"DEBUG {(int)context.DebugChannel}");
            }
            if (!isInstanced
                && HasSkinningData(mesh)) {
                defines.Add("USE_SKINNING");
            }
            if (context.EnableMorphing
                && HasMorphTargetData(mesh)) {
                AddMorphTargetDefines(defines, mesh);
            }
            ModelAlphaMode alphaMode = alphaModeOverride ?? material?.AlphaMode ?? ModelAlphaMode.Opaque;
            defines.AddRaw($"ALPHAMODE {(int)alphaMode}");
            string fragShader = context.IsScatterPass ? "scatter.frag" :
                material?.SpecularGlossiness?.IsEnabled == true ? "specular_glossiness.frag" : "pbr.frag";
            try {
                int vertHash = ShaderCache.SelectShader("primitive.vert", defines);
                int fragHash = ShaderCache.SelectShader(fragShader, defines);
                return ShaderCache.GetShaderProgram(vertHash, fragHash);
            }
            catch (Exception ex) {
                Log.Error($"PbrRenderer: shader compile failed: {ex.Message}");
                return null;
            }
        }

        static void AddVertexAttributeDefines(ShaderDefines defines, ModelMesh mesh) {
            if (mesh == null) {
                return;
            }
            HashSet<string> semantics = new();
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.VertexBuffer?.VertexDeclaration == null) {
                    continue;
                }
                foreach (VertexElement element in part.VertexBuffer.VertexDeclaration.VertexElements) {
                    semantics.Add(element.Semantic);
                }
            }
            if (semantics.Contains("NORMAL")) {
                defines.Add("HAS_NORMAL_VEC3");
            }
            if (semantics.Contains("TANGENT")) {
                defines.Add("HAS_TANGENT_VEC4");
            }
            if (semantics.Contains("TEXCOORD")
                || semantics.Contains("TEXCOORD0")) {
                defines.Add("HAS_TEXCOORD_0_VEC2");
            }
            if (semantics.Contains("TEXCOORD1")) {
                defines.Add("HAS_TEXCOORD_1_VEC2");
            }
            if (semantics.Contains("COLOR")) {
                defines.Add("HAS_COLOR_0_VEC4");
            }
            if (semantics.Contains("BLENDINDICES")) {
                defines.Add("HAS_JOINTS_0_VEC4");
            }
            if (semantics.Contains("BLENDWEIGHTS")) {
                defines.Add("HAS_WEIGHTS_0_VEC4");
            }
        }

        static bool HasSkinningData(ModelMesh mesh) {
            if (mesh == null) {
                return false;
            }
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.VertexBuffer?.VertexDeclaration != null) {
                    foreach (VertexElement element in part.VertexBuffer.VertexDeclaration.VertexElements) {
                        if (element.Semantic == "BLENDINDICES"
                            || element.Semantic == "BLENDWEIGHTS") {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        static bool HasMorphTargetData(ModelMesh mesh) {
            if (mesh == null) {
                return false;
            }
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.HasMorphTargets == true) {
                    return true;
                }
            }
            return false;
        }

        static int ComputeMorphHash(ModelMesh mesh) {
            if (!HasMorphTargetData(mesh)) {
                return 0;
            }
            unchecked {
                int hash = "__MORPH__".GetHashCode();
                foreach (ModelMeshPart part in mesh.MeshParts) {
                    if (part?.HasMorphTargets != true) {
                        continue;
                    }
                    hash = hash * 31 + part.MorphTargetCount;
                    hash = hash * 31 + (part.HasMorphTargetPosition ? 1 : 0);
                    hash = hash * 31 + (part.HasMorphTargetNormal ? 1 : 0);
                    hash = hash * 31 + (part.HasMorphTargetTangent ? 1 : 0);
                    hash = hash * 31 + (part.HasMorphTargetTexCoord0 ? 1 : 0);
                    hash = hash * 31 + (part.HasMorphTargetTexCoord1 ? 1 : 0);
                    hash = hash * 31 + (part.HasMorphTargetColor0 ? 1 : 0);
                    hash = hash * 31 + part.MorphTargetPositionOffset;
                    hash = hash * 31 + part.MorphTargetNormalOffset;
                    hash = hash * 31 + part.MorphTargetTangentOffset;
                    hash = hash * 31 + part.MorphTargetTexCoord0Offset;
                    hash = hash * 31 + part.MorphTargetTexCoord1Offset;
                    hash = hash * 31 + part.MorphTargetColor0Offset;
                    break;
                }
                return hash;
            }
        }

        static int ComputeWidgetVertexLayoutHash(ModelMesh mesh) {
            unchecked {
                int hash = 17;
                if (mesh == null) {
                    return hash;
                }
                foreach (ModelMeshPart part in mesh.MeshParts) {
                    VertexDeclaration declaration = part?.VertexBuffer?.VertexDeclaration;
                    if (declaration == null) {
                        hash = hash * 31;
                        continue;
                    }
                    hash = hash * 31 + declaration.VertexStride;
                    foreach (VertexElement element in declaration.VertexElements) {
                        hash = hash * 31 + element.Semantic.GetHashCode();
                        hash = hash * 31 + element.Format.GetHashCode();
                        hash = hash * 31 + element.Offset;
                    }
                }
                hash = hash * 31 + (HasSkinningData(mesh) ? 1 : 0);
                return hash;
            }
        }

        static void AddMorphTargetDefines(ShaderDefines defines, ModelMesh mesh) {
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.HasMorphTargets != true) {
                    continue;
                }
                defines.Add("USE_MORPHING");
                defines.Add("HAS_MORPH_TARGETS");
                defines.AddRaw($"WEIGHT_COUNT {part.MorphTargetCount}");
                if (part.HasMorphTargetPosition) {
                    defines.Add("HAS_MORPH_TARGET_POSITION");
                    defines.AddRaw($"MORPH_TARGET_POSITION_OFFSET {part.MorphTargetPositionOffset}");
                }
                if (part.HasMorphTargetNormal) {
                    defines.Add("HAS_MORPH_TARGET_NORMAL");
                    defines.AddRaw($"MORPH_TARGET_NORMAL_OFFSET {part.MorphTargetNormalOffset}");
                }
                if (part.HasMorphTargetTangent) {
                    defines.Add("HAS_MORPH_TARGET_TANGENT");
                    defines.AddRaw($"MORPH_TARGET_TANGENT_OFFSET {part.MorphTargetTangentOffset}");
                }
                if (part.HasMorphTargetTexCoord0) {
                    defines.Add("HAS_MORPH_TARGET_TEXCOORD_0");
                    defines.AddRaw($"MORPH_TARGET_TEXCOORD_0_OFFSET {part.MorphTargetTexCoord0Offset}");
                }
                if (part.HasMorphTargetTexCoord1) {
                    defines.Add("HAS_MORPH_TARGET_TEXCOORD_1");
                    defines.AddRaw($"MORPH_TARGET_TEXCOORD_1_OFFSET {part.MorphTargetTexCoord1Offset}");
                }
                if (part.HasMorphTargetColor0) {
                    defines.Add("HAS_MORPH_TARGET_COLOR_0");
                    defines.AddRaw($"MORPH_TARGET_COLOR_0_OFFSET {part.MorphTargetColor0Offset}");
                }
                break;
            }
        }

        void AddMaterialDefines(ShaderDefines defines, ModelMaterial material) {
            defines.Add("MATERIAL_METALLICROUGHNESS");
            material.PopulateDefines(defines);
        }

        protected override int ComputeMaterialHash(ModelMaterial material) {
            int hash = base.ComputeMaterialHash(material);
            if (material?.SpecularGlossiness?.IsEnabled == true) {
                hash = hash * 31 + "SPEC_GLOSS".GetHashCode();
            }
            if (material?.Unlit?.IsEnabled == true) {
                hash = hash * 31 + "UNLIT".GetHashCode();
            }
            return hash;
        }

        void AddToneMapDefine(ShaderDefines defines, ToneMapMode mode) {
            switch (mode) {
                case ToneMapMode.KhrPbrNeutral: defines.Add("TONEMAP_KHR_PBR_NEUTRAL"); break;
                case ToneMapMode.AcesNarkowicz: defines.Add("TONEMAP_ACES_NARKOWICZ"); break;
                case ToneMapMode.AcesHill: defines.Add("TONEMAP_ACES_HILL"); break;
                case ToneMapMode.AcesHillExposureBoost: defines.Add("TONEMAP_ACES_HILL_EXPOSURE_BOOST"); break;
            }
        }

        #endregion
    }
}
