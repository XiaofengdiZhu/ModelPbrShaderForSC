using System;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using SixLabors.ImageSharp.Processing;

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
        int _captureCount;  // DEBUG: 捕获计数

        // Cubemap 6 面朝向（引擎左手坐标系：+Z 前，+Y 上）
        static readonly (Vector3 Target, Vector3 Up)[] CubemapFaces = [
            (Vector3.UnitX, Vector3.UnitY),       // +X 右
            (-Vector3.UnitX, Vector3.UnitY),      // -X 左
            (Vector3.UnitY, Vector3.UnitZ),       // +Y 上（Up 指向 +Z 前）
            (-Vector3.UnitY, -Vector3.UnitZ),     // -Y 下（Up 指向 -Z 后）
            (Vector3.UnitZ, Vector3.UnitY),       // +Z 前
            (-Vector3.UnitZ, Vector3.UnitY),      // -Z 后
        ];

        public void Initialize(SubsystemTerrain subsystemTerrain, SubsystemSky subsystemSky) {
            _subsystemTerrain = subsystemTerrain;
            _subsystemSky = subsystemSky;
            _terrainRenderer = subsystemTerrain.TerrainRenderer;
            // CubemapCamera 在 CaptureEnvironment 时按需创建
        }

        /// <summary>
        /// 捕获环境到 Cubemap
        /// </summary>
        /// <param name="gameWidget">提供 GameWidgetIndex 的 GameWidget</param>
        /// <param name="capturePosition">捕获位置</param>
        /// <param name="faceSize">每面分辨率</param>
        /// <returns>Cubemap 纹理的 GL 句柄</returns>
        public uint CaptureEnvironment(GameWidget gameWidget, Vector3 capturePosition, int faceSize) {
            if (_terrainRenderer == null) return 0;

            // 创建或复用 CubemapCamera，使用传入的 GameWidget
            _camera ??= new CubemapCamera(gameWidget);
            _camera.GameWidget = gameWidget;

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

                    // DEBUG: 输出相机参数
                    if (_captureCount < 3) {
                        Log.Information($"[glTF PBR Shader] Face {face} ({FaceNames[face]}): pos={capturePosition}, target={target}, up={up}, viewDir={_camera.ViewDirection}");
                    }

                    // 准备和渲染地形
                    _terrainRenderer.PrepareForDrawing(_camera);
                    _terrainRenderer.DrawOpaque(_camera);
                    _terrainRenderer.DrawAlphaTested(_camera);
                    _terrainRenderer.DrawTransparent(_camera);
                }

                // 生成 mipmaps
                GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
                GLWrapper.GL.GenerateMipmap(TextureTarget.TextureCubeMap);

                // DEBUG: 只保存前3次捕获 (已禁用)
                // if (_captureCount++ <= 3) {
                //     SaveCubemapToFiles(faceSize, _captureCount);
                // }
                _captureCount++;
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

            // 创建 cubemap 纹理 (HDR 格式以匹配静态环境贴图)
            _cubemapTexture = GLWrapper.GL.GenTexture();
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
            for (int i = 0; i < 6; i++) {
                GLWrapper.GL.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0,
                    InternalFormat.Rgba16f,
                    (uint)faceSize,
                    (uint)faceSize,
                    0,
                    PixelFormat.Rgba,
                    PixelType.HalfFloat,
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

        static readonly string[] FaceNames = ["posX", "negX", "posY", "negY", "posZ", "negZ"];

        unsafe void SaveCubemapToFiles(int faceSize, int captureCount) {
            try {
                string outputDir = RunPath.GetOperatingPath();

                // 读取每个面并保存
                for (int face = 0; face < 6; face++) {
                    // 绑定到该面
                    GLWrapper.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                    GLWrapper.GL.FramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0,
                        TextureTarget.TextureCubeMapPositiveX + face,
                        _cubemapTexture,
                        0
                    );

                    // 读取 HDR 像素 (Rgba16f -> HalfFloat)
                    Half[] halfPixels = new Half[faceSize * faceSize * 4];
                    fixed (Half* ptr = halfPixels) {
                        GLWrapper.GL.ReadPixels(0, 0, (uint)faceSize, (uint)faceSize, PixelFormat.Rgba, PixelType.HalfFloat, ptr);
                    }

                    // 转换为 LDR (Rgba32) 并垂直翻转
                    var sharpImage = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                        Image.DefaultImageSharpConfiguration, faceSize, faceSize);
                    for (int y = 0; y < faceSize; y++) {
                        for (int x = 0; x < faceSize; x++) {
                            int srcY = faceSize - 1 - y; // 垂直翻转
                            int srcIdx = (srcY * faceSize + x) * 4;
                            float r = (float)halfPixels[srcIdx];
                            float g = (float)halfPixels[srcIdx + 1];
                            float b = (float)halfPixels[srcIdx + 2];
                            float a = (float)halfPixels[srcIdx + 3];
                            sharpImage[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                                (byte)Math.Clamp(r * 255, 0, 255),
                                (byte)Math.Clamp(g * 255, 0, 255),
                                (byte)Math.Clamp(b * 255, 0, 255),
                                (byte)Math.Clamp(a * 255, 0, 255)
                            );
                        }
                    }

                    var image = new Engine.Media.Image(sharpImage);
                    string filename = $"{captureCount}_cubemap_{FaceNames[face]}.png";
                    string filepath = Storage.CombinePaths(outputDir, filename);
                    Engine.Media.Image.Save(image, filepath, ImageFileFormat.Png, true);
                    Log.Information($"[glTF PBR Shader] Saved cubemap face: {filepath}");
                }
            }
            catch (Exception ex) {
                Log.Warning($"[glTF PBR Shader] Failed to save cubemap: {ex.Message}");
            }
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
