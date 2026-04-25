using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using Shader = Engine.Graphics.Shader;
using Vector4 = System.Numerics.Vector4;
using Vector2 = Engine.Vector2;
using Vector3 = Engine.Vector3;
using Matrix = Engine.Matrix;

namespace Game {
    /// <summary>
    /// glTF PBR 渲染器
    /// 继承 AdvancedMeshRenderer，添加 PBR 材质 UBO 和 IBL 支持
    /// 每个 mesh part 独立分类到 Opaque/Transparent/Transmission/Scatter 队列
    /// </summary>
    public class GltfPbrRenderer : AdvancedMeshRenderer {
        static readonly ModelMaterial DefaultDielectricMaterial = new() {
            MetallicFactor = 0f, RoughnessFactor = 1.0f, BaseColorFactor = Vector4.One
        };

        static readonly Comparison<PartRenderEntry> BackToFrontComparison = (a, b) => b.Depth.CompareTo(a.Depth);

        static readonly ModelMaterial DefaultDielectricBlendMaterial = new() {
            MetallicFactor = 0f, RoughnessFactor = 1.0f, BaseColorFactor = Vector4.One, AlphaMode = ModelAlphaMode.Mask, AlphaCutoff = 0.01f
        };

        static readonly int InstancedHashSalt = "__INSTANCED__".GetHashCode();
        readonly List<PartRenderEntry> _allTransparentEntries = [];
        readonly PbrFramebufferManager _framebufferManager = new();
        readonly Dictionary<(ModelMesh, ModelMaterial, Texture2D), List<PartRenderEntry>> _instanceGroups = new();

        // PBR 材质 UBO
        readonly UniformBuffer<MaterialCoreData> _materialCoreUBO = new(1);
        readonly UniformBuffer<MaterialExtensionData> _materialExtUBO = new(6);
        readonly Dictionary<int, int> _morphSamplerLocationCache = [];
        readonly Dictionary<(int programHandle, int weightIndex), int> _morphWeightLocationCache = [];

        // Per-mesh-part 渲染队列
        readonly List<PartRenderEntry> _opaqueEntries = [];
        readonly Dictionary<int, int> _scatterDepthSamplerLocCache = [];
        readonly List<PartRenderEntry> _scatterEntries = [];
        readonly Dictionary<int, int> _scatterSamplerLocCache = [];
        readonly HashSet<int> _scatterSamplesSetShaders = [];
        readonly List<PartRenderEntry> _skinnedOpaqueEntries = [];
        readonly Dictionary<int, int> _transmissionSamplerLocCache = [];
        readonly Dictionary<int, (int sizeLoc, int screenLoc)> _transmissionSizeLocCache = [];
        readonly List<PartRenderEntry> _transparentAfterWater = [];
        readonly List<PartRenderEntry> _transparentBeforeWater = [];
        readonly UniformBuffer<VolumeScatterData> _volumeScatterUBO = new(5);
        Shader _currentInstanceShader;
        bool _hasTransmissionThisFrame;
        bool _shadersLoaded;

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
            BindUniformBlock(program, "VolumeScatterData", 5);
            BindUniformBlock(program, "MaterialExtensionData", 6);
        }

        #region Queue Preparation

        public override void PrepareCustomQueues(Camera camera, List<SubsystemModelsRenderer.ModelData> allModels) {
            _opaqueEntries.Clear();
            _scatterEntries.Clear();
            _transparentBeforeWater.Clear();
            _transparentAfterWater.Clear();
            _allTransparentEntries.Clear();
            _hasTransmissionThisFrame = false;
            _transmissionFboCaptured = false;
            _transparentRendered = false;
            Viewport vp = Display.Viewport;
            _framebufferManager.SetSize(vp.Width, vp.Height);
            foreach (SubsystemModelsRenderer.ModelData md in allModels) {
                ComponentModel cm = md.ComponentModel;
                Model model = cm.Model;
                if (model == null) {
                    continue;
                }
                bool isUnderwater = cm.RenderingMode == ModelRenderingMode.TransparentAfterWater;
                Texture2D textureOverride = cm.TextureOverride;
                foreach (int meshIndex in cm.MeshDrawOrders) {
                    if (meshIndex < 0
                        || meshIndex >= model.Meshes.Count) {
                        continue;
                    }
                    ModelMesh mesh = model.Meshes[meshIndex];
                    if (!mesh.IsVisible) continue;
                    foreach (ModelMeshPart part in mesh.MeshParts) {
                        ModelMaterial mat = model.GetMaterial(part.MaterialIndex);
                        PartRenderQueue queueType = PartRenderEntry.ComputeQueueType(mat);
                        if (queueType == PartRenderQueue.Transmission) {
                            _hasTransmissionThisFrame = true;
                        }
                        PartRenderEntry entry = new() {
                            Mesh = mesh, Part = part, Material = mat, ModelData = md, TextureOverride = textureOverride, QueueType = queueType
                        };
                        switch (queueType) {
                            case PartRenderQueue.Scatter:
                                // Scatter parts 渲染两次：scatter pass（写入 scatter FBO）+ transparent pass（读取 scatter FBO）
                                _scatterEntries.Add(entry);
                                if (isUnderwater) {
                                    _transparentAfterWater.Add(entry);
                                }
                                else {
                                    _transparentBeforeWater.Add(entry);
                                }
                                break;
                            case PartRenderQueue.Opaque: _opaqueEntries.Add(entry); break;
                            default:
                                if (isUnderwater) {
                                    _transparentAfterWater.Add(entry);
                                }
                                else {
                                    _transparentBeforeWater.Add(entry);
                                }
                                break;
                        }
                    }
                }
            }
            CollectGlobalLights(allModels);
        }

        #endregion

        #region Morph Targets

        void SetupMorphTargets(ModelMeshPart part, Shader shader) {
            if (part?.HasMorphTargets != true) {
                return;
            }
            part.MorphTargetTexture.Bind((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.MorphTargets));
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
                (int programHandle, int i) cacheKey = (programHandle, i);
                if (!_morphWeightLocationCache.TryGetValue(cacheKey, out int loc)) {
                    loc = GLWrapper.GL.GetUniformLocation((uint)programHandle, $"u_morphWeights[{i}]");
                    _morphWeightLocationCache[cacheKey] = loc;
                }
                if (loc >= 0) {
                    GLWrapper.GL.Uniform1(loc, weights[i]);
                }
            }
        }

        #endregion

        public override void Dispose() {
            IblSampler?.Dispose();
            _materialCoreUBO?.Dispose();
            _materialExtUBO?.Dispose();
            _volumeScatterUBO?.Dispose();
            _framebufferManager?.Dispose();
            _morphSamplerLocationCache.Clear();
            _morphWeightLocationCache.Clear();
            _scatterSamplesSetShaders.Clear();
            _transmissionSamplerLocCache.Clear();
            _transmissionSizeLocCache.Clear();
            _scatterSamplerLocCache.Clear();
            _scatterDepthSamplerLocCache.Clear();
            CelestialBodyCache.Clear();
            base.Dispose();
        }

        public struct CelestialBodyCacheEntry {
            public bool Visible;
            public double Timestamp;
        }

        #region Render Passes

        public override void RenderOpaquePass(Camera camera) {
            // 1. Scatter pass
            if (_scatterEntries.Count > 0) {
                _framebufferManager.EnsureScatterFramebuffer();
                _framebufferManager.BindScatter();
                _framebufferManager.ClearScatter();
                CurrentContext.UseLinearOutput = true;
                CurrentContext.IsScatterPass = true;
                UpdateContextHash(CurrentContext);
                foreach (PartRenderEntry entry in _scatterEntries) {
                    RenderSingleEntry(entry, camera);
                }
                CurrentContext.UseLinearOutput = false;
                CurrentContext.IsScatterPass = false;
                UpdateContextHash(CurrentContext);
                _framebufferManager.UnbindFramebuffer();
            }

            // 2. Opaque pass
            if (_opaqueEntries.Count > 0) {
                RenderOpaqueBatched(camera);
            }

            // 注意：Transmission FBO 捕获在 RenderTransparentPass(drawOrder 150) 执行
            // SC 天空 drawOrder 5，水面 drawOrder ~100，都在 150 之前
        }

        bool _transmissionFboCaptured;
        bool _transparentRendered;

        public override void RenderTransparentPass(Camera camera, bool underwater) {
            // drawOrder 150 被调用两次(underwater=false/true)，只在第一次执行全部透明渲染
            if (_transparentRendered) {
                return;
            }
            _transparentRendered = true;

            // Transmission FBO 捕获（drawOrder 150 时天空和水面都已渲染）
            if (_hasTransmissionThisFrame && !_transmissionFboCaptured) {
                _transmissionFboCaptured = true;
                _framebufferManager.EnsureTransmissionFramebuffer();
                _framebufferManager.Transmission.BlitFromBackbuffer(Display.Viewport.Width, Display.Viewport.Height);
                _framebufferManager.GenerateTransmissionMipmap();
            }

            // 合并 before/after water 列表到独立列表，统一排序渲染
            _allTransparentEntries.AddRange(_transparentBeforeWater);
            _allTransparentEntries.AddRange(_transparentAfterWater);
            List<PartRenderEntry> entries = _allTransparentEntries;
            if (entries.Count == 0) {
                return;
            }

            // 计算深度用于 back-to-front 排序
            Matrix viewMatrix = camera.ViewMatrix;
            for (int i = 0; i < entries.Count; i++) {
                PartRenderEntry entry = entries[i];
                Vector3 center = entry.Mesh.BoundingBox.Center();
                Matrix boneTransform = GetBoneTransformForEntry(entry);
                Vector3 worldCenter = Vector3.Transform(center, boneTransform);
                Vector3 viewPos = Vector3.Transform(worldCenter, viewMatrix);
                entry.Depth = viewPos.Z;
                entries[i] = entry;
            }
            entries.Sort(BackToFrontComparison);

            // 渲染
            foreach (PartRenderEntry entry in entries) {
                RenderSingleEntry(entry, camera);
            }
        }

        #endregion

        #region Core Rendering

        void RenderSingleEntry(PartRenderEntry entry, Camera camera) {
            if (entry.Part == null) {
                return;
            }
            ComponentModel cm = entry.ModelData.ComponentModel;
            Model model = cm.Model;
            bool hasSkin = model?.HasSkin == true;

            // 蒙皮：计算 joint matrices
            if (hasSkin) {
                EnsureJointTexture(model);
                SubsystemModelsRenderer smr = _subsystemModelsRenderer;
                Matrix invertedView = camera.InvertedViewMatrix;
                int jointCount = smr.CalculateJointMatrices(cm, model, invertedView, smr.m_jointMatricesBuffer);
                smr.m_jointTexture.Update(smr.m_jointMatricesBuffer.AsSpan(0, jointCount));
            }
            ModelMaterial effectiveMaterial = GetEffectiveMaterial(entry);
            SetHasPunctualLight(GetCelestialBodyVisible(entry.ModelData) || _collectedLights.Count > 0);
            Shader shader;
            if (hasSkin) {
                shader = GetOrCreateShader(entry.Mesh, effectiveMaterial, CurrentContext);
            }
            else {
                shader = GetOrCreateInstancedShader(entry.Mesh, effectiveMaterial, CurrentContext, entry.TextureOverride != null);
            }
            if (shader == null) {
                return;
            }
            _currentInstanceShader = shader;
            shader.PrepareForDrawing();
            GLWrapper.UseProgram(shader.m_program);
            SetPerFrameUniforms(shader, entry.ModelData);

            // 变换矩阵
            if (hasSkin) {
                UpdateRenderStateUBO(CurrentContext.Wvp, CurrentContext.CameraView);
                BindJointTexture(_subsystemModelsRenderer.m_jointTexture, shader);
                SetupMorphTargets(entry.Part, shader);
            }
            else {
                UpdateRenderStateUBOForInstancing();
                Matrix4x4 worldMatrix = GetWorldMatrixForEntry(entry);
                _instanceMatrices[0] = worldMatrix;
                _instanceLightData[0] = new Vector2(entry.ModelData.Light, GetCelestialBodyVisible(entry.ModelData) ? 1f : 0f);
                UploadInstanceData(_instanceMatrices, 1);
                UploadInstanceLightData(_instanceLightData, 1);
                SetupInstanceAttributes();
            }
            UpdateMaterialUBOs(effectiveMaterial, false);
            UpdateUVTransformUBO(effectiveMaterial);

            // 纹理
            BindTexturesForEntry(entry, effectiveMaterial, shader);
            if (IblSampler != null
                && CurrentContext.UseIBL) {
                BindIBLTextures();
            }

            // GL 状态
            SetupDepthState(effectiveMaterial);
            bool isNegScale = GetBoneTransformForEntry(entry).Determinant() < 0f;
            SetupCullMode(effectiveMaterial, isNegScale);
            SetupBlendMode(effectiveMaterial, CurrentContext);
            SetupTransmissionUniforms(effectiveMaterial, shader);
            SetupVolumeScatterUniforms(effectiveMaterial, shader);

            // 绘制
            if (hasSkin) {
                DrawMeshPart(entry.Part);
            }
            else {
                DrawMeshPartInstanced(entry.Part, 1);
                DisableInstanceAttributes();
            }
        }

        void RenderOpaqueBatched(Camera camera) {
            // 分离蒙皮和非蒙皮
            _skinnedOpaqueEntries.Clear();
            foreach (PartRenderEntry e in _opaqueEntries) {
                if (e.ModelData.ComponentModel.Model?.HasSkin == true) {
                    _skinnedOpaqueEntries.Add(e);
                }
            }

            // 非蒙皮：按 (Mesh, Material, TextureOverride) 分组实例化渲染
            _instanceGroups.Clear();
            foreach (PartRenderEntry e in _opaqueEntries) {
                if (e.ModelData.ComponentModel.Model?.HasSkin == true) {
                    continue;
                }
                (ModelMesh, ModelMaterial, Texture2D) key = (e.Mesh, e.Material, e.TextureOverride);
                if (!_instanceGroups.TryGetValue(key, out List<PartRenderEntry> list)) {
                    list = [];
                    _instanceGroups[key] = list;
                }
                list.Add(e);
            }
            foreach (KeyValuePair<(ModelMesh, ModelMaterial, Texture2D), List<PartRenderEntry>> kvp in _instanceGroups) {
                (ModelMesh mesh, ModelMaterial material, Texture2D textureOverride) = kvp.Key;
                List<PartRenderEntry> groupEntries = kvp.Value;
                if (mesh == null) {
                    continue;
                }
                ModelMaterial effectiveMaterial;
                if (material != null) {
                    effectiveMaterial = material;
                }
                else if (textureOverride != null) {
                    effectiveMaterial = textureOverride is RenderTarget2D ? DefaultDielectricBlendMaterial : DefaultDielectricMaterial;
                }
                else {
                    effectiveMaterial = DefaultDielectricMaterial;
                }
                // 动态设置 HasPunctualLight：有太阳/月亮或有全局 glTF 灯光时启用 USE_PUNCTUAL
                bool hasLights = _collectedLights.Count > 0;
                if (!hasLights) {
                    for (int gi = 0; gi < groupEntries.Count; gi++) {
                        if (GetCelestialBodyVisible(groupEntries[gi].ModelData)) { hasLights = true; break; }
                    }
                }
                SetHasPunctualLight(hasLights);
                Shader shader = GetOrCreateInstancedShader(mesh, effectiveMaterial, CurrentContext, textureOverride != null);
                if (shader == null) {
                    continue;
                }
                _currentInstanceShader = shader;
                shader.PrepareForDrawing();
                GLWrapper.UseProgram(shader.m_program);
                SetPerFrameUniformsBatch(shader, groupEntries);
                UpdateRenderStateUBOForInstancing();
                UpdateLightsUBO(1f);
                UpdateMaterialUBOs(effectiveMaterial, false);
                UpdateUVTransformUBO(effectiveMaterial);
                Model model = groupEntries[0].ModelData.ComponentModel.Model;
                if (textureOverride != null) {
                    MaterialTextureBinder.BindTexture2D(textureOverride, MaterialTextureSlot.BaseColor);
                }
                else if (model != null
                    && material != null) {
                    BindMaterialTextures(model, material, shader, null);
                }
                // sampler uniform locations 必须在每次 shader bind 后设置，
                // 否则 IBL 等纹理 slot 会指向错误的 texture unit
                MaterialTextureBinder.SetTextureSlotUniforms(shader);
                if (IblSampler != null
                    && CurrentContext.UseIBL) {
                    BindIBLTextures();
                }
                // 检测 glTF EXT_mesh_gpu_instancing（用 entry 的 Part 而非 mesh.MeshParts[0]）
                ModelMeshPart firstPart = groupEntries[0].Part;
                bool hasGltfInstancing = firstPart != null
                    && firstPart.InstanceCount > 0
                    && firstPart.InstanceMatrices != null;

                if (hasGltfInstancing) {
                    // glTF 实例化：每个 entry 贡献 N 个矩阵（同节点所有 primitive 共享引用）
                    int instancesPerEntry = firstPart.InstanceCount;
                    var gltfMatrices = firstPart.InstanceMatrices;
                    int entryIdx = 0;
                    int instanceIdx = 0;

                    while (entryIdx < groupEntries.Count) {
                        int posCount = 0, negCount = 0;
                        while (entryIdx < groupEntries.Count && posCount + negCount < MaxInstancesPerBatch) {
                            PartRenderEntry e = groupEntries[entryIdx];
                            Matrix4x4 worldMatrix = GetWorldMatrixForEntry(e);
                            Vector2 light = new(e.ModelData.Light, GetCelestialBodyVisible(e.ModelData) ? 1f : 0f);

                            while (instanceIdx < instancesPerEntry && posCount + negCount < MaxInstancesPerBatch) {
                                // gltfLocal 先于 world 变换（行向量约定：v * gltfLocal * world）
                                Matrix4x4 instMatrix = gltfMatrices[instanceIdx] * worldMatrix;
                                Matrix4x4.Decompose(instMatrix, out System.Numerics.Vector3 s, out _, out _);
                                bool isNeg = s.X < 0 ^ s.Y < 0 ^ s.Z < 0;
                                if (isNeg) {
                                    _instanceMatrices[MaxInstancesPerBatch - 1 - negCount] = instMatrix;
                                    _instanceLightData[MaxInstancesPerBatch - 1 - negCount] = light;
                                    negCount++;
                                }
                                else {
                                    _instanceMatrices[posCount] = instMatrix;
                                    _instanceLightData[posCount] = light;
                                    posCount++;
                                }
                                instanceIdx++;
                            }

                            if (instanceIdx >= instancesPerEntry) {
                                instanceIdx = 0;
                                entryIdx++;
                            }
                            else {
                                break;
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
                else {
                    // 普通实例化：每个 entry 贡献 1 个矩阵
                    for (int offset = 0; offset < groupEntries.Count; offset += MaxInstancesPerBatch) {
                        int count = Math.Min(MaxInstancesPerBatch, groupEntries.Count - offset);
                        int posCount = 0, negCount = 0;
                        for (int i = 0; i < count; i++) {
                            PartRenderEntry e = groupEntries[offset + i];
                            Matrix4x4 worldMatrix = GetWorldMatrixForEntry(e);
                            Vector2 light = new(e.ModelData.Light, GetCelestialBodyVisible(e.ModelData) ? 1f : 0f);
                            Matrix engineMatrix = GetBoneTransformForEntry(e);
                            if (engineMatrix.Determinant() < 0f) {
                                _instanceMatrices[MaxInstancesPerBatch - 1 - negCount] = worldMatrix;
                                _instanceLightData[MaxInstancesPerBatch - 1 - negCount] = light;
                                negCount++;
                            }
                            else {
                                _instanceMatrices[posCount] = worldMatrix;
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
            }

            // 蒙皮：逐个渲染
            foreach (PartRenderEntry entry in _skinnedOpaqueEntries) {
                RenderSingleEntry(entry, camera);
            }
        }

        void DrawInstanceBatch(ModelMesh mesh, ModelMaterial material, int count, bool isNegativeScale) {
            UploadInstanceData(_instanceMatrices, count);
            UploadInstanceLightData(_instanceLightData, count);
            SetupInstanceAttributes();
            SetupDepthState(material);
            SetupCullMode(material, isNegativeScale);
            SetupBlendMode(material, CurrentContext);
            SetupTransmissionUniforms(material, _currentInstanceShader);
            SetupVolumeScatterUniforms(material, _currentInstanceShader);
            DrawMeshInstanced(mesh, count);
            DisableInstanceAttributes();
        }

        /// <summary>
        /// 绘制单个 mesh part 使用实例化（用于非蒙皮单 part 渲染）
        /// </summary>
        void DrawMeshPartInstanced(ModelMeshPart part, int instanceCount) {
            if (part?.VertexBuffer == null
                || part.IndexBuffer == null) {
                return;
            }
            GLWrapper.ApplyViewportScissor(Display.Viewport, Display.ScissorRectangle, Display.RasterizerState.ScissorTestEnable);
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, part.VertexBuffer.m_buffer);
            GLWrapper.BindBuffer(BufferTargetARB.ElementArrayBuffer, part.IndexBuffer.m_buffer);
            SetupVertexAttributes(part.VertexBuffer.VertexDeclaration);
            unsafe {
                IntPtr indexOffset = new(part.StartIndex * part.IndexBuffer.IndexFormat.GetSize());
                GLWrapper.GL.DrawElementsInstanced(
                    GLWrapper.TranslatePrimitiveType(part.PrimitiveType),
                    (uint)part.IndicesCount,
                    GLWrapper.TranslateIndexFormat(part.IndexBuffer.IndexFormat),
                    indexOffset.ToPointer(),
                    (uint)instanceCount
                );
            }
        }

        #endregion

        #region Helpers

        static Matrix GetBoneTransformForEntry(PartRenderEntry entry) {
            ComponentModel cm = entry.ModelData.ComponentModel;
            int boneIndex = entry.Mesh.ParentBone?.Index ?? 0;
            if (boneIndex < cm.AbsoluteBoneTransformsForCamera.Length) {
                return cm.AbsoluteBoneTransformsForCamera[boneIndex];
            }
            return Matrix.Identity;
        }

        static Matrix4x4 GetWorldMatrixForEntry(PartRenderEntry entry) {
            return GetBoneTransformForEntry(entry);
        }

        ModelMaterial GetEffectiveMaterial(PartRenderEntry entry) {
            if (entry.Material != null) {
                return entry.Material;
            }
            if (entry.TextureOverride != null) {
                return entry.TextureOverride is RenderTarget2D ? DefaultDielectricBlendMaterial : DefaultDielectricMaterial;
            }
            return DefaultDielectricMaterial;
        }

        void EnsureJointTexture(Model model) {
            SubsystemModelsRenderer smr = _subsystemModelsRenderer;
            int jointCount = Math.Min(model.Skin.JointCount, SubsystemModelsRenderer.MaxJointsCount);
            if (smr.m_jointTexture == null
                || smr.m_jointTexture.MaxJointCount < jointCount) {
                smr.m_jointTexture?.Dispose();
                smr.m_jointTexture = new JointTexture(jointCount);
            }
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
            UpdateLightsUBO(modelData.Light);
        }

        void SetPerFrameUniformsBatch(Shader shader, List<PartRenderEntry> entries) {
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
            else if (entry.Material != null) {
                Model model = entry.ModelData.ComponentModel?.Model;
                if (model != null) {
                    BindMaterialTextures(model, entry.Material, shader, null);
                }
            }
            // sampler uniform locations 必须在每次 shader bind 后设置，
            // 否则 IBL 等纹理 slot 会指向错误的 texture unit
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
            if (material == null) return;
            int extensionFlags = (int)MaterialUboBuilder.BuildExtensionFlags(material);
            if (LastMaterial != material || LastMaterialVersion != material.Version) {
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

        #endregion

        #region Shader Compilation

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
            bool hasTextureOverride = false) {
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
            if (!isInstanced
                && context.EnableMorphing
                && HasMorphTargetData(mesh)) {
                AddMorphTargetDefines(defines, mesh);
            }
            ModelAlphaMode alphaMode = material?.AlphaMode ?? ModelAlphaMode.Opaque;
            defines.AddRaw($"ALPHAMODE {(int)alphaMode}");
            string fragShader = context.IsScatterPass ? "scatter.frag" :
                material?.SpecularGlossiness?.IsEnabled == true ? "specular_glossiness.frag" : "pbr.frag";
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

        #endregion

        #region Transmission / Scatter Uniforms

        void SetupTransmissionUniforms(ModelMaterial material, Shader shader) {
            if (material?.Transmission?.IsEnabled != true) {
                return;
            }
            if (!_framebufferManager.HasTransmissionFramebuffer) {
                return;
            }
            int programHandle = shader.m_program;
            if (!_transmissionSamplerLocCache.TryGetValue(programHandle, out int samplerLoc)) {
                samplerLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_TransmissionFramebufferSampler");
                _transmissionSamplerLocCache[programHandle] = samplerLoc;
            }
            if (samplerLoc >= 0) {
                GLWrapper.GL.Uniform1(samplerLoc, (int)MaterialTextureSlot.TransmissionFramebuffer);
            }
            if (!_transmissionSizeLocCache.TryGetValue(programHandle, out (int sizeLoc, int screenLoc) locs)) {
                locs.sizeLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_TransmissionFramebufferSize");
                locs.screenLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_ScreenSize");
                _transmissionSizeLocCache[programHandle] = locs;
            }
            if (locs.sizeLoc >= 0) {
                GLWrapper.GL.Uniform2(locs.sizeLoc, _framebufferManager.Width, _framebufferManager.Height);
            }
            if (locs.screenLoc >= 0) {
                GLWrapper.GL.Uniform2(locs.screenLoc, _framebufferManager.Width, _framebufferManager.Height);
            }
            _framebufferManager.BindTransmissionTexture();
        }

        void SetupVolumeScatterUniforms(ModelMaterial material, Shader shader) {
            if (material?.VolumeScatter?.IsEnabled != true) {
                return;
            }
            VolumeScatterData scatterData = new() {
                MultiScatterColor = new Vector4(material.VolumeScatter.MultiscatterColor, 0f),
                MinRadius = VolumeScatterExtension.ScatterMinRadius,
                MaterialID = CurrentContext.IsScatterPass ? 1 : 0,
                FramebufferWidth = _framebufferManager.Width,
                FramebufferHeight = _framebufferManager.Height
            };
            _volumeScatterUBO.Update(ref scatterData);
            SetScatterSamplesUniformsOnce(shader);
            if (!CurrentContext.IsScatterPass
                && _framebufferManager.HasScatterFramebuffer) {
                int programHandle = shader.m_program;
                if (!_scatterSamplerLocCache.TryGetValue(programHandle, out int samplerLoc)) {
                    samplerLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_ScatterFramebufferSampler");
                    _scatterSamplerLocCache[programHandle] = samplerLoc;
                }
                if (samplerLoc >= 0) {
                    GLWrapper.GL.Uniform1(samplerLoc, (int)MaterialTextureSlot.ScatterFramebuffer);
                }
                if (!_scatterDepthSamplerLocCache.TryGetValue(programHandle, out int depthLoc)) {
                    depthLoc = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_ScatterDepthFramebufferSampler");
                    _scatterDepthSamplerLocCache[programHandle] = depthLoc;
                }
                if (depthLoc >= 0) {
                    GLWrapper.GL.Uniform1(depthLoc, (int)MaterialTextureSlot.ScatterDepthFramebuffer);
                }
                _framebufferManager.BindScatterTextures();
            }
        }

        void SetScatterSamplesUniformsOnce(Shader shader) {
            if (_scatterSamplesSetShaders.Contains(shader.m_program)) {
                return;
            }
            float[] samples = VolumeScatterExtension.ScatterSamples;
            if (samples == null) {
                return;
            }
            int sampleCount = samples.Length / 3;
            int programHandle = shader.m_program;
            for (int i = 0; i < sampleCount; i++) {
                int idx = i * 3;
                int loc = GLWrapper.GL.GetUniformLocation((uint)programHandle, $"u_ScatterSamples[{i}]");
                if (loc >= 0) {
                    GLWrapper.GL.Uniform3(loc, samples[idx], samples[idx + 1], samples[idx + 2]);
                }
            }
            _scatterSamplesSetShaders.Add(programHandle);
        }

        #endregion

        #region Celestial Body Visibility

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

        #endregion
    }
}