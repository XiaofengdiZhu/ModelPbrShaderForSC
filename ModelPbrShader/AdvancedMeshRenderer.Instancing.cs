using System;
using System.Numerics;
using Engine.Graphics;
using Silk.NET.OpenGLES;

namespace Game {
    partial class AdvancedMeshRenderer {
        protected void EnsureInstanceBuffer() {
            if (_instanceBufferCreated) {
                return;
            }
            unsafe {
                GLWrapper.GL.GenBuffers(1, out uint buffer);
                _instanceVBO = (int)buffer;
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVBO);
                GLWrapper.GL.BufferData(BufferTargetARB.ArrayBuffer, MaxInstancesPerBatch * 64, (void*)0, BufferUsageARB.DynamicDraw);
                GLWrapper.GL.GenBuffers(1, out uint lightBuffer);
                _instanceLightVBO = (int)lightBuffer;
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceLightVBO);
                GLWrapper.GL.BufferData(BufferTargetARB.ArrayBuffer, MaxInstancesPerBatch * 4, (void*)0, BufferUsageARB.DynamicDraw);
                GLWrapper.GL.GenBuffers(1, out uint iblBuffer);
                _instanceIblStrengthVBO = (int)iblBuffer;
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceIblStrengthVBO);
                GLWrapper.GL.BufferData(BufferTargetARB.ArrayBuffer, MaxInstancesPerBatch * 4, (void*)0, BufferUsageARB.DynamicDraw);
            }
            _instanceBufferCreated = true;
        }

        protected void UploadInstanceData(Matrix4x4[] matrices, int count) {
            EnsureInstanceBuffer();
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVBO);
            unsafe {
                fixed (Matrix4x4* ptr = matrices) {
                    GLWrapper.GL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(count * 64), ptr);
                }
            }
        }

        protected void UploadInstanceLightData(float[] lightData, int count) {
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceLightVBO);
            unsafe {
                fixed (float* ptr = lightData) {
                    GLWrapper.GL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(count * 4), ptr);
                }
            }
        }

        protected void UploadInstanceIblStrengthData(float[] iblStrengthData, int count) {
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceIblStrengthVBO);
            unsafe {
                fixed (float* ptr = iblStrengthData) {
                    GLWrapper.GL.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)(count * 4), ptr);
                }
            }
        }

        protected void SetupInstanceAttributes() {
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVBO);
            unsafe {
                for (int i = 0; i < 4; i++) {
                    uint loc = (uint)(8 + i);
                    GLWrapper.GL.VertexAttribPointer(loc, 4, VertexAttribPointerType.Float, false, 64, new IntPtr(i * 16).ToPointer());
                    GLWrapper.GL.EnableVertexAttribArray(loc);
                    GLWrapper.GL.VertexAttribDivisor(loc, 1);
                }
            }
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceLightVBO);
            unsafe {
                GLWrapper.GL.VertexAttribPointer(InstanceLightAttribLocation, 1, VertexAttribPointerType.Float, false, 4, (void*)0);
                GLWrapper.GL.EnableVertexAttribArray(InstanceLightAttribLocation);
                GLWrapper.GL.VertexAttribDivisor(InstanceLightAttribLocation, 1);
            }
            GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceIblStrengthVBO);
            unsafe {
                GLWrapper.GL.VertexAttribPointer(InstanceIblStrengthAttribLocation, 1, VertexAttribPointerType.Float, false, 4, (void*)0);
                GLWrapper.GL.EnableVertexAttribArray(InstanceIblStrengthAttribLocation);
                GLWrapper.GL.VertexAttribDivisor(InstanceIblStrengthAttribLocation, 1);
            }
        }

        protected void DisableInstanceAttributes() {
            for (int i = 0; i < 4; i++) {
                GLWrapper.GL.DisableVertexAttribArray((uint)(8 + i));
            }
            GLWrapper.GL.DisableVertexAttribArray(InstanceLightAttribLocation);
            GLWrapper.GL.DisableVertexAttribArray(InstanceIblStrengthAttribLocation);
        }

        /// <summary>
        /// 实例化绘制网格
        /// </summary>
        protected virtual void DrawMeshInstanced(ModelMesh mesh, int instanceCount) {
            if (mesh == null) {
                return;
            }
            GLWrapper.ApplyViewportScissor(Display.Viewport, Display.ScissorRectangle, Display.RasterizerState.ScissorTestEnable);
            VertexDeclaration lastDecl = null;
            foreach (ModelMeshPart part in mesh.MeshParts) {
                if (part?.VertexBuffer == null
                    || part.IndexBuffer == null) {
                    continue;
                }
                GLWrapper.BindBuffer(BufferTargetARB.ArrayBuffer, part.VertexBuffer.m_buffer);
                GLWrapper.BindBuffer(BufferTargetARB.ElementArrayBuffer, part.IndexBuffer.m_buffer);
                if (part.VertexBuffer.VertexDeclaration != lastDecl) {
                    SetupVertexAttributes(part.VertexBuffer.VertexDeclaration);
                    lastDecl = part.VertexBuffer.VertexDeclaration;
                }
                unsafe {
                    IntPtr indexOffset = new(part.StartIndex * part.IndexBuffer.IndexFormat.GetSize());
                    GLWrapper.GL.DrawElementsInstanced(
                        GLWrapper.TranslatePrimitiveType(part.PrimitiveType),
                        (uint)part.IndicesCount,
                        GLWrapper.TranslateIndexFormat(part.IndexBuffer.IndexFormat),
                        indexOffset.ToPointer(),
                        (uint)instanceCount
                    );
                }
            }
        }
    }
}