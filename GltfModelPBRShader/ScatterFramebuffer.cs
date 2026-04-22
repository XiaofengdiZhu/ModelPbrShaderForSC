using System;
using Engine.Graphics;
using Silk.NET.OpenGLES;

namespace Game {
    /// <summary>
    /// Scatter 帧缓冲区，用于 VolumeScatter 次表面散射
    /// 继承 RenderTarget2D，将深度 renderbuffer 替换为可采样的深度纹理
    /// </summary>
    public class ScatterFramebuffer : RenderTarget2D {
        public int DepthTextureHandle { get; private set; }

        public ScatterFramebuffer(int width, int height)
            : base(width, height, 1, ColorFormat.Rgba16f, DepthFormat.None) {
        }

        public override unsafe void AllocateRenderTarget() {
            // 基类创建 FBO + 颜色附件（DepthFormat.None 跳过深度 renderbuffer）
            base.AllocateRenderTarget();

            // 创建深度纹理（可被着色器采样）
            GLWrapper.GL.GenTextures(1, out uint depthTex);
            DepthTextureHandle = (int)depthTex;
            GLWrapper.BindTexture(TextureTarget.Texture2D, DepthTextureHandle, true);
            GLWrapper.GL.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.DepthComponent24,
                (uint)Width, (uint)Height, 0, PixelFormat.DepthComponent, PixelType.UnsignedInt, null);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)TextureMinFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)TextureMagFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)TextureWrapMode.ClampToEdge);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)TextureWrapMode.ClampToEdge);

            // 附加深度纹理到 FBO
            GLWrapper.BindFramebuffer(m_frameBuffer);
            GLWrapper.GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthAttachment, TextureTarget.Texture2D, depthTex, 0);

            GLEnum status = GLWrapper.GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != GLEnum.FramebufferComplete) {
                throw new InvalidOperationException($"ScatterFramebuffer incomplete: {status}");
            }
        }

        public override void DeleteRenderTarget() {
            if (DepthTextureHandle != 0) {
                GLWrapper.DeleteTexture(DepthTextureHandle);
                DepthTextureHandle = 0;
            }
            base.DeleteRenderTarget();
        }

        /// <summary>
        /// 清除为透明黑色（散射贡献为 0）
        /// </summary>
        public void ClearTransparent() {
            GLWrapper.GL.ClearColor(0f, 0f, 0f, 0f);
            GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        }

        public void BindDepthTexture(TextureUnit unit) {
            GLWrapper.ActiveTexture(unit);
            GLWrapper.BindTexture(TextureTarget.Texture2D, DepthTextureHandle, true);
        }
    }
}
