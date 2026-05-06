using System;
using Engine;
using Engine.Graphics;
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

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;

            _cubemapRenderTarget?.Dispose();
            _cubemapRenderTarget = null;
        }
    }
}
