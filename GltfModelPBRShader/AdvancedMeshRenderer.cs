using System;
using System.Collections.Generic;
using System.Numerics;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Shader = Engine.Graphics.Shader;
using Vector4 = System.Numerics.Vector4;
using Vector2 = Engine.Vector2;
using Vector3 = Engine.Vector3;

namespace Game {
    /// <summary>
    /// 高级网格渲染器基类
    /// 模组开发者继承此类实现自定义渲染（PBR、卡通渲染等）
    /// </summary>
    /// <remarks>
    /// 基类管理通用 UBO：Scene、RenderState、Lights、UVTransform
    /// 子类管理材质相关 UBO（如 PBR 的 MaterialCore、MaterialExtension）
    /// 子类需要实现：
    /// - LoadShaderSources(): 加载 .vert, .frag, .glsl 文件
    /// - SetupShaderCallbacks(): 设置 Attribute/UBO 绑定回调
    /// - CreateShaderVariant(): 构建着色器变体
    /// </remarks>
    public abstract partial class AdvancedMeshRenderer : ICustomModelRenderer {
        protected const int MaxInstancesPerBatch = 256;

        // Locations 8-11: instance model matrix (mat4), location 12: instance light data (vec2)
        protected const int InstanceLightAttribLocation = 12;

        /// <summary>
        /// 骨骼纹理纹理槽
        /// 注意：MaterialTextureSlot.MorphTargets = 30，JointTexture 使用 slot 31 避免冲突
        /// </summary>
        protected const int JointTextureSlot = 31;

        protected readonly Dictionary<int, int> _celestialBodyVisibleLocCache = [];
        readonly HashSet<ComponentModel> _collectedLightModels = [];
        protected readonly Dictionary<int, int> _glymulLocationCache = [];

        // Uniform location 缓存
        readonly Dictionary<int, int> _jointSamplerLocationCache = [];
        protected readonly Dictionary<int, int> _terrainLightLocCache = [];

        // 帧级光照数据（逐模型缩放时使用）
        Vector3 _baseLightColor;
        protected Camera _camera;
        protected List<CollectedLight> _collectedLights = [];

        // 缓存优化
        bool _instanceBufferCreated;
        protected Vector2[] _instanceLightData = new Vector2[MaxInstancesPerBatch];
        int _instanceLightVBO;
        protected Matrix4x4[] _instanceMatrices = new Matrix4x4[MaxInstancesPerBatch];

        // 实例化渲染
        int _instanceVBO;
        (bool useIBL, bool useLinearOutput, ToneMapMode toneMapMode, bool hasPunctualLight, DebugChannel debugChannel) _lastContextParams;

        // 子系统引用
        protected SubsystemModelsRenderer _subsystemModelsRenderer;
        protected SubsystemSky _subsystemSky;
        protected SubsystemTerrain _subsystemTerrain;
        protected SubsystemTimeOfDay _subsystemTimeOfDay;
        Vector3 _viewLightDir;
        protected RenderContext CurrentContext = new();
        protected int LastExtensionFlags;

        // 材质缓存状态（子类可访问）
        protected ModelMaterial LastMaterial;
        protected int LastMaterialVersion = -1;
        protected UniformBuffer<LightsData> LightsUBO;

        // 渲染状态
        protected RenderStateData RenderStateData;

        protected UniformBuffer<RenderStateData> RenderStateUBO;

        // 通用 UBO 实例（基类管理）
        protected UniformBuffer<SceneData> SceneUBO;
        protected bool UvTransformDirty = true;
        protected UniformBuffer<UVTransformData> UVTransformUBO;

        protected AdvancedMeshRenderer() {
            // 创建通用 UBO
            SceneUBO = new UniformBuffer<SceneData>(0);
            LightsUBO = new UniformBuffer<LightsData>(2);
            RenderStateUBO = new UniformBuffer<RenderStateData>(3);
            UVTransformUBO = new UniformBuffer<UVTransformData>(4);

            // 调用抽象方法让子类设置
            LoadShaderSources();
            SetupShaderCallbacks();
        }

        /// <summary>
        /// 当前视图投影矩阵
        /// </summary>
        public Matrix4x4 CurrentViewProjection { get; private set; }

        /// <summary>
        /// IBL 环境贴图强度（默认 1.0）
        /// </summary>
        public float EnvironmentStrength { get; set; } = 1.0f;

        /// <summary>
        /// 当前方向光是否在地平线以上（太阳/月亮可见）
        /// </summary>
        public bool IsDirectionalLightActive { get; private set; } = true;

        /// <summary>
        /// IBL mipmap 层数
        /// </summary>
        public int MipCount { get; set; }

        /// <summary>
        /// 是否有 IBL 环境贴图（子类重写）
        /// </summary>
        public abstract bool HasIBL { get; }

        /// <summary>
        /// 获取缓存的上下文 hash
        /// </summary>
        protected int CachedContextHash { get; private set; }

        /// <summary>
        /// 当前激活的方向光方向（世界空间）
        /// </summary>
        public Vector3 ActiveLightDirection { get; private set; } = Vector3.UnitY;

        public virtual void Initialize(SubsystemModelsRenderer subsystemModelsRenderer) {
            _subsystemModelsRenderer = subsystemModelsRenderer;
            _subsystemSky = _subsystemModelsRenderer.m_subsystemSky;
            _subsystemTimeOfDay = _subsystemModelsRenderer.m_subsystemTimeOfDay;
            _subsystemTerrain = _subsystemModelsRenderer.m_subsystemTerrain;
        }

        /// <summary>
        /// 开始帧渲染：准备队列 + 设置光源 + 更新 UBO
        /// </summary>
        public virtual void BeginFrame(Camera camera, List<SubsystemModelsRenderer.ModelData> allModels) {
            _camera = camera;
            // Build RenderContext from subsystems
            float timeOfDay = _subsystemTimeOfDay.TimeOfDay;
            float midday = _subsystemTimeOfDay.Midday;

            // Sun direction
            float sunAngle = MathF.PI * 2f * (timeOfDay - midday) + MathF.PI;
            float seasonAngle = _subsystemSky.CalculateSeasonAngle();
            Vector3 sunDir = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitY, Matrix.CreateRotationZ(-sunAngle) * Matrix.CreateRotationX(seasonAngle))
            );

            // Moon direction
            float moonAngle = sunAngle - MathF.PI;
            Vector3 moonDir = Vector3.Normalize(
                Vector3.Transform(Vector3.UnitY, Matrix.CreateRotationZ(-moonAngle) * Matrix.CreateRotationX(seasonAngle))
            );

            // Sun color with dawn/dusk glow
            float dawnGlow = Math.Max(_subsystemSky.CalculateDawnGlowIntensity(timeOfDay), _subsystemSky.CalculateDuskGlowIntensity(timeOfDay));
            float precipitationIntensity = _subsystemSky.m_subsystemWeather.PrecipitationIntensity;
            float precipitationDim = MathUtils.Lerp(1f, 0f, precipitationIntensity);
            Vector3 sunColor = Vector3.Lerp(new Vector3(1f, 1f, 1f), new Vector3(1f, 1f, 0.627f), dawnGlow) * precipitationDim;
            float skyIntensity = _subsystemSky.SkyLightIntensity;

            // Moonlight with moon phase
            float moonPhaseFactor = _subsystemSky.MoonPhase == 4 ? 0.0f : 0.15f;
            float moonIntensity = (1f - skyIntensity) * moonPhaseFactor;
            Vector3 moonColor = new Vector3(0.8f, 0.85f, 1.0f) * moonIntensity;

            // Select main light
            Vector3 lightDirection;
            Vector3 lightColor;
            if (skyIntensity >= moonIntensity) {
                lightDirection = sunDir;
                lightColor = sunColor * skyIntensity;
            }
            else {
                lightDirection = moonDir;
                lightColor = moonColor;
            }

            // IBL strength follows day/night cycle
            EnvironmentStrength = Math.Max(skyIntensity, 0.1f);
            ActiveLightDirection = lightDirection;
            IsDirectionalLightActive = lightDirection.Y <= 0f;
            CurrentContext.View = Matrix4x4.Identity;
            CurrentContext.Projection = camera.ProjectionMatrix;
            CurrentContext.CameraView = camera.ViewMatrix;
            CurrentContext.Wvp = camera.ViewMatrix * camera.ProjectionMatrix;
            CurrentContext.UseIBL = HasIBL;
            CurrentContext.UseLinearOutput = false;
            CurrentContext.IsScatterPass = false;
            CurrentContext.ToneMapMode = ToneMapMode.KhrPbrNeutral;
            CurrentContext.HasPunctualLight = false;
            CurrentContext.DebugChannel = DebugChannel.None;
            CurrentContext.EnableSkinning = false;
            CurrentContext.EnableMorphing = true;
            CurrentContext.LightDirection = lightDirection;
            CurrentContext.LightColor = lightColor;
            UpdateContextHash(CurrentContext);
            CurrentViewProjection = CurrentContext.View * CurrentContext.Projection;

            // 更新 SceneData UBO
            // SC 引擎的 ModelMatrix 含 ViewMatrix（AbsoluteBoneTransformsForCamera），
            // 所以 v_Position 和法线在 view space。
            // CameraPos 设为 view space 原点 (0,0,0)，使 v = normalize(-v_Position) 正确。
            // EnvRotation = transpose(mat3(CameraView))，将 view space 向量变换回 world space 采样 IBL。
            Matrix4x4 cameraView = CurrentContext.CameraView;
            SceneData sceneData = new() {
                CameraPos = new Vector4(0f, 0f, 0f, 1f),
                Exposure = 1f,
                EnvironmentStrength = EnvironmentStrength,
                MipCount = MipCount,
                EnvRotationCol0 = new Vector4(cameraView.M11, cameraView.M21, cameraView.M31, 0f),
                EnvRotationCol1 = new Vector4(cameraView.M12, cameraView.M22, cameraView.M32, 0f),
                EnvRotationCol2 = new Vector4(cameraView.M13, cameraView.M23, cameraView.M33, 0f)
            };
            SceneUBO.Update(ref sceneData);

            // 方向光：使用 CameraView 将世界空间光照方向变换到 view space
            _viewLightDir = Vector3.Normalize(Vector3.TransformNormal(CurrentContext.LightDirection, cameraView));
            _baseLightColor = CurrentContext.LightColor;
            UpdateLightsUBO(1f);

            // 重置材质缓存
            LastMaterial = null;
            LastMaterialVersion = -1;
            UvTransformDirty = true;
            MaterialTextureBinder.ResetFrameState();
        }

        public virtual void RenderOpaquePass() { }

        public virtual void RenderTransparentPass(bool underwater) { }

        public virtual void Dispose() {
            SceneUBO?.Dispose();
            LightsUBO?.Dispose();
            RenderStateUBO?.Dispose();
            UVTransformUBO?.Dispose();
            if (_instanceBufferCreated) {
                uint vbo = (uint)_instanceVBO;
                GLWrapper.GL.DeleteBuffers(1u, in vbo);
                uint lightVbo = (uint)_instanceLightVBO;
                GLWrapper.GL.DeleteBuffers(1u, in lightVbo);
                _instanceBufferCreated = false;
            }
            _jointSamplerLocationCache.Clear();
            _glymulLocationCache.Clear();
        }

        /// <summary>
        /// 准备自定义渲染队列（由子类在 BeginFrame 末尾调用）
        /// </summary>
        protected virtual void PrepareCustomQueues(List<SubsystemModelsRenderer.ModelData> allModels) { }

        /// <summary>
        /// 加载着色器源码（由模组实现）
        /// </summary>
        protected abstract void LoadShaderSources();

        /// <summary>
        /// 设置 Attribute/UBO 绑定回调（由模组实现）
        /// </summary>
        protected abstract void SetupShaderCallbacks();

        /// <summary>
        /// 创建着色器变体（由模组实现）
        /// </summary>
        protected abstract Shader CreateShaderVariant(ModelMesh mesh, ModelMaterial material, RenderContext context);

        /// <summary>
        /// 获取或创建着色器变体
        /// </summary>
        protected virtual Shader GetOrCreateShader(ModelMesh mesh, ModelMaterial material, RenderContext context) {
            // 计算材质 hash（子类可重写以优化）
            int materialHash = ComputeMaterialHash(material);
            int contextHash = CachedContextHash;

            // 尝试从缓存获取
            Shader shader = ShaderCache.TryGetShaderProgram(materialHash, contextHash);
            if (shader != null) {
                return shader;
            }

            // 创建新的着色器变体
            return CreateShaderVariant(mesh, material, context);
        }

        /// <summary>
        /// 计算材质 hash（子类可重写以优化）
        /// </summary>
        protected virtual int ComputeMaterialHash(ModelMaterial material) {
            unchecked {
                int hash = 17;
                if (material != null) {
                    hash = hash * 31 + material.AlphaMode.GetHashCode();
                    hash = hash * 31 + material.DoubleSided.GetHashCode();
                    hash = hash * 31 + (int)MaterialUboBuilder.BuildExtensionFlags(material);
                    hash = hash * 31 + (int)MaterialUboBuilder.BuildTextureFlags(material);
                }
                return hash;
            }
        }

        /// <summary>
        /// 更新 RenderState UBO（实例化模式）
        /// 只设置共享的投影矩阵，per-instance 变换由顶点属性提供
        /// </summary>
        protected void UpdateRenderStateUBOForInstancing() {
            RenderStateData.ViewProjectionMatrix = CurrentContext.Projection;
            RenderStateData.ModelMatrix = Matrix4x4.Identity;
            RenderStateData.ViewMatrix = Matrix4x4.Identity;
            RenderStateData.ProjectionMatrix = CurrentContext.Projection;
            RenderStateData.NormalMatrix = Matrix4x4.Identity;
            RenderStateUBO.Update(ref RenderStateData);
        }

        /// <summary>
        /// 更新 RenderState UBO
        /// wvpMatrix: 预组合的 WVP（用于 gl_Position）
        /// worldMatrix: 世界矩阵，已含 ViewMatrix（用于 v_Position、法线变换）
        /// </summary>
        protected void UpdateRenderStateUBO(Matrix4x4 wvpMatrix, Matrix4x4 worldMatrix) {
            RenderStateData.ViewProjectionMatrix = wvpMatrix;
            RenderStateData.ModelMatrix = worldMatrix;
            RenderStateData.ViewMatrix = CurrentContext.View;
            RenderStateData.ProjectionMatrix = CurrentContext.Projection;

            // 计算法线矩阵 = transpose(inverse(worldMatrix))
            RenderStateData.NormalMatrix = Matrix4x4.Invert(worldMatrix, out Matrix4x4 invModel) ? Matrix4x4.Transpose(invModel) : Matrix4x4.Identity;
            RenderStateUBO.Update(ref RenderStateData);
        }

        /// <summary>
        /// 更新 UV 变换 UBO（懒更新）
        /// </summary>
        protected void UpdateUVTransformUBO(ModelMaterial material) {
            if (!UvTransformDirty) {
                return;
            }
            UVTransformData uvTransformData = MaterialUboBuilder.BuildUVTransformData(material);
            UVTransformUBO.Update(ref uvTransformData);
            UvTransformDirty = false;
        }

        /// <summary>
        /// 绑定 Uniform Block
        /// </summary>
        protected static void BindUniformBlock(uint programHandle, string blockName, uint bindingPoint) {
            uint blockIndex = GLWrapper.GL.GetUniformBlockIndex(programHandle, blockName);
            if (blockIndex != uint.MaxValue) {
                GLWrapper.GL.UniformBlockBinding(programHandle, blockIndex, bindingPoint);
            }
        }

        /// <summary>
        /// 绑定材质纹理（从 Model 延迟加载）
        /// </summary>
        protected virtual void BindMaterialTextures(Model model, ModelMaterial material, Shader shader, Texture2D textureOverride) {
            // TextureOverride：DAE 等非 glTF 模型使用 ComponentModel.TextureOverride
            if (textureOverride != null) {
                MaterialTextureBinder.BindTexture2D(textureOverride, MaterialTextureSlot.BaseColor);
                MaterialTextureBinder.SetTextureSlotUniforms(shader);
                return;
            }
            int textureCount = model.ModelData?.Textures.Count ?? 0;
            if (textureCount == 0) {
                return;
            }
            Texture2D[] textures = new Texture2D[textureCount];
            for (int i = 0; i < textureCount; i++) {
                textures[i] = model.GetTexture(i);
            }
            MaterialTextureBinder.BindMaterialTextures(material, textures);
            MaterialTextureBinder.SetTextureSlotUniforms(shader);
        }

        /// <summary>
        /// 绑定骨骼纹理到指定纹理槽并设置 shader uniform
        /// </summary>
        /// <param name="jointTexture">骨骼矩阵纹理</param>
        /// <param name="shader">使用该纹理的着色器</param>
        protected virtual void BindJointTexture(JointTexture jointTexture, Shader shader) {
            jointTexture.Bind(JointTextureSlot);
            int programHandle = shader.m_program;
            if (!_jointSamplerLocationCache.TryGetValue(programHandle, out int location)) {
                location = GLWrapper.GL.GetUniformLocation((uint)programHandle, "u_jointsSampler");
                _jointSamplerLocationCache[programHandle] = location;
            }
            if (location >= 0) {
                GLWrapper.GL.Uniform1(location, JointTextureSlot);
            }
        }
    }
}