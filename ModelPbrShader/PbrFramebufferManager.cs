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
        bool _disposed;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public bool HasTransmissionFramebuffer => Transmission != null;
        public bool HasScatterFramebuffer => Scatter != null;

        public RenderTarget2D Transmission { get; private set; }

        public ScatterFramebuffer Scatter { get; private set; }

        public void SetSize(int width, int height) {
            if (Width == width && Height == height) return;
            Width = width;
            Height = height;
            if (Transmission != null && (Width != Transmission.Width || Height != Transmission.Height)) {
                Transmission.Dispose();
                Transmission = null;
            }
            if (Scatter != null && (Width != Scatter.Width || Height != Scatter.Height)) {
                Scatter.Dispose();
                Scatter = null;
            }
        }

        public void EnsureTransmissionFramebuffer() {
            if (Transmission == null
                || Width != Transmission.Width
                || Height != Transmission.Height) {
                Transmission?.Dispose();
                Transmission = new RenderTarget2D(Width, Height, 1, ColorFormat.Rgba16f, DepthFormat.Depth16);
            }
        }

        public void EnsureScatterFramebuffer() {
            if (Scatter == null
                || Width != Scatter.Width
                || Height != Scatter.Height) {
                Scatter?.Dispose();
                Scatter = new ScatterFramebuffer(Width, Height);
            }
        }

        public void BindTransmission() {
            if (Transmission != null) {
                GLWrapper.ApplyRenderTarget(Transmission);
                GLWrapper.m_viewport = null;
                GLWrapper.GL.Viewport(0, 0, (uint)Width, (uint)Height);
            }
        }

        public void BindScatter() {
            if (Scatter != null) {
                GLWrapper.BindFramebuffer(Scatter.m_frameBuffer);
                GLWrapper.m_viewport = null;
                GLWrapper.GL.Viewport(0, 0, (uint)Width, (uint)Height);
            }
        }

        public void ClearTransmission() {
            if (Transmission == null) {
                return;
            }
            BindTransmission();
            GLWrapper.ClearColor(new Engine.Vector4(0f, 0f, 0f, 1f));
            GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public void ClearScatter() {
            if (Scatter == null) {
                return;
            }
            BindScatter();
            Scatter.ClearTransparent();
        }

        public void GenerateTransmissionMipmap() {
            if (Transmission == null) {
                return;
            }
            Transmission.GenerateMipMaps();
            // glGenerateMipmap 生成的 mip 层数有限，但 GL_TEXTURE_MAX_LEVEL 默认 1000
            // textureLod 采样超出实际层数时返回黑色，必须限制到实际最大层数
            int maxLevel = (int)Math.Floor(Math.Log2(Math.Max(Width, Height)));
            GLWrapper.BindTexture(TextureTarget.Texture2D, Transmission.m_texture, false);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMaxLevel, maxLevel);
        }

        public void BlitSourceToTransmission(RenderTarget2D source, Viewport sourceViewport) {
            if (Transmission == null) {
                return;
            }
            if (source == null) {
                Transmission.BlitFromBackbuffer(sourceViewport.Width, sourceViewport.Height);
            }
            else {
                Transmission.BlitFromRenderTarget(source, new Engine.Rectangle(
                    sourceViewport.X,
                    sourceViewport.Y,
                    sourceViewport.Width,
                    sourceViewport.Height
                ));
            }
        }

        public void UnbindFramebuffer() {
            GLWrapper.ApplyRenderTarget(null);
            GLWrapper.m_viewport = null;
        }

        public void BindTransmissionTexture() {
            if (Transmission == null) {
                return;
            }
            TextureUnit unit = (TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.TransmissionFramebuffer);
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(TextureTarget.Texture2D, Transmission.m_texture, true);
        }

        public void BindScatterTextures() {
            if (Scatter == null) {
                return;
            }
            GLWrapper.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterFramebuffer));
            GLWrapper.BindTexture(TextureTarget.Texture2D, Scatter.m_texture, true);
            Scatter.BindDepthTexture((TextureUnit)((int)TextureUnit.Texture0 + (int)MaterialTextureSlot.ScatterDepthFramebuffer));
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            Transmission?.Dispose();
            Scatter?.Dispose();
            _disposed = true;
        }
    }
}
