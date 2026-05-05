using System;
using Engine;
using Engine.Graphics;
using Silk.NET.OpenGLES;
using Shader = Engine.Graphics.Shader;

namespace Game {
    public class IblFilteringShader : Shader {
        ShaderParameter m_cubemapParam;
        ShaderParameter m_roughnessParam;
        ShaderParameter m_sampleCountParam;
        ShaderParameter m_widthParam;
        ShaderParameter m_lodBiasParam;
        ShaderParameter m_distributionParam;
        ShaderParameter m_currentFaceParam;
        ShaderParameter m_isGeneratingLUTParam;
        ShaderParameter m_floatTextureParam;
        ShaderParameter m_intensityScaleParam;

        IblFilteringShader(uint programHandle) : base(programHandle) {
            m_cubemapParam = GetParameter("u_cubemapTexture", true);
            m_roughnessParam = GetParameter("u_roughness", true);
            m_sampleCountParam = GetParameter("u_sampleCount", true);
            m_widthParam = GetParameter("u_width", true);
            m_lodBiasParam = GetParameter("u_lodBias", true);
            m_distributionParam = GetParameter("u_distribution", true);
            m_currentFaceParam = GetParameter("u_currentFace", true);
            m_isGeneratingLUTParam = GetParameter("u_isGeneratingLUT", true);
            m_floatTextureParam = GetParameter("u_floatTexture", true);
            m_intensityScaleParam = GetParameter("u_intensityScale", true);
        }

        public override void PrepareForDrawing() {
            m_glymulParameter.SetValue(1f);
        }

        public CubemapTexture CubemapTexture { set => m_cubemapParam.SetValue(value); }
        public float Roughness { set => m_roughnessParam.SetValue(value); }
        public int SampleCount { set => m_sampleCountParam.SetValue(value); }
        public int Width { set => m_widthParam.SetValue(value); }
        public float LodBias { set => m_lodBiasParam.SetValue(value); }
        public int Distribution { set => m_distributionParam.SetValue(value); }
        public int CurrentFace { set => m_currentFaceParam.SetValue(value); }
        public int IsGeneratingLUT { set => m_isGeneratingLUTParam.SetValue(value); }
        public int FloatTexture { set => m_floatTextureParam.SetValue(value); }
        public float IntensityScale { set => m_intensityScaleParam.SetValue(value); }

        public static IblFilteringShader Create() {
            string vertSource = LoadShaderSource("fullscreen.vert");
            string fragSource = LoadShaderSource("ibl_filtering.frag");
            uint vertShader = CompileStage(vertSource, true, "fullscreen.vert");
            uint fragShader = CompileStage(fragSource, false, "ibl_filtering.frag");
            uint program = Link(vertShader, fragShader);
            GLWrapper.GL.DeleteShader(vertShader);
            GLWrapper.GL.DeleteShader(fragShader);
            return new IblFilteringShader(program);
        }

        public void FlushUniforms() {
            PrepareForDrawing();
            GLWrapper.UseProgram(m_program);
            foreach (ShaderParameter param in m_parameters) {
                if (!param.IsChanged) continue;
                switch (param.Type) {
                    case ShaderParameterType.Float:
                        GLWrapper.GL.Uniform1(param.Location, (uint)param.Count, param.Value);
                        break;
                    case ShaderParameterType.Int:
                        GLWrapper.GL.Uniform1(param.Location, (uint)param.Count, param.IntValue);
                        break;
                    case ShaderParameterType.SamplerCube:
                        GLWrapper.ActiveTexture(TextureUnit.Texture0);
                        GLWrapper.GL.Uniform1(param.Location, 0);
                        CubemapTexture cube = (CubemapTexture)param.Resource;
                        GLWrapper.BindTexture(TextureTarget.TextureCubeMap, cube?.m_texture ?? 0, true);
                        break;
                }
                param.IsChanged = false;
            }
        }

        static string LoadShaderSource(string shaderName) {
            string path = Storage.CombinePaths("GltfModelPbrShaders", shaderName);
            System.IO.Stream stream = ContentManager.GetStream(path);
            return new System.IO.StreamReader(stream).ReadToEnd();
        }

        static uint CompileStage(string source, bool isVertex, string name) {
            ShaderType type = isVertex ? ShaderType.VertexShader : ShaderType.FragmentShader;
            uint shader = GLWrapper.GL.CreateShader(type);
            string preprocessed = Preprocess(source, isVertex);
            GLWrapper.GL.ShaderSource(shader, preprocessed);
            GLWrapper.GL.CompileShader(shader);
            GLWrapper.GL.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0) {
                string log = GLWrapper.GL.GetShaderInfoLog(shader);
                GLWrapper.GL.DeleteShader(shader);
                throw new InvalidOperationException(string.Format("Shader compilation failed ({0}): {1}", name, log));
            }
            return shader;
        }

        static string Preprocess(string source, bool isVertex) {
            System.Text.StringBuilder sb = new();
            sb.AppendLine("#version 300 es");
            sb.AppendLine("#define GLSL");
            if (isVertex) {
                sb.AppendLine("uniform float u_glymul;");
                sb.AppendLine("#define OPENGL_POSITION_FIX gl_Position.y *= u_glymul;");
            }
            sb.AppendLine("#line 1");
            sb.Append(source);
            return sb.ToString();
        }

        static uint Link(uint vertShader, uint fragShader) {
            uint program = GLWrapper.GL.CreateProgram();
            GLWrapper.GL.AttachShader(program, vertShader);
            GLWrapper.GL.AttachShader(program, fragShader);
            GLWrapper.GL.LinkProgram(program);
            GLWrapper.GL.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0) {
                string log = GLWrapper.GL.GetProgramInfoLog(program);
                GLWrapper.GL.DeleteProgram(program);
                throw new InvalidOperationException(string.Format("Program link failed: {0}", log));
            }
            return program;
        }
    }
}
