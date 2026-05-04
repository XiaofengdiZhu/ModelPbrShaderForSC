using System;
using System.IO;
using System.Reflection;
using System.Text;
using Engine;
using Engine.Graphics;
using Shader = Engine.Graphics.Shader;
using PrimitiveType = Engine.Graphics.PrimitiveType;

namespace Game {
    /// <summary>
    /// 环境贴图捕获器
    /// 将地形和云层渲染到等距矩形全景图
    /// </summary>
    public class EnvironmentCapture : IDisposable {
        // 着色器
        Shader _terrainOpaqueShader;
        Shader _terrainAlphaTestedShader;
        Shader _terrainTransparentShader;

        // 着色器源码缓存
        string _vertShaderSource;
        string _fragShaderSource;

        // 复用的渲染目标
        RenderTarget2D _renderTarget;
        int _renderTargetWidth;
        int _renderTargetHeight;

        // 捕获相机
        CaptureCamera _captureCamera;
        GameWidget _gameWidget;

        // 子系统引用
        SubsystemTerrain _subsystemTerrain;
        SubsystemSky _subsystemSky;

        // 常量
        const float FarPlane = 256f;
        const int AllOpaqueMask = 0x1F; // (1 << 0) | (1 << 1) | (1 << 2) | (1 << 3) | (1 << 4)
        const int AlphaTestMask = 0x20; // (1 << 5)
        const int TransparentMask = 0x40; // (1 << 6)

        bool _disposed;

        /// <summary>
        /// 初始化环境捕获器
        /// </summary>
        /// <param name="subsystemTerrain">地形子系统</param>
        /// <param name="subsystemSky">天空子系统</param>
        /// <param name="gameWidget">游戏 widget（用于创建 CaptureCamera）</param>
        public void Initialize(SubsystemTerrain subsystemTerrain, SubsystemSky subsystemSky, GameWidget gameWidget) {
            _subsystemTerrain = subsystemTerrain;
            _subsystemSky = subsystemSky;
            _gameWidget = gameWidget;
            LoadShaders();
        }

        void LoadShaders() {
            // 从嵌入式资源加载着色器源码
            Assembly assembly = typeof(EnvironmentCapture).Assembly;
            string vertResourceName = "GltfModelPBRShader.Assets.Shaders.terrain_equirectangular.vert";
            string fragResourceName = "GltfModelPBRShader.Assets.Shaders.terrain_equirectangular.frag";

            using (Stream vertStream = assembly.GetManifestResourceStream(vertResourceName)) {
                if (vertStream == null) {
                    throw new InvalidOperationException($"Cannot find embedded resource: {vertResourceName}");
                }
                using StreamReader reader = new(vertStream, Encoding.UTF8);
                _vertShaderSource = reader.ReadToEnd();
            }

            using (Stream fragStream = assembly.GetManifestResourceStream(fragResourceName)) {
                if (fragStream == null) {
                    throw new InvalidOperationException($"Cannot find embedded resource: {fragResourceName}");
                }
                using StreamReader reader = new(fragStream, Encoding.UTF8);
                _fragShaderSource = reader.ReadToEnd();
            }

            // 创建着色器变体
            // 不透明地形着色器
            _terrainOpaqueShader = CreateTerrainShader("Opaque");

            // 半透明测试地形着色器
            _terrainAlphaTestedShader = CreateTerrainShader("ALPHATESTED");

            // 透明地形着色器
            _terrainTransparentShader = CreateTerrainShader("Transparent");
        }

        Shader CreateTerrainShader(string macro) {
            ShaderMacro[] macros = macro switch {
                "Opaque" => [new ShaderMacro("Opaque")],
                "ALPHATESTED" => [new ShaderMacro("ALPHATESTED")],
                "Transparent" => [new ShaderMacro("Transparent")],
                _ => []
            };
            return new Shader(_vertShaderSource, _fragShaderSource, macros);
        }

        /// <summary>
        /// 捕获地形到等距矩形全景图
        /// </summary>
        /// <param name="capturePosition">捕获位置（世界坐标）</param>
        /// <param name="width">输出宽度</param>
        /// <param name="height">输出高度</param>
        public void CaptureTerrain(Vector3 capturePosition, int width, int height) {
            if (_subsystemTerrain == null || _subsystemTerrain.TerrainRenderer == null) {
                return;
            }

            TerrainRenderer renderer = _subsystemTerrain.TerrainRenderer;

            // 创建或更新捕获相机
            if (_captureCamera == null || _captureCamera.ViewPosition != capturePosition) {
                _captureCamera = new CaptureCamera(_gameWidget, capturePosition);
            }

            // 准备区块（视锥剔除对于等距矩形投影无意义，但我们仍调用 PrepareForDrawing）
            renderer.PrepareForDrawing(_captureCamera);

            // 设置通用 Uniform
            Vector3 fogColor = new(_subsystemSky.ViewFogColor);
            float fogDensity = _subsystemSky.ViewFogDensity;

            SetupShaderUniforms(_terrainOpaqueShader, capturePosition, fogColor, fogDensity);
            SetupShaderUniforms(_terrainAlphaTestedShader, capturePosition, fogColor, fogDensity);
            SetupShaderUniforms(_terrainTransparentShader, capturePosition, fogColor, fogDensity);

            // 设置透明测试阈值
            _terrainAlphaTestedShader.GetParameter("u_alphaThreshold")?.SetValue(0.5f);

            // 渲染状态
            Display.BlendState = BlendState.Opaque;
            Display.DepthStencilState = DepthStencilState.Default;
            Display.RasterizerState = RasterizerState.CullCounterClockwise;

            // 渲染不透明地形（所有方向子集）
            foreach (TerrainChunk chunk in renderer.m_chunksToDraw) {
                DrawTerrainChunkWithShader(_terrainOpaqueShader, chunk, AllOpaqueMask);
            }

            // 渲染半透明测试地形（树叶等）
            Display.BlendState = BlendState.Opaque;
            foreach (TerrainChunk chunk in renderer.m_chunksToDraw) {
                DrawTerrainChunkWithShader(_terrainAlphaTestedShader, chunk, AlphaTestMask);
            }

            // 渲染透明地形（水、玻璃）
            Display.BlendState = BlendState.AlphaBlend;
            foreach (TerrainChunk chunk in renderer.m_chunksToDraw) {
                DrawTerrainChunkWithShader(_terrainTransparentShader, chunk, TransparentMask);
            }
        }

        void SetupShaderUniforms(Shader shader, Vector3 capturePosition, Vector3 fogColor, float fogDensity) {
            shader.GetParameter("u_CaptureCenter")?.SetValue(capturePosition);
            shader.GetParameter("u_FarPlane")?.SetValue(FarPlane);
            shader.GetParameter("u_fogColor")?.SetValue(fogColor);
            shader.GetParameter("u_fogDensity")?.SetValue(fogDensity);
        }

        void DrawTerrainChunkWithShader(Shader shader, TerrainChunk chunk, int subsetsMask) {
            foreach (TerrainChunkGeometry.Buffer buffer in chunk.Buffers) {
                shader.GetParameter("u_texture")?.SetValue(buffer.Texture);
                DrawTerrainChunkGeometrySubsets(shader, chunk, buffer, subsetsMask);
            }
        }

        /// <summary>
        /// 绘制地形区块几何子集
        /// 基于 TerrainRenderer.DrawTerrainChunkGeometrySubsets 改编
        /// </summary>
        void DrawTerrainChunkGeometrySubsets(Shader shader, TerrainChunk chunk, TerrainChunkGeometry.Buffer buffer, int subsetsMask) {
            int startIndex = int.MaxValue;
            int endIndex = 0;

            // 合并连续的子集以减少绘制调用
            for (int i = 0; i < 7; i++) {
                if ((subsetsMask & (1 << i)) != 0 && buffer.SubsetIndexBufferEnds[i] > 0) {
                    if (startIndex == int.MaxValue) {
                        startIndex = buffer.SubsetIndexBufferStarts[i];
                    }
                    endIndex = buffer.SubsetIndexBufferEnds[i];
                }
                else if (endIndex > startIndex) {
                    // 绘制合并的子集
                    Display.DrawIndexed(PrimitiveType.TriangleList, shader, buffer.VertexBuffer, buffer.IndexBuffer, startIndex, endIndex - startIndex);
                    startIndex = int.MaxValue;
                    endIndex = 0;
                }
            }

            // 绘制剩余的子集
            if (endIndex > startIndex) {
                Display.DrawIndexed(PrimitiveType.TriangleList, shader, buffer.VertexBuffer, buffer.IndexBuffer, startIndex, endIndex - startIndex);
            }
        }

        /// <summary>
        /// 捕获云层到等距矩形全景图
        /// </summary>
        /// <param name="capturePosition">捕获位置</param>
        /// <param name="width">输出宽度</param>
        /// <param name="height">输出高度</param>
        public void CaptureClouds(Vector3 capturePosition, int width, int height) {
            // TODO: 实现云层捕获
            // 云层由 SubsystemSky.DrawClouds 渲染，需要复制其逻辑并应用等距矩形投影
            // 暂时跳过，因为云层对 IBL 贡献较小
        }

        /// <summary>
        /// 主入口：捕获环境贴图
        /// </summary>
        /// <param name="capturePosition">捕获位置（玩家眼睛位置）</param>
        /// <param name="width">全景图宽度</param>
        /// <param name="height">全景图高度</param>
        /// <param name="exposure">曝光值（默认 1.0）</param>
        /// <returns>捕获的环境贴图</returns>
        public EnvironmentMap CaptureEnvironment(Vector3 capturePosition, int width, int height, float exposure = 1.0f) {
            // 创建或复用渲染目标
            EnsureRenderTarget(width, height);

            // 保存当前渲染目标
            RenderTarget2D previousRenderTarget = Display.RenderTarget;

            try {
                // 设置渲染目标
                Display.RenderTarget = _renderTarget;
                Display.Clear(Color.Transparent, 1f, 0);

                // 1. 捕获地形
                CaptureTerrain(capturePosition, width, height);

                // 2. 捕获云层（可选）
                CaptureClouds(capturePosition, width, height);

                // 3. 从渲染目标创建 EnvironmentMap
                return EnvironmentMap.FromRenderTarget(_renderTarget, exposure);
            }
            finally {
                // 恢复渲染目标
                Display.RenderTarget = previousRenderTarget;
            }
        }

        void EnsureRenderTarget(int width, int height) {
            if (_renderTarget == null || _renderTargetWidth != width || _renderTargetHeight != height) {
                _renderTarget?.Dispose();
                _renderTarget = new RenderTarget2D(width, height, 1, ColorFormat.Rgba8888, DepthFormat.Depth24Stencil8);
                _renderTargetWidth = width;
                _renderTargetHeight = height;
            }
        }

        /// <summary>
        /// 获取或创建渲染目标（供外部使用）
        /// </summary>
        public RenderTarget2D GetOrCreateRenderTarget(int width, int height) {
            EnsureRenderTarget(width, height);
            return _renderTarget;
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;

            _terrainOpaqueShader?.Dispose();
            _terrainOpaqueShader = null;

            _terrainAlphaTestedShader?.Dispose();
            _terrainAlphaTestedShader = null;

            _terrainTransparentShader?.Dispose();
            _terrainTransparentShader = null;

            _renderTarget?.Dispose();
            _renderTarget = null;

            // CaptureCamera 不实现 IDisposable，无需释放
            _captureCamera = null;
        }
    }
}
