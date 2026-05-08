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

        // Multi-frame capture state
        List<TerrainChunk> _captureChunks;
        Vector3 _capturePosition;
        float _captureFarPlane;
        Viewport _cubemapViewport;
        Rectangle _cubemapScissor;
        Viewport _savedViewport;
        Rectangle _savedScissor;
        RenderTarget2D _savedRenderTarget;
        int _savedMainFramebuffer;

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

        public void PrepareCapture(GameWidget gameWidget, Vector3 capturePosition, int faceSize) {
            if (_terrainRenderer == null) return;

            _camera ??= new CubemapCamera(gameWidget);
            _camera.GameWidget = gameWidget;
            EnsureCubemapResources(faceSize);

            _capturePosition = capturePosition;
            _captureFarPlane = _subsystemSky.VisibilityRange;
            _captureChunks = CullChunksByDistance(capturePosition, _captureFarPlane);

            int compensateY = Display.BackbufferSize.Y - faceSize;
            _cubemapViewport = new Viewport(0, compensateY, faceSize, faceSize);
            _cubemapScissor = new Rectangle(0, compensateY, faceSize, faceSize);
        }

        public void BeginFaceGroup() {
            _savedViewport = Display.Viewport;
            _savedScissor = Display.ScissorRectangle;
            _savedRenderTarget = Display.RenderTarget;
            _savedMainFramebuffer = GLWrapper.m_mainFramebuffer;

            GLWrapper.m_mainFramebuffer = _cubemapRenderTarget.m_frameBuffer;
            Display.RenderTarget = null;
        }

        public void CaptureFace(int face) {
            _cubemapRenderTarget.BindFace(face);
            GLWrapper.m_framebuffer = -1;

            Display.Viewport = _cubemapViewport;
            Display.ScissorRectangle = _cubemapScissor;

            GLWrapper.ClearColor(new Vector4(_subsystemSky.ViewFogColor));
            GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            var (target, up) = CubemapFaces[face];
            _camera.SetupForCubemapFace(_capturePosition, target, up, _captureFarPlane);
            PrepareChunksForFace(_captureChunks);

            _terrainRenderer.DrawOpaque(_camera);
            _terrainRenderer.DrawAlphaTested(_camera);
            _terrainRenderer.DrawTransparent(_camera);
        }

        public void EndFaceGroup() {
            GLWrapper.m_mainFramebuffer = _savedMainFramebuffer;
            Display.RenderTarget = _savedRenderTarget;
            Display.ScissorRectangle = _savedScissor;
            Display.Viewport = _savedViewport;
        }

        public CubemapTexture FinalizeCapture() {
            _cubemapRenderTarget.GenerateMipMaps();
            _captureChunks = null;
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
