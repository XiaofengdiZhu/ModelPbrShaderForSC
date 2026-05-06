using System;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;

namespace Game {
    public class EnvironmentCapture : IDisposable {
        CubemapRenderTarget _cubemapRenderTarget;
        int _faceSize;

        CubemapCamera _camera;
        TerrainRenderer _terrainRenderer;
        SubsystemSky _subsystemSky;
        SubsystemTerrain _subsystemTerrain;

        bool _disposed;

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

        public CubemapTexture CaptureEnvironment(GameWidget gameWidget, Vector3 capturePosition, int faceSize) {
            if (_terrainRenderer == null) return null;

            _camera ??= new CubemapCamera(gameWidget);
            _camera.GameWidget = gameWidget;

            EnsureCubemapResources(faceSize);

            Viewport previousViewport = Display.Viewport;
            RenderTarget2D previousRenderTarget = Display.RenderTarget;
            int savedMainFramebuffer = GLWrapper.m_mainFramebuffer;

            float farPlane = _subsystemSky.VisibilityRange;

            try {
                GLWrapper.m_mainFramebuffer = _cubemapRenderTarget.m_frameBuffer;
                Display.RenderTarget = null;

                // ApplyViewportScissor Y-flips when RenderTarget==null:
                //   y = BackbufferSize.Y - viewport.Y - viewport.Height
                // Compensate: set Y = BackbufferSize.Y - faceSize, so after flip: y = 0
                int compensateY = Display.BackbufferSize.Y - faceSize;
                Viewport cubemapViewport = new Viewport(0, compensateY, faceSize, faceSize);
                Rectangle cubemapScissor = new Rectangle(0, compensateY, faceSize, faceSize);

                for (int face = 0; face < 6; face++) {
                    _cubemapRenderTarget.BindFace(face);
                    GLWrapper.m_framebuffer = -1;

                    Display.Viewport = cubemapViewport;
                    Display.ScissorRectangle = cubemapScissor;

                    GLWrapper.ClearColor(new Vector4(_subsystemSky.ViewFogColor));
                    GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                    var (target, up) = CubemapFaces[face];
                    _camera.SetupForCubemapFace(capturePosition, target, up, farPlane);

                    _terrainRenderer.PrepareForDrawing(_camera);

                    _terrainRenderer.DrawOpaque(_camera);
                    _terrainRenderer.DrawAlphaTested(_camera);
                    _terrainRenderer.DrawTransparent(_camera);
                }

                _cubemapRenderTarget.GenerateMipMaps();
            }
            finally {
                GLWrapper.m_mainFramebuffer = savedMainFramebuffer;
                Display.RenderTarget = previousRenderTarget;
                Display.Viewport = previousViewport;

                GLWrapper.m_program = -1;
                GLWrapper.m_framebuffer = -1;
                GLWrapper.m_lastShader = null;
                GLWrapper.m_texture2D = -1;
                GLWrapper.m_lastVertexDeclaration = null;
                GLWrapper.m_lastVertexOffset = IntPtr.Zero;
                GLWrapper.m_lastArrayBuffer = -1;
                GLWrapper.m_viewport = null;
                GLWrapper.m_rasterizerState = null;
                GLWrapper.m_depthStencilState = null;
                GLWrapper.m_blendState = null;
            }

            return _cubemapRenderTarget;
        }

        void EnsureCubemapResources(int faceSize) {
            if (_cubemapRenderTarget != null && _faceSize == faceSize) return;

            _cubemapRenderTarget?.Dispose();
            _faceSize = faceSize;

            _cubemapRenderTarget = new CubemapRenderTarget(faceSize, 1, ColorFormat.Rgba16f, DepthFormat.Depth24Stencil8);
            _cubemapRenderTarget.SetFilterMode(true);
            _cubemapRenderTarget.SetWrapMode(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
        }

        static readonly string[] FaceNames = ["posX", "negX", "posY", "negY", "posZ", "negZ"];

        public unsafe void SaveCubemapToFiles(int faceSize, int captureCount) {
            try {
                string outputDir = RunPath.GetOperatingPath();

                for (int face = 0; face < 6; face++) {
                    _cubemapRenderTarget.BindFace(face);

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

            _cubemapRenderTarget?.Dispose();
            _cubemapRenderTarget = null;
        }
    }
}
