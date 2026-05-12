using System;
using System.Collections.Generic;
using Engine.Graphics;
using Silk.NET.OpenGLES;

namespace Game {
    public class ComputeShader : IDisposable {
        uint m_program;
        bool m_disposed;
        readonly Dictionary<string, int> _uniformCache = new();

        ComputeShader(uint program) => m_program = program;

        public static ComputeShader Create(string shaderSource) {
            string preprocessed = "#version 310 es\n#line 1\n" + shaderSource;
            uint shader = GLWrapper.GL.CreateShader(ShaderType.ComputeShader);
            GLWrapper.GL.ShaderSource(shader, preprocessed);
            GLWrapper.GL.CompileShader(shader);
            GLWrapper.GL.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0) {
                string log = GLWrapper.GL.GetShaderInfoLog(shader);
                GLWrapper.GL.DeleteShader(shader);
                throw new InvalidOperationException("Compute shader compilation failed: " + log);
            }
            uint program = GLWrapper.GL.CreateProgram();
            GLWrapper.GL.AttachShader(program, shader);
            GLWrapper.GL.LinkProgram(program);
            GLWrapper.GL.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linkStatus);
            if (linkStatus == 0) {
                string log = GLWrapper.GL.GetProgramInfoLog(program);
                GLWrapper.GL.DeleteProgram(program);
                GLWrapper.GL.DeleteShader(shader);
                throw new InvalidOperationException("Compute program link failed: " + log);
            }
            GLWrapper.GL.DeleteShader(shader);
            return new ComputeShader(program);
        }

        public void Use() {
            GLWrapper.UseProgram((int)m_program);
        }

        public int GetUniformLocation(string name) {
            if (!_uniformCache.TryGetValue(name, out int loc)) {
                loc = GLWrapper.GL.GetUniformLocation(m_program, name);
                _uniformCache[name] = loc;
            }
            return loc;
        }

        public void SetFloat(string name, float value) {
            int loc = GetUniformLocation(name);
            if (loc >= 0) {
                GLWrapper.GL.Uniform1(loc, value);
            }
        }

        public void SetInt(string name, int value) {
            int loc = GetUniformLocation(name);
            if (loc >= 0) {
                GLWrapper.GL.Uniform1(loc, value);
            }
        }

        public void SetSamplerCube(string name, int unit, CubemapTexture texture) {
            int loc = GetUniformLocation(name);
            if (loc < 0) {
                return;
            }
            GLWrapper.ActiveTexture(TextureUnit.Texture0 + unit);
            GLWrapper.GL.Uniform1(loc, unit);
            GLWrapper.BindTexture(TextureTarget.TextureCubeMap, texture?.m_texture ?? 0, true);
        }

        public void BindImageCubemap(int unit, CubemapTexture texture, int mipLevel) {
            GLWrapper.GL.BindImageTexture(
                (uint)unit,
                (uint)texture.m_texture,
                mipLevel,
                true,
                0,
                BufferAccessARB.WriteOnly,
                InternalFormat.Rgba16f
            );
        }

        public void BindImage2D(int unit, Texture2D texture, int mipLevel) {
            GLWrapper.GL.BindImageTexture(
                (uint)unit,
                (uint)texture.m_texture,
                mipLevel,
                false,
                0,
                BufferAccessARB.WriteOnly,
                InternalFormat.Rgba16f
            );
        }

        public void Dispatch(int groupsX, int groupsY, int groupsZ = 1) {
            GLWrapper.GL.DispatchCompute((uint)groupsX, (uint)groupsY, (uint)groupsZ);
        }

        public static void MemoryBarrier() {
            GLWrapper.GL.MemoryBarrier((uint)(MemoryBarrierMask.ShaderImageAccessBarrierBit | MemoryBarrierMask.TextureFetchBarrierBit));
        }

        public void Dispose() {
            if (m_disposed) {
                return;
            }
            m_disposed = true;
            GLWrapper.GL.DeleteProgram(m_program);
            m_program = 0;
        }
    }
}