using System;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;

namespace Game {
    /// <summary>
    /// 管理 Transmission 和 Scatter 帧缓冲区生命周期
    /// 延迟创建，尺寸变化时自动重建
    /// </summary>
    public class PbrFramebufferManager : IDisposable {
        RenderTarget2D _transmission;
        ScatterFramebuffer _scatter;
        bool _disposed;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public bool HasTransmissionFramebuffer => _transmission != null;
        public bool HasScatterFramebuffer => _scatter != null;

        public RenderTarget2D Transmission => _transmission;
        public ScatterFramebuffer Scatter => _scatter;

        public void SetSize(int width, int height) {
            Width = width;
            Height = height;
        }

        public void EnsureTransmissionFramebuffer() {
            if (_transmission == null
                || Width != _transmission.Width
                || Height != _transmission.Height) {
                _transmission?.Dispose();
                _transmission = new RenderTarget2D(Width, Height, 1, ColorFormat.Rgba16f, DepthFormat.Depth16);
            }
        }

        public void EnsureScatterFramebuffer() {
            if (_scatter == null
                || Width != _scatter.Width
                || Height != _scatter.Height) {
                _scatter?.Dispose();
                _scatter = new ScatterFramebuffer(Width, Height);
            }
        }

        public void BindTransmission() {
            if (_transmission != null) {
                GLWrapper.ApplyRenderTarget(_transmission);
                GLWrapper.m_viewport = null;
                GLWrapper.GL.Viewport(0, 0, (uint)Width, (uint)Height);
            }
        }

        public void BindScatter() {
            if (_scatter != null) {
                GLWrapper.BindFramebuffer(_scatter.m_frameBuffer);
                GLWrapper.m_viewport = null;
                GLWrapper.GL.Viewport(0, 0, (uint)Width, (uint)Height);
            }
        }

        public void ClearTransmission() {
            if (_transmission == null) return;
            GLWrapper.GL.ClearColor(0f, 0f, 0f, 1f);
            GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public void ClearScatter() => _scatter?.ClearTransparent();

        public void GenerateTransmissionMipmap() {
            if (_transmission == null) return;
            _transmission.GenerateMipMaps();
            // glGenerateMipmap 生成的 mip 层数有限，但 GL_TEXTURE_MAX_LEVEL 默认 1000
            // textureLod 采样超出实际层数时返回黑色，必须限制到实际最大层数
            int maxLevel = (int)Math.Floor(Math.Log2(Math.Max(Width, Height)));
            GLWrapper.BindTexture(TextureTarget.Texture2D, _transmission.m_texture, false);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, maxLevel);
        }

        public void BlitBackbufferToTransmission(int screenWidth, int screenHeight) =>
            _transmission?.BlitFromBackbuffer(screenWidth, screenHeight);

        public void UnbindFramebuffer() {
            GLWrapper.ApplyRenderTarget(null);
            GLWrapper.m_viewport = null;
        }

        public void BindTransmissionTexture() {
            if (_transmission == null) return;
            TextureUnit unit = (TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.TransmissionFramebuffer);
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(TextureTarget.Texture2D, _transmission.m_texture, true);
        }

        public void BindScatterTextures() {
            if (_scatter == null) return;
            GLWrapper.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterFramebuffer));
            GLWrapper.BindTexture(TextureTarget.Texture2D, _scatter.m_texture, true);
            _scatter.BindDepthTexture(
                (TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterDepthFramebuffer));
        }

        public void Dispose() {
            if (_disposed) return;
            _transmission?.Dispose();
            _scatter?.Dispose();
            _disposed = true;
        }
    }
}
