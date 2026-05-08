using System;
using System.Collections.Generic;
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

            // Pre-filter chunks by distance (once for all faces)
            List<TerrainChunk> visibleChunks = CullChunksByDistance(capturePosition, farPlane);

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

                    // Frustum cull pre-filtered chunks (skips full PrepareForDrawing)
                    PrepareChunksForFace(visibleChunks);

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

        List<TerrainChunk> CullChunksByDistance(Vector3 center, float visibilityRange) {
            float rangeSq = visibilityRange * visibilityRange;
            Vector2 center2d = new(center.X, center.Z);
            List<TerrainChunk> result = [];
            TerrainChunk[] chunks = _subsystemTerrain.Terrain.AllocatedChunks;
            for (int i = 0; i < chunks.Length; i++) {
                TerrainChunk chunk = chunks[i];
                if (chunk.Buffers.Count > 0 && Vector2.DistanceSquared(center2d, chunk.Center) <= rangeSq) {
                    result.Add(chunk);
                }
            }
            return result;
        }

        void PrepareChunksForFace(List<TerrainChunk> candidates) {
            _terrainRenderer.m_chunksToDraw.Clear();
            BoundingFrustum frustum = _camera.ViewFrustum;
            for (int i = 0; i < candidates.Count; i++) {
                TerrainChunk chunk = candidates[i];
                if (frustum.Intersection(chunk.BoundingBox)) {
                    _terrainRenderer.m_chunksToDraw.Add(chunk);
                }
            }
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
