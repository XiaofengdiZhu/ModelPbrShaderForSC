using System;
using Engine;
using Engine.Graphics;
using Silk.NET.OpenGLES;

namespace Game {
    /// <summary>
    /// 环境贴图捕获器
    /// 将地形渲染到 Cubemap 的 6 个面
    /// </summary>
    public class EnvironmentCapture : IDisposable {
        // Cubemap 纹理和帧缓冲
        uint _cubemapTexture;
        uint _depthBuffer;
        uint _framebuffer;
        int _faceSize;

        // 相机和子系统引用
        CubemapCamera _camera;
        TerrainRenderer _terrainRenderer;
        SubsystemSky _subsystemSky;
        SubsystemTerrain _subsystemTerrain;

        bool _disposed;

        // Cubemap 6 面朝向（左手坐标系：+Z 前，+Y 上）
        static readonly (Vector3 Target, Vector3 Up)[] CubemapFaces = [
            (Vector3.UnitX, -Vector3.UnitY),      // +X 右
            (-Vector3.UnitX, -Vector3.UnitY),     // -X 左
            (Vector3.UnitY, Vector3.UnitZ),       // +Y 上
            (-Vector3.UnitY, -Vector3.UnitZ),     // -Y 下
            (Vector3.UnitZ, -Vector3.UnitY),      // +Z 后（引擎 +Z 为前，但 GL cubemap 约定 +Z 为后）
            (-Vector3.UnitZ, -Vector3.UnitY),     // -Z 前
        ];

        public void Initialize(SubsystemTerrain subsystemTerrain, SubsystemSky subsystemSky) {
            _subsystemTerrain = subsystemTerrain;
            _subsystemSky = subsystemSky;
            _terrainRenderer = subsystemTerrain.TerrainRenderer;
            _camera = new CubemapCamera();
        }

        /// <summary>
        /// 捕获环境到 Cubemap
        /// </summary>
        /// <param name="capturePosition">捕获位置</param>
        /// <param name="faceSize">每面分辨率</param>
        /// <returns>Cubemap 纹理的 GL 句柄</returns>
        public uint CaptureEnvironment(Vector3 capturePosition, int faceSize) {
            if (_terrainRenderer == null) return 0;

            EnsureCubemapResources(faceSize);

            // 保存当前渲染状态
            Viewport previousViewport = Display.Viewport;
            RenderTarget2D previousRenderTarget = Display.RenderTarget;

            float farPlane = _subsystemSky.VisibilityRange;

            try {
                for (int face = 0; face < 6; face++) {
                    // 绑定 cubemap face 到帧缓冲
                    GLWrapper.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                    GLWrapper.GL.FramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0,
                        TextureTarget.TextureCubeMapPositiveX + face,
                        _cubemapTexture,
                        0
                    );
                    GLWrapper.GL.FramebufferRenderbuffer(
                        FramebufferTarget.Framebuffer,
                        FramebufferAttachment.DepthAttachment,
                        RenderbufferTarget.Renderbuffer,
                        _depthBuffer
                    );

                    GLWrapper.GL.Viewport(0, 0, (uint)faceSize, (uint)faceSize);

                    // 清除缓冲
                    Color skyColor = _subsystemSky?.ViewFogColor ?? Color.Transparent;
                    GLWrapper.GL.ClearColor(
                        skyColor.R / 255f,
                        skyColor.G / 255f,
                        skyColor.B / 255f,
                        1f
                    );
                    GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    // 设置相机朝向
                    var (target, up) = CubemapFaces[face];
                    _camera.SetupForCubemapFace(capturePosition, target, up, farPlane);

                    // 准备和渲染地形
                    _terrainRenderer.PrepareForDrawing(_camera);
                    _terrainRenderer.DrawOpaque(_camera);
                    _terrainRenderer.DrawAlphaTested(_camera);
                    _terrainRenderer.DrawTransparent(_camera);
                }

                // 生成 mipmaps
                GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
                GLWrapper.GL.GenerateMipmap(TextureTarget.TextureCubeMap);
            }
            finally {
                // 恢复渲染状态
                Display.Viewport = previousViewport;
                Display.RenderTarget = previousRenderTarget;

                // 重置 GLWrapper 缓存
                GLWrapper.m_program = -1;
                GLWrapper.m_framebuffer = -1;
                GLWrapper.m_lastShader = null;
                GLWrapper.m_texture2D = -1;
                GLWrapper.m_viewport = null;
            }

            return _cubemapTexture;
        }

        unsafe void EnsureCubemapResources(int faceSize) {
            if (_cubemapTexture != 0 && _faceSize == faceSize) return;

            // 清理旧资源
            if (_cubemapTexture != 0) {
                GLWrapper.GL.DeleteTexture(_cubemapTexture);
                GLWrapper.GL.DeleteRenderbuffer(_depthBuffer);
                GLWrapper.GL.DeleteFramebuffer(_framebuffer);
            }

            _faceSize = faceSize;

            // 创建 cubemap 纹理
            _cubemapTexture = GLWrapper.GL.GenTexture();
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
            for (int i = 0; i < 6; i++) {
                GLWrapper.GL.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0,
                    InternalFormat.Rgba8,
                    (uint)faceSize,
                    (uint)faceSize,
                    0,
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    null
                );
            }
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // 创建深度缓冲
            _depthBuffer = GLWrapper.GL.GenRenderbuffer();
            GLWrapper.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
            GLWrapper.GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)faceSize, (uint)faceSize);

            // 创建帧缓冲
            _framebuffer = GLWrapper.GL.GenFramebuffer();
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            if (_cubemapTexture != 0) {
                GLWrapper.GL.DeleteTexture(_cubemapTexture);
                _cubemapTexture = 0;
            }
            if (_depthBuffer != 0) {
                GLWrapper.GL.DeleteRenderbuffer(_depthBuffer);
                _depthBuffer = 0;
            }
            if (_framebuffer != 0) {
                GLWrapper.GL.DeleteFramebuffer(_framebuffer);
                _framebuffer = 0;
            }
        }
    }
}
