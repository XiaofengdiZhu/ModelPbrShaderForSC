using System;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;

namespace Game {
    public class EnvironmentCapture : IDisposable {
        uint _cubemapTexture;
        uint _depthBuffer;
        uint _framebuffer;
        int _faceSize;

        CubemapCamera _camera;
        TerrainRenderer _terrainRenderer;
        SubsystemSky _subsystemSky;
        SubsystemTerrain _subsystemTerrain;

        bool _disposed;
        int _captureCount;

        static readonly (Vector3 Target, Vector3 Up)[] CubemapFaces = [
            (Vector3.UnitX, Vector3.UnitY),
            (-Vector3.UnitX, Vector3.UnitY),
            (Vector3.UnitY, Vector3.UnitZ),
            (-Vector3.UnitY, -Vector3.UnitZ),
            (Vector3.UnitZ, Vector3.UnitY),
            (-Vector3.UnitZ, Vector3.UnitY),
        ];

        public void Initialize(SubsystemTerrain subsystemTerrain, SubsystemSky subsystemSky) {
            _subsystemTerrain = subsystemTerrain;
            _subsystemSky = subsystemSky;
            _terrainRenderer = subsystemTerrain.TerrainRenderer;
        }

        public uint CaptureEnvironment(GameWidget gameWidget, Vector3 capturePosition, int faceSize) {
            if (_terrainRenderer == null) return 0;

            _camera ??= new CubemapCamera(gameWidget);
            _camera.GameWidget = gameWidget;

            EnsureCubemapResources(faceSize);

            Viewport previousViewport = Display.Viewport;
            RenderTarget2D previousRenderTarget = Display.RenderTarget;

            float farPlane = _subsystemSky.VisibilityRange;

            try {
                for (int face = 0; face < 6; face++) {
                    GLWrapper.BindFramebuffer((int)_framebuffer);
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

                    Color skyColor = _subsystemSky?.ViewFogColor ?? Color.Transparent;
                    GLWrapper.ClearColor(new Vector4(
                        skyColor.R / 255f,
                        skyColor.G / 255f,
                        skyColor.B / 255f,
                        1f
                    ));
                    GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    var (target, up) = CubemapFaces[face];
                    _camera.SetupForCubemapFace(capturePosition, target, up, farPlane);

                    if (_captureCount < 3) {
                        Log.Information(string.Format("[glTF PBR Shader] Face {0} ({1}): pos={2}, target={3}, up={4}, viewDir={5}",
                            face, FaceNames[face], capturePosition, target, up, _camera.ViewDirection));
                    }

                    _terrainRenderer.PrepareForDrawing(_camera);
                    _terrainRenderer.DrawOpaque(_camera);
                    _terrainRenderer.DrawAlphaTested(_camera);
                    _terrainRenderer.DrawTransparent(_camera);
                }

                GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
                GLWrapper.GL.GenerateMipmap(TextureTarget.TextureCubeMap);

                // if (_captureCount++ <= 3) {
                //     SaveCubemapToFiles(faceSize, _captureCount);
                // }
                _captureCount++;
            }
            finally {
                Display.Viewport = previousViewport;
                Display.RenderTarget = previousRenderTarget;

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

            if (_cubemapTexture != 0) {
                GLWrapper.DeleteTexture((int)_cubemapTexture);
                GLWrapper.GL.DeleteRenderbuffer(_depthBuffer);
                GLWrapper.DeleteFramebuffer((int)_framebuffer);
            }

            _faceSize = faceSize;

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

            _depthBuffer = GLWrapper.GL.GenRenderbuffer();
            GLWrapper.GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depthBuffer);
            GLWrapper.GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, InternalFormat.DepthComponent24, (uint)faceSize, (uint)faceSize);

            _framebuffer = GLWrapper.GL.GenFramebuffer();
        }

        static readonly string[] FaceNames = ["posX", "negX", "posY", "negY", "posZ", "negZ"];

        unsafe void SaveCubemapToFiles(int faceSize, int captureCount) {
            try {
                string outputDir = RunPath.GetOperatingPath();

                for (int face = 0; face < 6; face++) {
                    GLWrapper.BindFramebuffer((int)_framebuffer);
                    GLWrapper.GL.FramebufferTexture2D(
                        FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0,
                        TextureTarget.TextureCubeMapPositiveX + face,
                        _cubemapTexture,
                        0
                    );

                    Half[] halfPixels = new Half[faceSize * faceSize * 4];
                    fixed (Half* ptr = halfPixels) {
                        GLWrapper.GL.ReadPixels(0, 0, (uint)faceSize, (uint)faceSize, PixelFormat.Rgba, PixelType.HalfFloat, ptr);
                    }

                    var sharpImage = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                        Image.DefaultImageSharpConfiguration, faceSize, faceSize);
                    for (int y = 0; y < faceSize; y++) {
                        for (int x = 0; x < faceSize; x++) {
                            int srcY = faceSize - 1 - y;
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
                    string filename = string.Format("{0}_cubemap_{1}.png", captureCount, FaceNames[face]);
                    string filepath = Storage.CombinePaths(outputDir, filename);
                    Engine.Media.Image.Save(image, filepath, ImageFileFormat.Png, true);
                    Log.Information(string.Format("[glTF PBR Shader] Saved cubemap face: {0}", filepath));
                }
            }
            catch (Exception ex) {
                Log.Warning(string.Format("[glTF PBR Shader] Failed to save cubemap: {0}", ex.Message));
            }
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            if (_cubemapTexture != 0) {
                GLWrapper.DeleteTexture((int)_cubemapTexture);
                _cubemapTexture = 0;
            }
            if (_depthBuffer != 0) {
                GLWrapper.GL.DeleteRenderbuffer(_depthBuffer);
                _depthBuffer = 0;
            }
            if (_framebuffer != 0) {
                GLWrapper.DeleteFramebuffer((int)_framebuffer);
                _framebuffer = 0;
            }
        }
    }
}
