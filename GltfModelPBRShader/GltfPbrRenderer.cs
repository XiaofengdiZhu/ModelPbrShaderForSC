using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Shader = Engine.Graphics.Shader;

namespace Game {
    /// <summary>
    /// glTF PBR 渲染器
    /// 继承 AdvancedMeshRenderer，添加 PBR 材质 UBO 和 IBL 支持
    /// </summary>
    public class GltfPbrRenderer : AdvancedMeshRenderer {
        // PBR 材质 UBO（原 PbrMeshRenderer）
        UniformBuffer<MaterialCoreData> _materialCoreUBO;
        UniformBuffer<MaterialExtensionData> _materialExtUBO;

        IblSampler _iblSampler;
        bool _shadersLoaded;
        readonly Dictionary<(ModelMesh, ModelMaterial, Texture2D), List<InstanceRenderData>> _instanceGroups = new();
        string _lastDefines;
        Texture2D _currentTextureOverride;

        static readonly ModelMaterial DefaultDielectricMaterial = new() {
            MetallicFactor = 0f,
            RoughnessFactor = 1.0f,
            BaseColorFactor = System.Numerics.Vector4.One
        };

        static readonly ModelMaterial DefaultDielectricBlendMaterial = new() {
            MetallicFactor = 0f,
            RoughnessFactor = 1.0f,
            BaseColorFactor = System.Numerics.Vector4.One,
            AlphaMode = ModelAlphaMode.Mask,
            AlphaCutoff = 0.01f
        };

        public IblSampler IblSampler => _iblSampler;

        public GltfPbrRenderer() {
            _materialCoreUBO = new(1);
            _materialExtUBO = new(6);
        }

        public override bool HasIBL => _iblSampler != null;

        public void LoadEnvironmentMap(Stream hdrStream) {
            _iblSampler?.Dispose();
            _iblSampler = new IblSampler();

            EnvironmentMap envMap = EnvironmentMap.LoadHDR(hdrStream);
            _iblSampler.Process(envMap);
            MipCount = _iblSampler.MipCount;
            envMap.Dispose();
        }

        static void AddShader(Dictionary<string, string> shaders, string basePath, string name) {
            string path = Storage.CombinePaths(basePath, name);
            Stream stream = ContentManager.GetStream(path);
            shaders[name] = new StreamReader(stream).ReadToEnd();
        }

        protected override void LoadShaderSources() {
            if (_shadersLoaded) return;

            string basePath = "GltfModelPbrShaders/pbr/";
            Dictionary<string, string> shaders = new();

            AddShader(shaders, basePath, "primitive.vert");
            AddShader(shaders, basePath, "pbr.frag");
            AddShader(shaders, basePath, "ubos.glsl");
            AddShader(shaders, basePath, "functions.glsl");
            AddShader(shaders, basePath, "textures.glsl");
            AddShader(shaders, basePath, "material_info.glsl");
            AddShader(shaders, basePath, "brdf.glsl");
            AddShader(shaders, basePath, "ibl.glsl");
            AddShader(shaders, basePath, "punctual.glsl");
            AddShader(shaders, basePath, "tonemapping.glsl");
            AddShader(shaders, basePath, "animation.glsl");
            AddShader(shaders, basePath, "iridescence.glsl");
            AddShader(shaders, basePath, "specular_glossiness.frag");
            AddShader(shaders, basePath, "scatter.frag");

            AddShader(shaders, "GltfModelPbrShaders/", "fullscreen.vert");
            AddShader(shaders, "GltfModelPbrShaders/", "panorama_to_cubemap.frag");
            AddShader(shaders, "GltfModelPbrShaders/", "ibl_filtering.frag");

            ShaderCache.LoadShaderSources(shaders, basePath);
            _shadersLoaded = true;
        }

        protected override void SetupShaderCallbacks() {
            ShaderCache.BindAttributeLocationsCallback = BindAttributeLocations;
            ShaderCache.BindUniformBlockBindingsCallback = BindUniformBlocks;
        }

        static void BindAttributeLocations(uint program) {
            GLWrapper.GL.BindAttribLocation(program, 0, "a_position");
            GLWrapper.GL.BindAttribLocation(program, 1, "a_normal");
            GLWrapper.GL.BindAttribLocation(program, 2, "a_texcoord_0");
            GLWrapper.GL.BindAttribLocation(program, 3, "a_texcoord_1");
            GLWrapper.GL.BindAttribLocation(program, 4, "a_color_0");
            GLWrapper.GL.BindAttribLocation(program, 5, "a_tangent");
            GLWrapper.GL.BindAttribLocation(program, 6, "a_joints_0");
            GLWrapper.GL.BindAttribLocation(program, 7, "a_weights_0");
            GLWrapper.GL.BindAttribLocation(program, 8, "a_instance_model_matrix");
            GLWrapper.GL.BindAttribLocation(program, InstanceLightAttribLocation, "a_instance_light");
        }

        static void BindUniformBlocks(uint program) {
            BindUniformBlock(program, "SceneData", 0);
            BindUniformBlock(program, "MaterialCoreData", 1);
            BindUniformBlock(program, "LightsData", 2);
            BindUniformBlock(program, "RenderStateData", 3);
            BindUniformBlock(program, "UVTransformData", 4);
            BindUniformBlock(program, "MaterialExtensionData", 6);
        }

        public override void Render(ModelMesh mesh, ModelMaterial material,
            SubsystemModelsRenderer.ModelData modelData,
            Texture2D textureOverride,
            JointTexture jointTexture = null) {
            if (mesh == null) return;

            _currentTextureOverride = textureOverride;

            ModelMaterial effectiveMaterial;
            if (material != null) {
                effectiveMaterial = material;
            }
            else if (textureOverride != null) {
                effectiveMaterial = textureOverride is RenderTarget2D
                    ? DefaultDielectricBlendMaterial
                    : DefaultDielectricMaterial;
            }
            else {
                effectiveMaterial = null;
            }

            Shader shader = GetOrCreateShader(mesh, effectiveMaterial, CurrentContext);
            if (shader == null) {
                Engine.Log.Error("GltfPbrRenderer: shader is null!");
                return;
            }

            shader.PrepareForDrawing();

            GLWrapper.UseProgram(shader.m_program);

            int programHandle = shader.m_program;
            if (!_glymulLocationCache.TryGetValue(programHandle, out int glymulLoc)) {
                glymulLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_glymul");
                _glymulLocationCache[programHandle] = glymulLoc;
            }
            if (glymulLoc >= 0) {
                float glymul = Display.RenderTarget != null ? -1f : 1f;
                GLWrapper.GL.Uniform1(glymulLoc, glymul);
            }

            // 非 instanced 路径: 设置地形光照和太阳可见性 uniform
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
                GLWrapper.GL.Uniform1(celestialBodyVisibleLoc, modelData.CelestialBodyVisible ? 1f : 0f);
            }

            UpdateRenderStateUBO(CurrentContext.Wvp, CurrentContext.CameraView);
            UpdateMaterialUBOs(effectiveMaterial, false);
            UpdateUVTransformUBO(effectiveMaterial);

            if (textureOverride != null) {
                MaterialTextureBinder.BindTexture2D(textureOverride, MaterialTextureSlot.BaseColor);
                MaterialTextureBinder.SetTextureSlotUniforms(shader);
            }
            else if (material != null) {
                Model model = modelData.ComponentModel?.Model;
                if (model != null) {
                    BindMaterialTextures(model, material, shader, null);
                }
            }

            if (_iblSampler != null && CurrentContext.UseIBL) {
                BindIBLTextures();
            }

            if (jointTexture != null) {
                BindJointTexture(jointTexture, shader);
            }

            SetupDepthState(effectiveMaterial);
            SetupCullMode(effectiveMaterial);
            SetupBlendMode(effectiveMaterial, CurrentContext);
            DrawMesh(mesh);
        }

        public override void RenderInstances(List<InstanceRenderData> instances) {
            if (instances == null || instances.Count == 0) return;

            // 1. 按 (mesh, material, textureOverride) 分组（复用字典）
            _instanceGroups.Clear();
            foreach (var inst in instances) {
                var key = (inst.Mesh, inst.Material, inst.TextureOverride);
                if (!_instanceGroups.TryGetValue(key, out var list)) {
                    list = new List<InstanceRenderData>();
                    _instanceGroups[key] = list;
                }
                list.Add(inst);
            }

            // 2. 逐组渲染
            foreach (var kvp in _instanceGroups) {
                var (mesh, material, textureOverride) = kvp.Key;
                var groupInstances = kvp.Value;

                if (mesh == null) continue;

                _currentTextureOverride = textureOverride;

                ModelMaterial effectiveMaterial;
                if (material != null) {
                    effectiveMaterial = material;
                }
                else if (textureOverride != null) {
                    effectiveMaterial = textureOverride is RenderTarget2D
                        ? DefaultDielectricBlendMaterial
                        : DefaultDielectricMaterial;
                }
                else {
                    effectiveMaterial = null;
                }

                Shader shader = GetOrCreateInstancedShader(mesh, effectiveMaterial, CurrentContext);
                if (shader == null) continue;

                shader.PrepareForDrawing();
                GLWrapper.UseProgram(shader.m_program);

                int programHandle = shader.m_program;
                if (!_glymulLocationCache.TryGetValue(programHandle, out int glymulLoc)) {
                    glymulLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_glymul");
                    _glymulLocationCache[programHandle] = glymulLoc;
                }
                if (glymulLoc >= 0) {
                    float glymul = Display.RenderTarget != null ? -1f : 1f;
                    GLWrapper.GL.Uniform1(glymulLoc, glymul);
                }

                UpdateRenderStateUBOForInstancing();
                UpdateLightsUBO(1f);
                UpdateMaterialUBOs(effectiveMaterial, false);
                UpdateUVTransformUBO(effectiveMaterial);

                Model model = groupInstances[0].Model;
                if (textureOverride != null) {
                    MaterialTextureBinder.BindTexture2D(textureOverride, MaterialTextureSlot.BaseColor);
                    MaterialTextureBinder.SetTextureSlotUniforms(shader);
                }
                else if (model != null && material != null) {
                    BindMaterialTextures(model, material, shader, null);
                }

                if (_iblSampler != null && CurrentContext.UseIBL) {
                    BindIBLTextures();
                }

                // 分批绘制（每批最多 MaxInstancesPerBatch 个实例）
                for (int offset = 0; offset < groupInstances.Count; offset += MaxInstancesPerBatch) {
                    int count = Math.Min(MaxInstancesPerBatch, groupInstances.Count - offset);
                    for (int i = 0; i < count; i++) {
                        _instanceMatrices[i] = groupInstances[offset + i].WorldMatrix;
                        _instanceLightData[i] = new System.Numerics.Vector2(
                            groupInstances[offset + i].LightIntensity,
                            groupInstances[offset + i].CelestialBodyVisible);
                    }

                    UploadInstanceData(_instanceMatrices, count);
                    UploadInstanceLightData(_instanceLightData, count);
                    SetupInstanceAttributes();
                    SetupDepthState(effectiveMaterial);
                    SetupCullMode(effectiveMaterial);
                    SetupBlendMode(effectiveMaterial, CurrentContext);
                    DrawMeshInstanced(mesh, count);
                    DisableInstanceAttributes();
                }
            }

            _currentTextureOverride = null;
        }

        void BindIBLTextures() {
            MaterialTextureBinder.BindIBLTextures(
                _iblSampler.LambertianTexture,
                _iblSampler.GGXTexture,
                _iblSampler.SheenTexture,
                _iblSampler.GGXLut,
                _iblSampler.CharlieLut
            );
        }

        void UpdateMaterialUBOs(ModelMaterial material, bool useGeneratedTangents) {
            int extensionFlags = (int)MaterialUboBuilder.BuildExtensionFlags(material);

            if (LastMaterial != material) {
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

        protected override Shader CreateShaderVariant(ModelMesh mesh, ModelMaterial material, in RenderContext context) {
            return CreateShaderVariantInternal(mesh, material, context, false);
        }

        Shader CreateShaderVariantInternal(ModelMesh mesh, ModelMaterial material, in RenderContext context, bool isInstanced) {
            ShaderDefines defines = new();

            AddVertexAttributeDefines(defines, mesh);

            if (isInstanced) defines.Add("USE_INSTANCING");

            if (material != null) {
                AddMaterialDefines(defines, material);
            }

            if (context.UseIBL) defines.Add("USE_IBL");
            if (context.LightCount > 0) defines.Add("USE_PUNCTUAL");
            if (context.UseLinearOutput) {
                defines.Add("LINEAR_OUTPUT");
            }
            else {
                AddToneMapDefine(defines, context.ToneMapMode);
            }
            if (context.DebugChannel != DebugChannel.None) {
                defines.AddRaw($"DEBUG {(int)context.DebugChannel}");
            }
            if (!isInstanced && HasSkinningData(mesh)) {
                defines.Add("USE_SKINNING");
            }

            ModelAlphaMode alphaMode = material?.AlphaMode ?? ModelAlphaMode.Opaque;
            defines.AddRaw($"ALPHAMODE {(int)alphaMode}");

            _lastDefines = defines.ToString();

            try {
                int vertHash = ShaderCache.SelectShader("primitive.vert", defines);
                int fragHash = ShaderCache.SelectShader("pbr.frag", defines);
                return ShaderCache.GetShaderProgram(vertHash, fragHash);
            }
            catch (Exception ex) {
                Engine.Log.Error($"GltfPbrRenderer: shader compile failed: {ex.Message}");
                return null;
            }
        }

        static readonly int InstancedHashSalt = "__INSTANCED__".GetHashCode();

        Shader GetOrCreateInstancedShader(ModelMesh mesh, ModelMaterial material, in RenderContext context) {
            int materialHash = ComputeMaterialHash(material) * 31 + InstancedHashSalt;
            int contextHash = CachedContextHash;
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) return shader;
            return CreateShaderVariantInternal(mesh, material, context, true);
        }

        static void AddVertexAttributeDefines(ShaderDefines defines, ModelMesh mesh) {
            if (mesh == null) return;
            HashSet<string> semantics = new();
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.VertexBuffer?.VertexDeclaration == null) continue;
                foreach (VertexElement element in part.VertexBuffer.VertexDeclaration.VertexElements) {
                    semantics.Add(element.Semantic);
                }
            }
            if (semantics.Contains("NORMAL")) defines.Add("HAS_NORMAL_VEC3");
            if (semantics.Contains("TANGENT")) defines.Add("HAS_TANGENT_VEC4");
            if (semantics.Contains("TEXCOORD") || semantics.Contains("TEXCOORD0")) defines.Add("HAS_TEXCOORD_0_VEC2");
            if (semantics.Contains("TEXCOORD1")) defines.Add("HAS_TEXCOORD_1_VEC2");
            if (semantics.Contains("COLOR")) {
                defines.Add("HAS_COLOR_0_VEC4");
            }
            if (semantics.Contains("BLENDINDICES")) defines.Add("HAS_JOINTS_0_VEC4");
            if (semantics.Contains("BLENDWEIGHTS")) defines.Add("HAS_WEIGHTS_0_VEC4");
        }

        static bool HasSkinningData(ModelMesh mesh) {
            if (mesh == null) return false;
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.VertexBuffer?.VertexDeclaration != null) {
                    foreach (VertexElement element in part.VertexBuffer.VertexDeclaration.VertexElements) {
                        if (element.Semantic == "BLENDINDICES" ||
                            element.Semantic == "BLENDWEIGHTS") {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        void AddMaterialDefines(ShaderDefines defines, ModelMaterial material) {
            defines.Add("MATERIAL_METALLICROUGHNESS");

            if (material.BaseColorTexture?.HasTexture == true || _currentTextureOverride != null) defines.Add("HAS_BASE_COLOR_MAP");
            if (material.MetallicRoughnessTexture?.HasTexture == true) defines.Add("HAS_METALLIC_ROUGHNESS_MAP");
            if (material.NormalTexture?.HasTexture == true) defines.Add("HAS_NORMAL_MAP");
            if (material.OcclusionTexture?.HasTexture == true) defines.Add("HAS_OCCLUSION_MAP");
            if (material.EmissiveTexture?.HasTexture == true) defines.Add("HAS_EMISSIVE_MAP");
            if (material.ClearCoat?.IsEnabled == true) defines.Add("MATERIAL_CLEARCOAT");
            if (material.Sheen?.IsEnabled == true) defines.Add("MATERIAL_SHEEN");
            if (material.Transmission?.IsEnabled == true) defines.Add("MATERIAL_TRANSMISSION");
            if (material.Volume?.IsEnabled == true) defines.Add("MATERIAL_VOLUME");
            if (material.Iridescence?.IsEnabled == true) defines.Add("MATERIAL_IRIDESCENCE");
            if (material.Specular?.IsEnabled == true) defines.Add("MATERIAL_SPECULAR");
            if (material.Anisotropy?.IsEnabled == true) defines.Add("MATERIAL_ANISOTROPY");
            if (material.DiffuseTransmission?.IsEnabled == true) defines.Add("MATERIAL_DIFFUSE_TRANSMISSION");
            if (material.SpecularGlossiness?.IsEnabled == true) defines.Add("MATERIAL_SPECULAR_GLOSSINESS");
        }

        protected override int ComputeMaterialHash(ModelMaterial material) {
            int hash = base.ComputeMaterialHash(material);
            if (_currentTextureOverride != null) {
                hash = hash * 31 + "__TEX_OVERRIDE__".GetHashCode();
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

        public override void Dispose() {
            _iblSampler?.Dispose();
            _materialCoreUBO?.Dispose();
            _materialExtUBO?.Dispose();
            base.Dispose();
        }
    }
}
