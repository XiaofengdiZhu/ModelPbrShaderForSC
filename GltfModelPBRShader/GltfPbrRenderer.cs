using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Engine;
using Engine.Animation;
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
    public partial class GltfPbrRenderer : AdvancedMeshRenderer {
        static readonly ModelMaterial DefaultDielectricMaterial = new() {
            MetallicFactor = 0f, RoughnessFactor = 1.0f, BaseColorFactor = Vector4.One
        };

        static readonly Comparison<PartRenderEntry> BackToFrontComparison = (a, b) => b.Depth.CompareTo(a.Depth);

        static readonly ModelMaterial DefaultDielectricMaskMaterial = new() {
            MetallicFactor = 0f, RoughnessFactor = 1.0f, BaseColorFactor = Vector4.One, AlphaMode = ModelAlphaMode.Mask, AlphaCutoff = 0.01f
        };

        static readonly int InstancedHashSalt = "__INSTANCED__".GetHashCode();
        readonly HashSet<Model> _activeSkinnedModels = [];
        readonly List<PartRenderEntry> _allTransparentEntries = [];
        readonly PbrFramebufferManager _framebufferManager = new();
        readonly Dictionary<(ModelMesh, ModelMaterial, Texture2D), List<PartRenderEntry>> _instanceGroups = new();
        readonly Dictionary<Model, JointTexture> _jointTextures = new();

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
        readonly List<Model> _staleJointModels = [];
        readonly Dictionary<int, int> _transmissionSamplerLocCache = [];
        readonly Dictionary<int, (int sizeLoc, int screenLoc)> _transmissionSizeLocCache = [];
        readonly List<PartRenderEntry> _transparentAfterWater = [];
        readonly List<PartRenderEntry> _transparentBeforeWater = [];
        readonly UniformBuffer<VolumeScatterData> _volumeScatterUBO = new(5);
        List<SubsystemModelsRenderer.ModelData> _allModels;
        Shader _currentInstanceShader;
        bool _hasTransmissionThisFrame;
        bool _shadersLoaded;

        // 动态 IBL：每玩家环境数据
        public readonly Dictionary<int, PlayerEnvironmentData> PlayerEnvironments = new();
        EnvironmentCapture _environmentCapture;
        PlayerEnvironmentData _currentPlayerData;

        // 捕获常量
        const float CaptureDistanceThreshold = 1.5f;      // 触发捕获的移动距离（米）
        const float CaptureTimeThresholdNear = 1f;        // 移动后的最短捕获间隔（秒）
        const float CaptureTimeThresholdFar = 3f;         // 静止时的捕获间隔（秒）
        const int EnvironmentMapFaceSize = 256;             // Cubemap 每面分辨率

        // 是否启用动态 IBL（默认 false，由 SubsystemGltfModelPBRShader 启用）
        public bool DynamicIblEnabled { get; set; }

        public IblSampler IblSampler { get; private set; }

        public override bool HasIBL => IblSampler != null;

        public Dictionary<SubsystemModelsRenderer.ModelData, CelestialBodyCacheEntry> CelestialBodyCache { get; } = new();

        /// <summary>
        /// 初始化动态 IBL 系统
        /// </summary>
        /// <param name="subsystemTerrain">地形子系统</param>
        /// <param name="subsystemSky">天空子系统</param>
        public void InitializeDynamicIbl(SubsystemTerrain subsystemTerrain, SubsystemSky subsystemSky) {
            _environmentCapture = new EnvironmentCapture();
            _environmentCapture.Initialize(subsystemTerrain, subsystemSky);
            DynamicIblEnabled = true;
            Log.Information("[glTF PBR Shader] Dynamic IBL initialized");
        }

        /// <summary>
        /// 获取或创建玩家环境数据
        /// </summary>
        /// <param name="camera">相机（用于获取玩家索引）</param>
        /// <returns>玩家环境数据</returns>
        public PlayerEnvironmentData GetOrCreatePlayerData(Camera camera) {
            // 从 camera.GameWidget.PlayerData 获取玩家索引
            int playerIndex = camera.GameWidget?.PlayerData?.PlayerIndex ?? 0;
            if (!PlayerEnvironments.TryGetValue(playerIndex, out PlayerEnvironmentData data)) {
                data = new PlayerEnvironmentData {
                    IblSampler = null, // 延迟到捕获时创建
                    LastCapturePosition = Vector3.Zero,
                    LastCaptureTime = 0,
                    CachedPlayerLight = 1f
                };
                PlayerEnvironments[playerIndex] = data;
                Log.Information($"[glTF PBR Shader] Created player environment data for player {playerIndex}");
            }
            return data;
        }

        /// <summary>
        /// 清理玩家环境数据（玩家断线时调用）
        /// </summary>
        /// <param name="playerIndex">玩家索引</param>
        public void CleanupPlayerData(int playerIndex) {
            if (PlayerEnvironments.TryGetValue(playerIndex, out PlayerEnvironmentData data)) {
                data.Dispose();
                PlayerEnvironments.Remove(playerIndex);
                Log.Information($"[glTF PBR Shader] Cleaned up player environment data for player {playerIndex}");
            }
        }

        /// <summary>
        /// 检查是否应该捕获环境贴图
        /// </summary>
        bool ShouldCapture(PlayerEnvironmentData playerData, Vector3 currentPosition) {
            float distanceMoved = Vector3.Distance(currentPosition, playerData.LastCapturePosition);
            double timeSinceCapture = Time.FrameStartTime - playerData.LastCaptureTime;

            bool result;
            if (distanceMoved > CaptureDistanceThreshold) {
                result = timeSinceCapture > CaptureTimeThresholdNear;
            }
            else {
                result = timeSinceCapture > CaptureTimeThresholdFar;
            }

            // 首次捕获（LastCaptureTime == 0）
            if (playerData.LastCaptureTime == 0) {
                // 检查地形是否已加载（至少有一个区块）
                if (_subsystemTerrain?.Terrain?.AllocatedChunks?.Length == 0) {
                    Log.Information("[glTF PBR Shader] Skipping first capture - terrain not loaded");
                    return false;
                }
                result = true;
            }

            return result;
        }

        /// <summary>
        /// 更新玩家光照缓存
        /// </summary>
        void UpdatePlayerLightCache(PlayerEnvironmentData playerData, Vector3 playerEyePosition) {
            float? light = LightingManager.CalculateSmoothLight(_subsystemTerrain, playerEyePosition);
            playerData.CachedPlayerLight = light ?? _subsystemSky.SkyLightIntensity;
        }

        /// <summary>
        /// 执行环境贴图捕获
        /// </summary>
        void CaptureEnvironment(Camera camera, PlayerEnvironmentData playerData, Vector3 capturePosition) {
            if (_environmentCapture == null) {
                Log.Warning("[glTF PBR Shader] EnvironmentCapture is null, skipping capture");
                return;
            }

            try {
                int faceSize = EnvironmentMapFaceSize;

                Log.Information($"[glTF PBR Shader] Capturing environment at {capturePosition}");

                // 捕获环境贴图到 Cubemap
                CubemapTexture cubemapTexture = _environmentCapture.CaptureEnvironment(camera.GameWidget, capturePosition, faceSize);

                // 处理为 IBL 采样器
                playerData.IblSampler?.Dispose();
                playerData.IblSampler = new IblSampler();
                playerData.IblSampler.Process(cubemapTexture, faceSize);
                playerData.MipCount = playerData.IblSampler.MipCount;

                // 更新捕获状态
                playerData.LastCapturePosition = capturePosition;
                playerData.LastCaptureTime = Time.FrameStartTime;

                Log.Information($"[glTF PBR Shader] Environment capture complete, MipCount={playerData.MipCount}");
            }
            catch (Exception ex) {
                Log.Error($"[glTF PBR Shader] Environment capture failed: {ex.Message}\n{ex.StackTrace}");
            }
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
            AddShader(shaders, "GltfModelPbrShaders/", "ibl_filtering.frag");
            ShaderCache.LoadShaderSources(shaders, basePath);
            AnimationPlayer.MorphWeightAnimationEnabled = true;
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
            GLWrapper.GL.BindAttribLocation(program, InstanceIblStrengthAttribLocation, "a_instance_ibl_strength");
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

        protected override void PrepareCustomQueues(List<SubsystemModelsRenderer.ModelData> allModels) {
            _opaqueEntries.Clear();
            _scatterEntries.Clear();
            _transparentBeforeWater.Clear();
            _transparentAfterWater.Clear();
            _allTransparentEntries.Clear();
            _hasTransmissionThisFrame = false;
            _transmissionFboCaptured = false;
            _transparentRendered = false;
            _activeSkinnedModels.Clear();
            Viewport vp = Display.Viewport;
            _framebufferManager.SetSize(vp.Width, vp.Height);
            foreach (SubsystemModelsRenderer.ModelData md in allModels) {
                ComponentModel cm = md.ComponentModel;
                Model model = cm.Model;
                if (model == null) {
                    continue;
                }
                if (model.HasSkin) {
                    _activeSkinnedModels.Add(model);
                }
                bool isUnderwater = cm.RenderingMode == ModelRenderingMode.TransparentAfterWater;
                Texture2D textureOverride = cm.TextureOverride;
                foreach (int meshIndex in cm.MeshDrawOrders) {
                    if (meshIndex < 0
                        || meshIndex >= model.Meshes.Count) {
                        continue;
                    }
                    ModelMesh mesh = model.Meshes[meshIndex];
                    if (!mesh.IsVisible) {
                        continue;
                    }
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
            // 清理已移除模型的 JointTexture，释放 GPU 资源
            if (_jointTextures.Count > 0) {
                foreach (Model model in _jointTextures.Keys) {
                    if (!_activeSkinnedModels.Contains(model)) {
                        _staleJointModels.Add(model);
                    }
                }
                foreach (Model model in _staleJointModels) {
                    _jointTextures[model].Dispose();
                    _jointTextures.Remove(model);
                }
                _staleJointModels.Clear();
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
            foreach (JointTexture jt in _jointTextures.Values) {
                jt.Dispose();
            }
            _jointTextures.Clear();
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
            MaterialTextureBinder.ClearAllCaches();
            base.Dispose();
        }

        public override void BeginFrame(Camera camera, List<SubsystemModelsRenderer.ModelData> allModels) {
            _allModels = allModels;

            // 动态 IBL：获取当前玩家数据并触发捕获
            if (DynamicIblEnabled && _environmentCapture != null) {
                _currentPlayerData = GetOrCreatePlayerData(camera);
                Vector3 capturePosition = camera.ViewPosition;

                // 更新玩家光照缓存
                UpdatePlayerLightCache(_currentPlayerData, capturePosition);

                // 检查是否需要捕获环境贴图
                if (ShouldCapture(_currentPlayerData, capturePosition)) {
                    CaptureEnvironment(camera, _currentPlayerData, capturePosition);
                }

                // 使用当前玩家的 IBL 采样器（如果已创建）
                if (_currentPlayerData.IblSampler != null) {
                    IblSampler = _currentPlayerData.IblSampler;
                    MipCount = _currentPlayerData.MipCount;
                }
            }

            base.BeginFrame(camera, allModels);
            PrepareCustomQueues(allModels);
        }

        public struct CelestialBodyCacheEntry {
            public bool Visible;
            public double Timestamp;
        }

        #region Render Passes

        public override void RenderOpaquePass() {
            // 1. Scatter pass
            if (_scatterEntries.Count > 0) {
                _framebufferManager.EnsureScatterFramebuffer();
                _framebufferManager.BindScatter();
                _framebufferManager.ClearScatter();
                CurrentContext.UseLinearOutput = true;
                CurrentContext.IsScatterPass = true;
                UpdateContextHash(CurrentContext);
                foreach (PartRenderEntry entry in _scatterEntries) {
                    RenderSingleEntry(entry);
                }
                CurrentContext.UseLinearOutput = false;
                CurrentContext.IsScatterPass = false;
                UpdateContextHash(CurrentContext);
                _framebufferManager.UnbindFramebuffer();
            }

            // 2. Opaque pass
            if (_opaqueEntries.Count > 0) {
                RenderOpaqueBatched();
            }

            // 3. Queue shadows + DrawExtras
            QueueShadowsAndDrawExtras();
        }

        bool _transmissionFboCaptured;
        bool _transparentRendered;

        public override void RenderTransparentPass(bool underwater) {
            // drawOrder 201 被调用两次(underwater=false/true)，只在第一次执行全部透明渲染
            if (_transparentRendered) {
                return;
            }
            _transparentRendered = true;

            // 合并 before/after water 列表到独立列表，统一排序渲染
            _allTransparentEntries.AddRange(_transparentBeforeWater);
            _allTransparentEntries.AddRange(_transparentAfterWater);
            List<PartRenderEntry> entries = _allTransparentEntries;

            // 计算 depth 用于排序
            Matrix viewMatrix = _camera.ViewMatrix;
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

            // 在 Transmission FBO 捕获前，先渲染非 Transmission 的透明物体（AlphaBlend、Scatter）
            // 这样 blit 时 alpha blend 内容会被包含在 transmission 屏幕空间查找纹理中
            if (_hasTransmissionThisFrame) {
                foreach (PartRenderEntry entry in entries) {
                    if (entry.QueueType != PartRenderQueue.Transmission) {
                        RenderSingleEntry(entry);
                    }
                }
            }

            // Transmission FBO 捕获（此时 opaque + alpha blend + 阴影贴花都已在 backbuffer 中）
            if (_hasTransmissionThisFrame && !_transmissionFboCaptured) {
                _transmissionFboCaptured = true;
                _framebufferManager.EnsureTransmissionFramebuffer();
                _framebufferManager.Transmission.BlitFromBackbuffer(Display.Viewport.Width, Display.Viewport.Height);
                _framebufferManager.GenerateTransmissionMipmap();
            }
            if (entries.Count == 0) {
                return;
            }

            // 渲染
            if (_hasTransmissionThisFrame) {
                // 非 Transmission 已渲染，只渲染 Transmission
                foreach (PartRenderEntry entry in entries) {
                    if (entry.QueueType == PartRenderQueue.Transmission) {
                        RenderSingleEntry(entry);
                    }
                }
            }
            else {
                foreach (PartRenderEntry entry in entries) {
                    RenderSingleEntry(entry);
                }
            }
        }

        #endregion

        #region Core Rendering

        void RenderSingleEntry(PartRenderEntry entry) {
            if (entry.Part == null) {
                return;
            }
            ComponentModel cm = entry.ModelData.ComponentModel;
            Model model = cm.Model;
            bool hasSkin = model?.HasSkin == true;

            // 蒙皮：计算 joint matrices
            JointTexture jointTex = null;
            int jointCount = 0;
            if (hasSkin) {
                jointTex = GetOrCreateJointTexture(model);
                SubsystemModelsRenderer smr = _subsystemModelsRenderer;
                Matrix invertedView = _camera.InvertedViewMatrix;
                jointCount = smr.CalculateJointMatrices(cm, model, invertedView, smr.m_jointMatricesBuffer);
                jointTex.Update(smr.m_jointMatricesBuffer.AsSpan(0, jointCount));
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
            }
            else {
                UpdateRenderStateUBOForInstancing();
                Matrix4x4 worldMatrix = GetWorldMatrixForEntry(entry);
                _instanceMatrices[0] = worldMatrix;
                _instanceLightData[0] = new Vector2(entry.ModelData.Light, GetCelestialBodyVisible(entry.ModelData) ? 1f : 0f);
                _instanceIblStrengthData[0] = CalculateIblStrength(entry.ModelData, worldMatrix);
                UploadInstanceData(_instanceMatrices, 1);
                UploadInstanceLightData(_instanceLightData, 1);
                UploadInstanceIblStrengthData(_instanceIblStrengthData, 1);
                SetupInstanceAttributes();
            }
            SetupMorphTargets(entry.Part, shader);
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
                BindJointTexture(jointTex, shader);
                DrawMeshPart(entry.Part);
            }
            else {
                DrawMeshPartInstanced(entry.Part, 1);
                DisableInstanceAttributes();
            }
        }

        void RenderOpaqueBatched() {
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
                    effectiveMaterial = textureOverride is RenderTarget2D ? DefaultDielectricMaskMaterial : DefaultDielectricMaterial;
                }
                else {
                    effectiveMaterial = DefaultDielectricMaterial;
                }
                // 动态设置 HasPunctualLight：有太阳/月亮或有全局 glTF 灯光时启用 USE_PUNCTUAL
                bool hasLights = _collectedLights.Count > 0;
                if (!hasLights) {
                    for (int gi = 0; gi < groupEntries.Count; gi++) {
                        if (GetCelestialBodyVisible(groupEntries[gi].ModelData)) {
                            hasLights = true;
                            break;
                        }
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
                SetPerFrameUniformsBatch(shader);
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
                SetupMorphTargets(firstPart, shader);
                bool hasGltfInstancing = firstPart != null && firstPart.InstanceCount > 0 && firstPart.InstanceMatrices != null;
                if (hasGltfInstancing) {
                    // glTF 实例化：每个 entry 贡献 N 个矩阵（同节点所有 primitive 共享引用）
                    int instancesPerEntry = firstPart.InstanceCount;
                    Matrix4x4[] gltfMatrices = firstPart.InstanceMatrices;
                    int entryIdx = 0;
                    int instanceIdx = 0;
                    while (entryIdx < groupEntries.Count) {
                        int posCount = 0, negCount = 0;
                        while (entryIdx < groupEntries.Count
                            && posCount + negCount < MaxInstancesPerBatch) {
                            PartRenderEntry e = groupEntries[entryIdx];
                            Matrix4x4 worldMatrix = GetWorldMatrixForEntry(e);
                            Vector2 light = new(e.ModelData.Light, GetCelestialBodyVisible(e.ModelData) ? 1f : 0f);
                            float iblStrength = CalculateIblStrength(e.ModelData, worldMatrix);
                            while (instanceIdx < instancesPerEntry
                                && posCount + negCount < MaxInstancesPerBatch) {
                                // gltfLocal 先于 world 变换（行向量约定：v * gltfLocal * world）
                                Matrix4x4 instMatrix = gltfMatrices[instanceIdx] * worldMatrix;
                                bool isNeg = instMatrix.GetDeterminant() < 0f;
                                if (isNeg) {
                                    _instanceMatrices[MaxInstancesPerBatch - 1 - negCount] = instMatrix;
                                    _instanceLightData[MaxInstancesPerBatch - 1 - negCount] = light;
                                    _instanceIblStrengthData[MaxInstancesPerBatch - 1 - negCount] = iblStrength;
                                    negCount++;
                                }
                                else {
                                    _instanceMatrices[posCount] = instMatrix;
                                    _instanceLightData[posCount] = light;
                                    _instanceIblStrengthData[posCount] = iblStrength;
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
                                _instanceIblStrengthData[i] = _instanceIblStrengthData[MaxInstancesPerBatch - 1 - i];
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
                            float iblStrength = CalculateIblStrength(e.ModelData, worldMatrix);
                            Matrix engineMatrix = GetBoneTransformForEntry(e);
                            if (engineMatrix.Determinant() < 0f) {
                                _instanceMatrices[MaxInstancesPerBatch - 1 - negCount] = worldMatrix;
                                _instanceLightData[MaxInstancesPerBatch - 1 - negCount] = light;
                                _instanceIblStrengthData[MaxInstancesPerBatch - 1 - negCount] = iblStrength;
                                negCount++;
                            }
                            else {
                                _instanceMatrices[posCount] = worldMatrix;
                                _instanceLightData[posCount] = light;
                                _instanceIblStrengthData[posCount] = iblStrength;
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
                                _instanceIblStrengthData[i] = _instanceIblStrengthData[MaxInstancesPerBatch - 1 - i];
                            }
                            DrawInstanceBatch(mesh, effectiveMaterial, negCount, true);
                        }
                    }
                }
            }

            // 蒙皮：逐个渲染
            foreach (PartRenderEntry entry in _skinnedOpaqueEntries) {
                RenderSingleEntry(entry);
            }
        }

        void DrawInstanceBatch(ModelMesh mesh, ModelMaterial material, int count, bool isNegativeScale) {
            UploadInstanceData(_instanceMatrices, count);
            UploadInstanceLightData(_instanceLightData, count);
            UploadInstanceIblStrengthData(_instanceIblStrengthData, count);
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

        #region IBL Strength Calculation

        /// <summary>
        /// 计算 IBL 强度
        /// 基于模型光照计算 IBL 贡献强度
        /// </summary>
        float CalculateIblStrength(SubsystemModelsRenderer.ModelData modelData, Matrix4x4 worldMatrix) {
            if (IblSampler == null) {
                return 0f;
            }

            // 获取模型光照
            float modelLight = modelData.Light;
            if (modelLight <= 0f) {
                return 0f;
            }

            // 动态 IBL：使用玩家光照缓存计算强度比值
            float iblStrength;
            if (DynamicIblEnabled && _currentPlayerData != null) {
                float playerLight = _currentPlayerData.CachedPlayerLight;
                // 如果玩家光照无效，使用默认值
                if (playerLight <= 0f) {
                    playerLight = 1f;
                }

                // IBL 强度比值，开方以软化对比度
                float ratio = modelLight / playerLight;
                iblStrength = MathF.Sqrt(Math.Min(ratio, 1f));
            }
            else {
                // 静态 IBL：使用 EnvironmentStrength
                iblStrength = EnvironmentStrength;
            }

            return iblStrength;
        }

        #endregion
    }
}