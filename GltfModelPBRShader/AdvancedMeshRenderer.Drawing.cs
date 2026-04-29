using System;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;

namespace Game {
    partial class AdvancedMeshRenderer {
        protected virtual void SetupDepthState(ModelMaterial material) {
            GLWrapper.ApplyDepthStencilState(DepthStencilState.Default);
        }

        protected virtual void SetupCullMode(ModelMaterial material, bool isNegativeScale = false) {
            RasterizerState state = isNegativeScale ? RasterizerState.CullClockwiseScissor : RasterizerState.CullCounterClockwiseScissor;
            GLWrapper.ApplyRasterizerState(state);
            if (material?.DoubleSided == true) {
                GLWrapper.ApplyRasterizerState(RasterizerState.CullNoneScissor);
            }
        }

        protected virtual void SetupBlendMode(ModelMaterial material, RenderContext context) {
            if (material?.Transmission?.IsEnabled == true
                && context.UseLinearOutput) {
                GLWrapper.ApplyBlendState(BlendState.Opaque);
                return;
            }
            ModelAlphaMode alphaMode = material?.AlphaMode ?? ModelAlphaMode.Opaque;
            GLWrapper.ApplyBlendState(alphaMode == ModelAlphaMode.Blend ? BlendState.NonPremultiplied : BlendState.Opaque);
        }

        protected virtual void DrawMeshPart(ModelMeshPart part) {
            if (part?.VertexBuffer == null
                || part.IndexBuffer == null) {
                return;
            }
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, part.VertexBuffer.m_buffer);
            GLWrapper.BindBuffer(BufferTargetARB.ElementArrayBuffer, part.IndexBuffer.m_buffer);
            SetupVertexAttributes(part.VertexBuffer.VertexDeclaration);
            unsafe {
                IntPtr indexOffset = new(part.StartIndex * part.IndexBuffer.IndexFormat.GetSize());
                GLWrapper.GL.DrawElements(
                    GLWrapper.TranslatePrimitiveType(part.PrimitiveType),
                    (uint)part.IndicesCount,
                    GLWrapper.TranslateIndexFormat(part.IndexBuffer.IndexFormat),
                    indexOffset.ToPointer()
                );
            }
        }

        protected virtual void SetupVertexAttributes(VertexDeclaration declaration) {
            if (declaration == null) {
                return;
            }
            for (int i = 0; i < 8; i++) {
                GLWrapper.VertexAttribArray(i, false);
            }
            foreach (VertexElement element in declaration.VertexElements) {
                int location = SemanticToLocation(element.Semantic);
                if (location < 0) {
                    continue;
                }
                GLWrapper.TranslateVertexElementFormat(element.Format, out VertexAttribPointerType type, out bool normalize);
                int size = element.Format.GetElementsCount();
                int stride = declaration.VertexStride;
                unsafe {
                    GLWrapper.GL.VertexAttribPointer((uint)location, size, type, normalize, (uint)stride, new IntPtr(element.Offset).ToPointer());
                }
                GLWrapper.VertexAttribArray(location, true);
            }
        }

        protected static int SemanticToLocation(string semantic) {
            return semantic switch {
                "POSITION" => 0,
                "NORMAL" => 1,
                "TEXCOORD" => 2,
                "TEXCOORD0" => 2,
                "TEXCOORD1" => 3,
                "TEXCOORD2" => -1,
                "COLOR" => 4,
                "TANGENT" => 5,
                "BLENDINDICES" => 6,
                "BLENDWEIGHTS" => 7,
                _ => -1
            };
        }
    }
}