using System;
using System.Collections.Generic;
using System.IO;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Shader = Engine.Graphics.Shader;
using Vector4 = System.Numerics.Vector4;

namespace Game {
    /// <summary>
    /// glTF PBR 渲染器
    /// 继承 AdvancedMeshRenderer，添加 PBR 材质 UBO 和 IBL 支持
    /// </summary>
    public class GltfPbrRenderer : AdvancedMeshRenderer {
        static readonly ModelMaterial DefaultDielectricMaterial = new() {
            MetallicFactor = 0f, RoughnessFactor = 1.0f, BaseColorFactor = Vector4.One
        };

        static readonly ModelMaterial DefaultDielectricBlendMaterial = new() {
            MetallicFactor = 0f, RoughnessFactor = 1.0f, BaseColorFactor = Vector4.One, AlphaMode = ModelAlphaMode.Mask, AlphaCutoff = 0.01f
        };

        static readonly int InstancedHashSalt = "__INSTANCED__".GetHashCode();
        readonly Dictionary<(ModelMesh, ModelMaterial, Texture2D), List<InstanceRenderData>> _instanceGroups = new();

        // PBR 材质 UBO（原 PbrMeshRenderer）
        readonly UniformBuffer<MaterialCoreData> _materialCoreUBO = new(1);
        readonly UniformBuffer<MaterialExtensionData> _materialExtUBO = new(6);
        Texture2D _currentTextureOverride;
        bool _shadersLoaded;
        readonly Dictionary<int, int> _morphSamplerLocationCache = [];
        readonly Dictionary<(int programHandle, int weightIndex), int> _morphWeightLocationCache = [];

        public IblSampler IblSampler { get; private set; }

        public override bool HasIBL => IblSampler != null;

        public Dictionary<SubsystemModelsRenderer.ModelData, CelestialBodyCacheEntry> CelestialBodyCache { get; } = new();

        public void LoadEnvironmentMap(Stream hdrStream) {
            IblSampler?.Dispose();
            IblSampler = new IblSampler();
            EnvironmentMap envMap = EnvironmentMap.LoadHDR(hdrStream);
            IblSampler.Process(envMap);
            MipCount = IblSampler.MipCount;
            envMap.Dispose();
        }

        static void AddShader(Dictionary<string, string> shaders, string basePath, string name) {
            string path = Storage.CombinePaths(basePath, name);
            Stream stream = ContentManager.GetStream(path);
            shaders[name] = new StreamReader(stream).ReadToEnd();
        }

        protected override void LoadShaderSources() {
            if (_shadersLoaded) {
                return;
            }
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

        public override void RenderPart(ModelMesh mesh,
            ModelMeshPart part,
            ModelMaterial material,
            SubsystemModelsRenderer.ModelData modelData,
            Texture2D textureOverride,
            JointTexture jointTexture = null) {
            if (part == null) {
                return;
            }
            _currentTextureOverride = textureOverride;
            ModelMaterial effectiveMaterial;
            if (material != null) {
                effectiveMaterial = material;
            }
            else if (textureOverride != null) {
                effectiveMaterial = textureOverride is RenderTarget2D ? DefaultDielectricBlendMaterial : DefaultDielectricMaterial;
            }
            else {
                effectiveMaterial = null;
            }
            Shader shader = GetOrCreateShader(mesh, effectiveMaterial, CurrentContext);
            if (shader == null) {
                Log.Error("GltfPbrRenderer: shader is null!");
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
            if (IblSampler != null
                && CurrentContext.UseIBL) {
                BindIBLTextures();
            }
            if (jointTexture != null) {
                BindJointTexture(jointTexture, shader);
            }
            SetupMorphTargets(part, shader);
            SetupDepthState(effectiveMaterial);
            bool isNegativeScale = DetectNegativeScale(modelData);
            SetupCullMode(effectiveMaterial, isNegativeScale);
            SetupBlendMode(effectiveMaterial, CurrentContext);
            GLWrapper.ApplyViewportScissor(Display.Viewport, Display.ScissorRectangle, Display.RasterizerState.ScissorTestEnable);
            DrawMeshPart(part);
        }

        static bool DetectNegativeScale(SubsystemModelsRenderer.ModelData modelData) {
            if (modelData?.ComponentModel?.Model == null) {
                return false;
            }
            Matrix? boneTransform = modelData.ComponentModel.GetBoneTransform(modelData.ComponentModel.Model.RootBone.Index);
            if (!boneTransform.HasValue) {
                return false;
            }
            return boneTransform.Value.Determinant() < 0f;
        }

        public override void RenderInstances(List<InstanceRenderData> instances) {
            if (instances == null
                || instances.Count == 0) {
                return;
            }

            // 1. 按 (mesh, material, textureOverride) 分组（复用字典）
            _instanceGroups.Clear();
            foreach (InstanceRenderData inst in instances) {
                (ModelMesh Mesh, ModelMaterial Material, Texture2D TextureOverride) key = (inst.Mesh, inst.Material, inst.TextureOverride);
                if (!_instanceGroups.TryGetValue(key, out List<InstanceRenderData> list)) {
                    list = new List<InstanceRenderData>();
                    _instanceGroups[key] = list;
                }
                list.Add(inst);
            }

            // 2. 逐组渲染
            foreach (KeyValuePair<(ModelMesh, ModelMaterial, Texture2D), List<InstanceRenderData>> kvp in _instanceGroups) {
                (ModelMesh mesh, ModelMaterial material, Texture2D textureOverride) = kvp.Key;
                List<InstanceRenderData> groupInstances = kvp.Value;
                if (mesh == null) {
                    continue;
                }
                _currentTextureOverride = textureOverride;
                ModelMaterial effectiveMaterial;
                if (material != null) {
                    effectiveMaterial = material;
                }
                else if (textureOverride != null) {
                    effectiveMaterial = textureOverride is RenderTarget2D ? DefaultDielectricBlendMaterial : DefaultDielectricMaterial;
                }
                else {
                    effectiveMaterial = null;
                }
                Shader shader = GetOrCreateInstancedShader(mesh, effectiveMaterial, CurrentContext);
                if (shader == null) {
                    continue;
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
                UpdateRenderStateUBOForInstancing();
                UpdateLightsUBO(1f);
                UpdateMaterialUBOs(effectiveMaterial, false);
                UpdateUVTransformUBO(effectiveMaterial);
                Model model = groupInstances[0].ModelData.ComponentModel.Model;
                if (textureOverride != null) {
                    MaterialTextureBinder.BindTexture2D(textureOverride, MaterialTextureSlot.BaseColor);
                    MaterialTextureBinder.SetTextureSlotUniforms(shader);
                }
                else if (model != null
                    && material != null) {
                    BindMaterialTextures(model, material, shader, null);
                }
                if (IblSampler != null
                    && CurrentContext.UseIBL) {
                    BindIBLTextures();
                }

                // 分批绘制（每批最多 MaxInstancesPerBatch 个实例）
                // 正负缩放实例不能共享同一 draw call（剔除方向不同），需分两次渲染
                for (int offset = 0; offset < groupInstances.Count; offset += MaxInstancesPerBatch) {
                    int count = Math.Min(MaxInstancesPerBatch, groupInstances.Count - offset);
                    // 单次遍历：正缩放放前部，负缩放放尾部
                    int posCount = 0, negCount = 0;
                    for (int i = 0; i < count; i++) {
                        InstanceRenderData inst = groupInstances[offset + i];
                        Vector2 light = new(inst.ModelData.Light, GetCelestialBodyVisible(inst.ModelData) ? 1f : 0f);
                        if (inst.WorldMatrix.Determinant() < 0f) {
                            _instanceMatrices[MaxInstancesPerBatch - 1 - negCount] = inst.WorldMatrix;
                            _instanceLightData[MaxInstancesPerBatch - 1 - negCount] = light;
                            negCount++;
                        } else {
                            _instanceMatrices[posCount] = inst.WorldMatrix;
                            _instanceLightData[posCount] = light;
                            posCount++;
                        }
                    }
                    if (posCount > 0) {
                        DrawInstanceBatch(mesh, effectiveMaterial, posCount, false);
                    }
                    if (negCount > 0) {
                        for (int i = 0; i < negCount; i++) {
                            _instanceMatrices[i] = _instanceMatrices[MaxInstancesPerBatch - 1 - i];
                            _instanceLightData[i] = _instanceLightData[MaxInstancesPerBatch - 1 - i];
                        }
                        DrawInstanceBatch(mesh, effectiveMaterial, negCount, true);
                    }
                }
            }
            _currentTextureOverride = null;
        }

        void DrawInstanceBatch(ModelMesh mesh, ModelMaterial material, int count, bool isNegativeScale) {
            UploadInstanceData(_instanceMatrices, count);
            UploadInstanceLightData(_instanceLightData, count);
            SetupInstanceAttributes();
            SetupDepthState(material);
            SetupCullMode(material, isNegativeScale);
            SetupBlendMode(material, CurrentContext);
            DrawMeshInstanced(mesh, count);
            DisableInstanceAttributes();
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

        protected override Shader CreateShaderVariant(ModelMesh mesh, ModelMaterial material, in RenderContext context) =>
            CreateShaderVariantInternal(mesh, material, context, false);

        protected override Shader GetOrCreateShader(ModelMesh mesh, ModelMaterial material, in RenderContext context) {
            int materialHash = ComputeMaterialHash(material) * 31 + ComputeMorphHash(mesh);
            int contextHash = AdjustContextHashForMaterial(CachedContextHash, material, context);
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) {
                return shader;
            }
            return CreateShaderVariant(mesh, material, context);
        }

        Shader CreateShaderVariantInternal(ModelMesh mesh, ModelMaterial material, in RenderContext context, bool isInstanced) {
            ShaderDefines defines = new();
            AddVertexAttributeDefines(defines, mesh);
            if (isInstanced) {
                defines.Add("USE_INSTANCING");
            }
            if (material != null) {
                AddMaterialDefines(defines, material);
            }
            // DiffuseTransmission 需要 IBL 采样背面环境光
            bool useIBL = context.UseIBL || material?.DiffuseTransmission?.IsEnabled == true;
            if (useIBL) {
                defines.Add("USE_IBL");
            }
            // Unlit 材质不需要灯光计算
            if (context.LightCount > 0
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
            if (!isInstanced
                && context.EnableMorphing
                && HasMorphTargetData(mesh)) {
                AddMorphTargetDefines(defines, mesh);
            }
            ModelAlphaMode alphaMode = material?.AlphaMode ?? ModelAlphaMode.Opaque;
            defines.AddRaw($"ALPHAMODE {(int)alphaMode}");
            // 根据工作流选择片段着色器
            string fragShader = context.IsScatterPass ? "scatter.frag"
                : material?.SpecularGlossiness?.IsEnabled == true ? "specular_glossiness.frag"
                : "pbr.frag";
            try {
                int vertHash = ShaderCache.SelectShader("primitive.vert", defines);
                int fragHash = ShaderCache.SelectShader(fragShader, defines);
                return ShaderCache.GetShaderProgram(vertHash, fragHash);
            }
            catch (Exception ex) {
                Log.Error($"GltfPbrRenderer: shader compile failed: {ex.Message}");
                return null;
            }
        }

        Shader GetOrCreateInstancedShader(ModelMesh mesh, ModelMaterial material, in RenderContext context) {
            int materialHash = ComputeMaterialHash(material) * 31 + InstancedHashSalt;
            int contextHash = AdjustContextHashForMaterial(CachedContextHash, material, context);
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) {
                return shader;
            }
            return CreateShaderVariantInternal(mesh, material, context, true);
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
            if (!HasMorphTargetData(mesh)) return 0;
            unchecked {
                int hash = "__MORPH__".GetHashCode();
                foreach (ModelMeshPart part in mesh.MeshParts) {
                    if (part?.HasMorphTargets != true) continue;
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
                break; // 第一个有 morph target 的 part 即可定义
            }
        }

        void SetupMorphTargets(ModelMeshPart part, Shader shader) {
            if (part?.HasMorphTargets != true) {
                return;
            }
            part.MorphTargetTexture.Bind((Silk.NET.OpenGLES.TextureUnit)((int)Silk.NET.OpenGLES.TextureUnit.Texture0 + (int)MaterialTextureSlot.MorphTargets));
            int programHandle = shader.m_program;
            if (!_morphSamplerLocationCache.TryGetValue(programHandle, out int samplerLoc)) {
                samplerLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_MorphTargetsSampler");
                _morphSamplerLocationCache[programHandle] = samplerLoc;
            }
            if (samplerLoc >= 0) {
                GLWrapper.GL.Uniform1(samplerLoc, (int)MaterialTextureSlot.MorphTargets);
            }
            float[] weights = part.MorphWeights;
            if (weights == null) {
                return;
            }
            for (int i = 0; i < part.MorphTargetCount && i < weights.Length; i++) {
                var cacheKey = (programHandle, i);
                if (!_morphWeightLocationCache.TryGetValue(cacheKey, out int loc)) {
                    loc = GLWrapper.GL.GetUniformLocation((uint)programHandle, $"u_morphWeights[{i}]");
                    _morphWeightLocationCache[cacheKey] = loc;
                }
                if (loc >= 0) {
                    GLWrapper.GL.Uniform1(loc, weights[i]);
                }
            }
        }

        void AddMaterialDefines(ShaderDefines defines, ModelMaterial material) {
            defines.Add("MATERIAL_METALLICROUGHNESS");
            if (material.BaseColorTexture?.HasTexture == true
                || _currentTextureOverride != null) {
                defines.Add("HAS_BASE_COLOR_MAP");
            }
            if (material.MetallicRoughnessTexture?.HasTexture == true) {
                defines.Add("HAS_METALLIC_ROUGHNESS_MAP");
            }
            if (material.NormalTexture?.HasTexture == true) {
                defines.Add("HAS_NORMAL_MAP");
            }
            if (material.OcclusionTexture?.HasTexture == true) {
                defines.Add("HAS_OCCLUSION_MAP");
            }
            if (material.EmissiveTexture?.HasTexture == true) {
                defines.Add("HAS_EMISSIVE_MAP");
            }
            if (material.ClearCoat?.IsEnabled == true) {
                defines.Add("MATERIAL_CLEARCOAT");
            }
            if (material.Sheen?.IsEnabled == true) {
                defines.Add("MATERIAL_SHEEN");
            }
            if (material.Transmission?.IsEnabled == true) {
                defines.Add("MATERIAL_TRANSMISSION");
            }
            if (material.Volume?.IsEnabled == true) {
                defines.Add("MATERIAL_VOLUME");
            }
            if (material.Iridescence?.IsEnabled == true) {
                defines.Add("MATERIAL_IRIDESCENCE");
            }
            if (material.Specular?.IsEnabled == true) {
                defines.Add("MATERIAL_SPECULAR");
            }
            if (material.Anisotropy?.IsEnabled == true) {
                defines.Add("MATERIAL_ANISOTROPY");
            }
            if (material.DiffuseTransmission?.IsEnabled == true) {
                defines.Add("MATERIAL_DIFFUSE_TRANSMISSION");
            }
            if (material.SpecularGlossiness?.IsEnabled == true) {
                defines.Add("MATERIAL_SPECULAR_GLOSSINESS");
            }
            if (material.EmissiveStrength?.IsEnabled == true) {
                defines.Add("MATERIAL_EMISSIVE_STRENGTH");
            }
            if (material.Unlit?.IsEnabled == true) {
                defines.Add("MATERIAL_UNLIT");
            }
        }

        protected override int ComputeMaterialHash(ModelMaterial material) {
            int hash = base.ComputeMaterialHash(material);
            if (_currentTextureOverride != null) {
                hash = hash * 31 + "__TEX_OVERRIDE__".GetHashCode();
            }
            // SpecularGlossiness 和 Unlit 影响片段着色器选择
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

        bool GetCelestialBodyVisible(SubsystemModelsRenderer.ModelData modelData) {
            double now = Time.FrameStartTime;
            if (CelestialBodyCache.TryGetValue(modelData, out CelestialBodyCacheEntry entry)
                && now - entry.Timestamp < 0.1) {
                return entry.Visible;
            }
            bool visible = CalculateCelestialBodyVisibility(modelData);
            CelestialBodyCache[modelData] = new CelestialBodyCacheEntry { Visible = visible, Timestamp = now };
            return visible;
        }

        bool CalculateCelestialBodyVisibility(SubsystemModelsRenderer.ModelData modelData) {
            Vector3 dir = new(-ActiveLightDirection.X, -ActiveLightDirection.Y, -ActiveLightDirection.Z);
            if (dir.Y < 0f) {
                return false;
            }
            Vector3 p;
            if (modelData.ComponentBody != null) {
                p = modelData.ComponentBody.Position;
                p.Y += 0.95f * (modelData.ComponentBody.BoundingBox.Max.Y - modelData.ComponentBody.BoundingBox.Min.Y);
            }
            else {
                Matrix? boneTransform = modelData.ComponentModel.GetBoneTransform(modelData.ComponentModel.Model.RootBone.Index);
                p = !boneTransform.HasValue ? Vector3.Zero : boneTransform.Value.Translation + new Vector3(0f, 0.9f, 0f);
            }
            int cellX = Terrain.ToCell(p.X);
            int cellZ = Terrain.ToCell(p.Z);
            int topHeight = _subsystemTerrain.Terrain.CalculateTopmostCellHeight(cellX, cellZ);
            float maxDist = p.Y >= topHeight ? 16f : 32f;
            Vector3 end = p + dir * maxDist;
            TerrainRaycastResult? result = _subsystemTerrain.Raycast(p, end, false, true, null);
            return !result.HasValue;
        }

        public override void Dispose() {
            IblSampler?.Dispose();
            _materialCoreUBO?.Dispose();
            _materialExtUBO?.Dispose();
            _morphSamplerLocationCache.Clear();
            _morphWeightLocationCache.Clear();
            CelestialBodyCache.Clear();
            base.Dispose();
        }

        // 天体可见性缓存（懒计算，0.1s 节流）
        public struct CelestialBodyCacheEntry {
            public bool Visible;
            public double Timestamp;
        }
    }
}