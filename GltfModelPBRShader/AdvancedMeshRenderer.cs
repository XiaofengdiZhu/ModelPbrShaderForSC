using System;
using System.Collections.Generic;
using System.Numerics;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using PrimitiveType = Silk.NET.OpenGLES.PrimitiveType;
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
    public abstract class AdvancedMeshRenderer : ICustomModelRenderer {
        protected const int MaxInstancesPerBatch = 256;

        // Locations 8-11: instance model matrix (mat4), location 12: instance light data (vec2)
        protected const int InstanceLightAttribLocation = 12;

        /// <summary>
        /// 骨骼纹理纹理槽
        /// 注意：MaterialTextureSlot.MorphTargets = 30，JointTexture 使用 slot 31 避免冲突
        /// </summary>
        protected const int JointTextureSlot = 31;

        protected readonly Dictionary<int, int> _celestialBodyVisibleLocCache = [];
        protected readonly Dictionary<int, int> _glymulLocationCache = [];

        // Uniform location 缓存
        readonly Dictionary<int, int> _jointSamplerLocationCache = [];
        protected readonly Dictionary<int, int> _terrainLightLocCache = [];

        // 帧级光照数据（逐模型缩放时使用）
        Vector3 _baseLightColor;

        // 缓存优化
        bool _instanceBufferCreated;
        protected Vector2[] _instanceLightData = new Vector2[MaxInstancesPerBatch];
        int _instanceLightVBO;
        protected Matrix4x4[] _instanceMatrices = new Matrix4x4[MaxInstancesPerBatch];

        // 实例化渲染
        int _instanceVBO;
        (bool useIBL, bool useLinearOutput, ToneMapMode toneMapMode, int lightCount, DebugChannel debugChannel) _lastContextParams;

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
        /// 开始帧渲染
        /// </summary>
        public virtual void BeginFrame(Camera camera) {
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
            CurrentContext.LightCount = 1;
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
            UvTransformDirty = true;
            MaterialTextureBinder.ResetFrameState();
        }

        public abstract void RenderInstances(List<InstanceRenderData> instances);

        public abstract void RenderPart(ModelMesh mesh,
            ModelMeshPart part,
            ModelMaterial material,
            SubsystemModelsRenderer.ModelData modelData,
            Texture2D textureOverride,
            JointTexture jointTexture = null);

        public virtual void PreRenderPass(Camera camera, List<SubsystemModelsRenderer.ModelData>[] modelsToDraw) { }

        public virtual void PreTransparentPass(Camera camera) { }

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
        /// 更新光照 UBO（逐模型光照强度缩放）
        /// </summary>
        protected void UpdateLightsUBO(float intensity) {
            LightsData lightsData = new() {
                LightCount = 1, Light0 = new LightData { Direction = _viewLightDir, Color = _baseLightColor * intensity, Intensity = 1f, Type = 0 }
            };
            LightsUBO.Update(ref lightsData);
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
        /// 设置深度测试
        /// 通过 ApplyDepthStencilState 保持 GLWrapper 缓存同步
        /// </summary>
        protected virtual void SetupDepthState(ModelMaterial material) {
            GLWrapper.ApplyDepthStencilState(DepthStencilState.Default);
        }

        /// <summary>
        /// 设置剔除模式
        /// 通过 ApplyRasterizerState 保持 GLWrapper 缓存同步
        /// </summary>
        protected virtual void SetupCullMode(ModelMaterial material, bool isNegativeScale = false) {
            RasterizerState state;
            if (material?.DoubleSided == true) {
                state = RasterizerState.CullNoneScissor;
            }
            else if (isNegativeScale) {
                state = RasterizerState.CullClockwiseScissor;
            }
            else {
                state = RasterizerState.CullCounterClockwiseScissor;
            }
            GLWrapper.ApplyRasterizerState(state);
        }

        /// <summary>
        /// 设置混合模式
        /// 必须通过 ApplyBlendState 设置，保持 GLWrapper.m_blendState 缓存同步。
        /// 直接调用 Enable/Disable(Blend) 会绕过缓存，导致后续 Display.DrawIndexed
        /// 的 ApplyBlendState 跳过 GL 调用，混合状态异常。
        /// </summary>
        protected virtual void SetupBlendMode(ModelMaterial material, RenderContext context) {
            // Transmission + 线性输出时禁用 blend（transmission pass 不需要混合）
            if (material?.Transmission?.IsEnabled == true
                && context.UseLinearOutput) {
                GLWrapper.ApplyBlendState(BlendState.Opaque);
                return;
            }
            ModelAlphaMode alphaMode = material?.AlphaMode ?? ModelAlphaMode.Opaque;
            GLWrapper.ApplyBlendState(alphaMode == ModelAlphaMode.Blend ? BlendState.AlphaBlend : BlendState.Opaque);
        }

        /// <summary>
        /// 绘制网格
        /// </summary>
        protected virtual void DrawMesh(ModelMesh mesh) {
            if (mesh == null) {
                return;
            }

            // 同步 Display.Viewport 到 GL（包含 DepthRange）
            // 本渲染器直接调用 GLWrapper.GL.DrawElements 绕过 Display.DrawIndexed，
            // 不会触发 GLWrapper.ApplyViewportScissor。
            // 若不手动同步，ComponentFirstPersonModel 的压缩深度范围会残留，
            // 导致 PBR 模型深度值异常，不被地形遮挡。
            GLWrapper.ApplyViewportScissor(Display.Viewport, Display.ScissorRectangle, Display.RasterizerState.ScissorTestEnable);
            foreach (ModelMeshPart part in mesh.MeshParts) {
                DrawMeshPart(part);
            }
        }

        /// <summary>
        /// 绘制网格部件
        /// </summary>
        protected virtual void DrawMeshPart(ModelMeshPart part) {
            if (part?.VertexBuffer == null
                || part.IndexBuffer == null) {
                return;
            }

            // 绑定顶点缓冲
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, part.VertexBuffer.m_buffer);
            GLWrapper.BindBuffer(BufferTargetARB.ElementArrayBuffer, part.IndexBuffer.m_buffer);

            // 设置顶点属性
            SetupVertexAttributes(part.VertexBuffer.VertexDeclaration);

            // 绘制
            unsafe {
                IntPtr indexOffset = new(part.StartIndex * part.IndexBuffer.IndexFormat.GetSize());
                GLWrapper.GL.DrawElements(
                    PrimitiveType.Triangles,
                    (uint)part.IndicesCount,
                    GLWrapper.TranslateIndexFormat(part.IndexBuffer.IndexFormat),
                    indexOffset.ToPointer()
                );
            }
        }

        /// <summary>
        /// 设置顶点属性
        /// 将 VertexDeclaration 中的 semantic 映射到着色器的 attribute location
        /// </summary>
        protected virtual void SetupVertexAttributes(VertexDeclaration declaration) {
            if (declaration == null) {
                return;
            }

            // 禁用所有 attribute（最多 8 个）
            for (int i = 0; i < 8; i++) {
                GLWrapper.VertexAttribArray(i, false);
            }

            // 遍历 vertex elements，映射到 attribute locations
            foreach (VertexElement element in declaration.VertexElements) {
                int location = SemanticToLocation(element.Semantic);
                if (location < 0) {
                    continue;
                }
                GLWrapper.TranslateVertexElementFormat(element.Format, out VertexAttribPointerType type, out bool normalize);
                int size = element.Format.GetElementsCount();
                int stride = declaration.VertexStride;
                unsafe {
                    GLWrapper.GL.VertexAttribPointer((uint)location, size, type, normalize, (uint)stride, new IntPtr(element.Offset).ToPointer());
                }
                GLWrapper.VertexAttribArray(location, true);
            }
        }

        protected void EnsureInstanceBuffer() {
            if (_instanceBufferCreated) {
                return;
            }
            unsafe {
                GLWrapper.GL.GenBuffers(1, out uint buffer);
                _instanceVBO = (int)buffer;
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVBO);
                GLWrapper.GL.BufferData(BufferTargetARB.ArrayBuffer, MaxInstancesPerBatch * 64, (void*)0, BufferUsageARB.DynamicDraw);
                GLWrapper.GL.GenBuffers(1, out uint lightBuffer);
                _instanceLightVBO = (int)lightBuffer;
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceLightVBO);
                GLWrapper.GL.BufferData(BufferTargetARB.ArrayBuffer, MaxInstancesPerBatch * 8, (void*)0, BufferUsageARB.DynamicDraw);
            }
            _instanceBufferCreated = true;
        }

        protected void UploadInstanceData(Matrix4x4[] matrices, int count) {
            EnsureInstanceBuffer();
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVBO);
            unsafe {
                fixed (Matrix4x4* ptr = matrices) {
                    GLWrapper.GL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(count * 64), ptr);
                }
            }
        }

        protected void UploadInstanceLightData(Vector2[] lightData, int count) {
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceLightVBO);
            unsafe {
                fixed (Vector2* ptr = lightData) {
                    GLWrapper.GL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(count * 8), ptr);
                }
            }
        }

        protected void SetupInstanceAttributes() {
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVBO);
            unsafe {
                for (int i = 0; i < 4; i++) {
                    uint loc = (uint)(8 + i);
                    GLWrapper.GL.VertexAttribPointer(loc, 4, VertexAttribPointerType.Float, false, 64, new IntPtr(i * 16).ToPointer());
                    GLWrapper.GL.EnableVertexAttribArray(loc);
                    GLWrapper.GL.VertexAttribDivisor(loc, 1);
                }
            }
            // Per-instance light: vec2 (terrainLight, sunVisible)
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceLightVBO);
            unsafe {
                GLWrapper.GL.VertexAttribPointer(InstanceLightAttribLocation, 2, VertexAttribPointerType.Float, false, 8, (void*)0);
                GLWrapper.GL.EnableVertexAttribArray(InstanceLightAttribLocation);
                GLWrapper.GL.VertexAttribDivisor(InstanceLightAttribLocation, 1);
            }
        }

        protected void DisableInstanceAttributes() {
            for (int i = 0; i < 4; i++) {
                GLWrapper.GL.DisableVertexAttribArray((uint)(8 + i));
            }
            GLWrapper.GL.DisableVertexAttribArray(InstanceLightAttribLocation);
        }

        /// <summary>
        /// 实例化绘制网格
        /// </summary>
        protected virtual void DrawMeshInstanced(ModelMesh mesh, int instanceCount) {
            if (mesh == null) {
                return;
            }
            GLWrapper.ApplyViewportScissor(Display.Viewport, Display.ScissorRectangle, Display.RasterizerState.ScissorTestEnable);
            VertexDeclaration lastDecl = null;
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.VertexBuffer == null
                    || part.IndexBuffer == null) {
                    continue;
                }
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, part.VertexBuffer.m_buffer);
                GLWrapper.BindBuffer(BufferTargetARB.ElementArrayBuffer, part.IndexBuffer.m_buffer);
                if (part.VertexBuffer.VertexDeclaration != lastDecl) {
                    SetupVertexAttributes(part.VertexBuffer.VertexDeclaration);
                    lastDecl = part.VertexBuffer.VertexDeclaration;
                }
                unsafe {
                    IntPtr indexOffset = new(part.StartIndex * part.IndexBuffer.IndexFormat.GetSize());
                    GLWrapper.GL.DrawElementsInstanced(
                        PrimitiveType.Triangles,
                        (uint)part.IndicesCount,
                        GLWrapper.TranslateIndexFormat(part.IndexBuffer.IndexFormat),
                        indexOffset.ToPointer(),
                        (uint)instanceCount
                    );
                }
            }
        }

        /// <summary>
        /// 将 vertex semantic 字符串映射到着色器 attribute location
        /// </summary>
        protected static int SemanticToLocation(string semantic) {
            return semantic switch {
                "POSITION" => 0,
                "NORMAL" => 1,
                "TEXCOORD" => 2,
                "TEXCOORD0" => 2,
                "TEXCOORD1" => 3,
                "TEXCOORD2" => -1,
                "COLOR" => 4,
                "TANGENT" => 5,
                "BLENDINDICES" => 6,
                "BLENDWEIGHTS" => 7,
                _ => -1
            };
        }

        protected void UpdateContextHash(RenderContext context) {
            (bool UseIBL, bool UseLinearOutput, ToneMapMode ToneMapMode, int LightCount, DebugChannel DebugChannel) contextParams = (context.UseIBL,
                context.UseLinearOutput, context.ToneMapMode, context.LightCount, context.DebugChannel);
            if (_lastContextParams == contextParams) {
                return;
            }
            _lastContextParams = contextParams;
            CachedContextHash = ComputeContextHash(context);
        }

        /// <summary>
        /// 计算渲染上下文的 defines hash
        /// </summary>
        protected static int ComputeContextHash(RenderContext context) {
            unchecked {
                int hash = 17;
                if (context.UseIBL) {
                    hash = hash * 31 + "USE_IBL 1".GetHashCode();
                }
                if (context.LightCount > 0) {
                    hash = hash * 31 + "USE_PUNCTUAL 1".GetHashCode();
                }
                if (context.UseLinearOutput) {
                    hash = hash * 31 + "LINEAR_OUTPUT 1".GetHashCode();
                }
                else {
                    string tonemapDefine = context.ToneMapMode switch {
                        ToneMapMode.KhrPbrNeutral => "TONEMAP_KHR_PBR_NEUTRAL 1",
                        ToneMapMode.AcesNarkowicz => "TONEMAP_ACES_NARKOWICZ 1",
                        ToneMapMode.AcesHill => "TONEMAP_ACES_HILL 1",
                        ToneMapMode.AcesHillExposureBoost => "TONEMAP_ACES_HILL_EXPOSURE_BOOST 1",
                        _ => "LINEAR_OUTPUT 1"
                    };
                    hash = hash * 31 + tonemapDefine.GetHashCode();
                }
                if (context.DebugChannel != DebugChannel.None) {
                    hash = hash * 31 + $"DEBUG {(int)context.DebugChannel}".GetHashCode();
                }
                return hash;
            }
        }

        /// <summary>
        /// 根据材质特性调整上下文 hash：
        /// - DiffuseTransmission 强制启用 IBL
        /// - Unlit 材质移除 USE_PUNCTUAL
        /// </summary>
        protected static int AdjustContextHashForMaterial(int contextHash, ModelMaterial material, RenderContext context) {
            unchecked {
                if (material?.DiffuseTransmission?.IsEnabled == true
                    && !context.UseIBL) {
                    contextHash ^= "USE_IBL 1".GetHashCode();
                }
                if (material?.Unlit?.IsEnabled == true
                    && context.LightCount > 0) {
                    contextHash ^= "USE_PUNCTUAL 1".GetHashCode();
                }
                return contextHash;
            }
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