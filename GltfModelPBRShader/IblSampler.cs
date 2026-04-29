using System;
using System.IO;
using System.Text;
using Engine;
using Engine.Graphics;
using Silk.NET.OpenGLES;
using PrimitiveType = Silk.NET.OpenGLES.PrimitiveType;

namespace Game {
    /// <summary>
    /// IBL 预处理器，用于生成预过滤的环境贴图
    /// </summary>
    public class IblSampler : IDisposable {
        readonly int _ggxSampleCount = 1024;
        readonly int _lambertianSampleCount = 2048;
        readonly int _lowestMipLevel = 4;
        readonly int _lutResolution = 1024;
        readonly int _sheenSampleCount = 64;
        readonly int _textureSize = 256;
        uint _cubemapTexture;
        bool _disposed;
        uint _framebuffer;
        uint _iblFilteringShader;
        uint _iblFragShader;

        uint _inputTexture;
        uint _panoramaFragShader;
        uint _panoramaToCubemapShader;
        uint _panoramaVertShader;

        public uint LambertianTexture { get; private set; }
        public uint GGXTexture { get; private set; }
        public uint SheenTexture { get; private set; }
        public uint GGXLut { get; private set; }
        public uint CharlieLut { get; private set; }
        public int MipCount { get; private set; }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            if (_inputTexture != 0) {
                GLWrapper.GL.DeleteTexture(_inputTexture);
                _inputTexture = 0;
            }
            if (_cubemapTexture != 0) {
                GLWrapper.GL.DeleteTexture(_cubemapTexture);
                _cubemapTexture = 0;
            }
            if (LambertianTexture != 0) {
                GLWrapper.GL.DeleteTexture(LambertianTexture);
                LambertianTexture = 0;
            }
            if (GGXTexture != 0) {
                GLWrapper.GL.DeleteTexture(GGXTexture);
                GGXTexture = 0;
            }
            if (SheenTexture != 0) {
                GLWrapper.GL.DeleteTexture(SheenTexture);
                SheenTexture = 0;
            }
            if (GGXLut != 0) {
                GLWrapper.GL.DeleteTexture(GGXLut);
                GGXLut = 0;
            }
            if (CharlieLut != 0) {
                GLWrapper.GL.DeleteTexture(CharlieLut);
                CharlieLut = 0;
            }
            if (_framebuffer != 0) {
                GLWrapper.GL.DeleteFramebuffer(_framebuffer);
                _framebuffer = 0;
            }
            if (_panoramaToCubemapShader != 0) {
                GLWrapper.GL.DeleteProgram(_panoramaToCubemapShader);
                _panoramaToCubemapShader = 0;
            }
            if (_iblFilteringShader != 0) {
                GLWrapper.GL.DeleteProgram(_iblFilteringShader);
                _iblFilteringShader = 0;
            }
            if (_panoramaVertShader != 0) {
                GLWrapper.GL.DeleteShader(_panoramaVertShader);
                _panoramaVertShader = 0;
            }
            if (_panoramaFragShader != 0) {
                GLWrapper.GL.DeleteShader(_panoramaFragShader);
                _panoramaFragShader = 0;
            }
            if (_iblFragShader != 0) {
                GLWrapper.GL.DeleteShader(_iblFragShader);
                _iblFragShader = 0;
            }
        }

        /// <summary>
        /// 初始化并处理环境贴图
        /// </summary>
        public void Process(EnvironmentMap panorama) {
            int[] viewport = new int[4];
            GLWrapper.GL.GetInteger(GetPName.Viewport, viewport);
            InitShaders();
            CreateInputTexture(panorama);
            CreateCubemapTextures();
            _framebuffer = GLWrapper.GL.GenFramebuffer();
            PanoramaToCubeMap();
            CubeMapToLambertian();
            CubeMapToGGX();
            CubeMapToSheen();
            GenerateGGXLut();
            GenerateCharlieLut();

            // 清理临时 GL 资源
            GLWrapper.GL.DeleteTexture(_inputTexture);
            _inputTexture = 0;
            GLWrapper.GL.DeleteTexture(_cubemapTexture);
            _cubemapTexture = 0;
            GLWrapper.GL.DeleteFramebuffer(_framebuffer);
            _framebuffer = 0;
            GLWrapper.GL.DeleteProgram(_panoramaToCubemapShader);
            _panoramaToCubemapShader = 0;
            GLWrapper.GL.DeleteProgram(_iblFilteringShader);
            _iblFilteringShader = 0;
            GLWrapper.GL.DeleteShader(_panoramaVertShader);
            _panoramaVertShader = 0;
            GLWrapper.GL.DeleteShader(_panoramaFragShader);
            _panoramaFragShader = 0;
            GLWrapper.GL.DeleteShader(_iblFragShader);
            _iblFragShader = 0;

            // 恢复 GL 状态
            GLWrapper.GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GLWrapper.GL.UseProgram(0);
            GLWrapper.GL.ActiveTexture(TextureUnit.Texture0);
            GLWrapper.GL.BindTexture(TextureTarget.Texture2D, 0);
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, 0);
            GLWrapper.GL.Viewport(viewport[0], viewport[1], (uint)viewport[2], (uint)viewport[3]);

            // 重置 GLWrapper 内部缓存，强制后续渲染重新设置 GL 状态
            // IblSampler 通过 GLWrapper.GL 直接调用绕过了 GLWrapper 的缓存，
            // 导致缓存与实际 GL 状态不同步，必须在此处重置
            // 注意：引擎 API 未暴露 InvalidateCachedState() 方法，只能直接写字段。
            // 引擎新增缓存字段时需同步更新此处。
            GLWrapper.m_program = -1;
            GLWrapper.m_framebuffer = -1;
            GLWrapper.m_lastShader = null;
            GLWrapper.m_lastVertexDeclaration = null;
            GLWrapper.m_lastVertexOffset = IntPtr.Zero;
            GLWrapper.m_lastArrayBuffer = -1;
        }

        void InitShaders() {
            string vertSource = LoadShaderSource("fullscreen.vert");
            string panoramaFragSource = LoadShaderSource("panorama_to_cubemap.frag");
            string iblFragSource = LoadShaderSource("ibl_filtering.frag");
            _panoramaVertShader = CompileShader(vertSource, true, "fullscreen.vert");
            _panoramaFragShader = CompileShader(panoramaFragSource, false, "panorama_to_cubemap.frag");
            _iblFragShader = CompileShader(iblFragSource, false, "ibl_filtering.frag");
            _panoramaToCubemapShader = LinkProgram(_panoramaVertShader, _panoramaFragShader);
            _iblFilteringShader = LinkProgram(_panoramaVertShader, _iblFragShader);
        }

        string LoadShaderSource(string shaderName) {
            string path = Storage.CombinePaths("GltfModelPbrShaders", shaderName);
            Stream stream = ContentManager.GetStream(path);
            return new StreamReader(stream).ReadToEnd();
        }

        uint CompileShader(string source, bool isVertex, string name) {
            ShaderType type = isVertex ? ShaderType.VertexShader : ShaderType.FragmentShader;
            uint shader = GLWrapper.GL.CreateShader(type);

            // 预处理：添加 version 和 GLSL define
            string fullSource = PreprocessShader(source, isVertex);
            GLWrapper.GL.ShaderSource(shader, fullSource);
            GLWrapper.GL.CompileShader(shader);
            GLWrapper.GL.GetShader(shader, ShaderParameterName.CompileStatus, out int status);
            if (status == 0) {
                string log = GLWrapper.GL.GetShaderInfoLog(shader);
                GLWrapper.GL.DeleteShader(shader);
                throw new InvalidOperationException($"Shader compilation failed ({name}): {log}");
            }
            return shader;
        }

        string PreprocessShader(string source, bool isVertex) {
            StringBuilder sb = new();

            // 添加 #version
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

        uint LinkProgram(uint vertShader, uint fragShader) {
            uint program = GLWrapper.GL.CreateProgram();
            GLWrapper.GL.AttachShader(program, vertShader);
            GLWrapper.GL.AttachShader(program, fragShader);
            GLWrapper.GL.LinkProgram(program);
            GLWrapper.GL.GetProgram(program, ProgramPropertyARB.LinkStatus, out int status);
            if (status == 0) {
                string log = GLWrapper.GL.GetProgramInfoLog(program);
                GLWrapper.GL.DeleteProgram(program);
                throw new InvalidOperationException($"Program link failed: {log}");
            }
            return program;
        }

        unsafe void CreateInputTexture(EnvironmentMap panorama) {
            _inputTexture = GLWrapper.GL.GenTexture();
            GLWrapper.GL.ActiveTexture(TextureUnit.Texture0);
            GLWrapper.GL.BindTexture(TextureTarget.Texture2D, _inputTexture);
            int numPixels = panorama.Width * panorama.Height;
            float[] rgbaData = new float[numPixels * 4];
            for (int i = 0; i < numPixels; i++) {
                rgbaData[i * 4] = panorama.DataFloat[i * 3];
                rgbaData[i * 4 + 1] = panorama.DataFloat[i * 3 + 1];
                rgbaData[i * 4 + 2] = panorama.DataFloat[i * 3 + 2];
                rgbaData[i * 4 + 3] = 1.0f;
            }
            fixed (float* d = rgbaData) {
                GLWrapper.GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    InternalFormat.Rgba32f,
                    (uint)panorama.Width,
                    (uint)panorama.Height,
                    0,
                    PixelFormat.Rgba,
                    PixelType.Float,
                    d
                );
            }
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.MirroredRepeat);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.MirroredRepeat);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        void CreateCubemapTextures() {
            _cubemapTexture = CreateCubemapTexture(true);
            LambertianTexture = CreateCubemapTexture(false);
            GGXTexture = CreateCubemapTexture(true);
            SheenTexture = CreateCubemapTexture(true);
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, GGXTexture);
            GLWrapper.GL.GenerateMipmap(TextureTarget.TextureCubeMap);
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, SheenTexture);
            GLWrapper.GL.GenerateMipmap(TextureTarget.TextureCubeMap);
            MipCount = (int)Math.Floor(Math.Log2(_textureSize)) + 1 - _lowestMipLevel;
        }

        unsafe uint CreateCubemapTexture(bool withMipmaps) {
            uint texture = GLWrapper.GL.GenTexture();
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, texture);
            for (int i = 0; i < 6; i++) {
                GLWrapper.GL.TexImage2D(
                    TextureTarget.TextureCubeMapPositiveX + i,
                    0,
                    InternalFormat.Rgba16f,
                    (uint)_textureSize,
                    (uint)_textureSize,
                    0,
                    PixelFormat.Rgba,
                    PixelType.HalfFloat,
                    null
                );
            }
            if (withMipmaps) {
                GLWrapper.GL.TexParameter(
                    TextureTarget.TextureCubeMap,
                    TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear
                );
            }
            else {
                GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            }
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GLWrapper.GL.TexParameter(TextureTarget.TextureCubeMap, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return texture;
        }

        void PanoramaToCubeMap() {
            GLWrapper.GL.UseProgram(_panoramaToCubemapShader);
            int u_panoramaLoc = GLWrapper.GL.GetUniformLocation(_panoramaToCubemapShader, "u_panorama");
            int u_currentFaceLoc = GLWrapper.GL.GetUniformLocation(_panoramaToCubemapShader, "u_currentFace");
            for (int i = 0; i < 6; i++) {
                GLWrapper.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                GLWrapper.GL.FramebufferTexture2D(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + i,
                    _cubemapTexture,
                    0
                );
                GLWrapper.GL.Viewport(0, 0, (uint)_textureSize, (uint)_textureSize);
                GLWrapper.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GLWrapper.GL.ActiveTexture(TextureUnit.Texture0);
                GLWrapper.GL.BindTexture(TextureTarget.Texture2D, _inputTexture);
                GLWrapper.GL.Uniform1(u_panoramaLoc, 0);
                GLWrapper.GL.Uniform1(u_currentFaceLoc, i);
                GLWrapper.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
            GLWrapper.GL.GenerateMipmap(TextureTarget.TextureCubeMap);
        }

        void CubeMapToLambertian() {
            ApplyFilter(0, 0.0f, 0, LambertianTexture, _lambertianSampleCount);
        }

        void CubeMapToGGX() {
            for (int mipLevel = 0; mipLevel <= MipCount; mipLevel++) {
                float roughness = MipCount > 1 ? (float)Math.Pow((double)mipLevel / (MipCount - 1), 2) : 0.0f;
                ApplyFilter(1, roughness, mipLevel, GGXTexture, _ggxSampleCount);
            }
        }

        void CubeMapToSheen() {
            const float minSheenRoughness = 0.05f;
            for (int mipLevel = 0; mipLevel <= MipCount; mipLevel++) {
                float roughness = MipCount > 1 ? (float)Math.Pow((double)mipLevel / (MipCount - 1), 2) : minSheenRoughness;
                roughness = Math.Max(roughness, minSheenRoughness);
                ApplyFilter(2, roughness, mipLevel, SheenTexture, _sheenSampleCount);
            }
        }

        void ApplyFilter(int distribution, float roughness, int targetMipLevel, uint targetTexture, int sampleCount) {
            GLWrapper.GL.UseProgram(_iblFilteringShader);
            int u_cubemapTextureLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_cubemapTexture");
            int u_roughnessLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_roughness");
            int u_sampleCountLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_sampleCount");
            int u_widthLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_width");
            int u_lodBiasLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_lodBias");
            int u_distributionLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_distribution");
            int u_currentFaceLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_currentFace");
            int u_isGeneratingLUTLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_isGeneratingLUT");
            int u_floatTextureLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_floatTexture");
            int u_intensityScaleLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_intensityScale");
            int currentTextureSize = _textureSize >> targetMipLevel;
            currentTextureSize = Math.Max(1, currentTextureSize);
            for (int i = 0; i < 6; i++) {
                GLWrapper.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                GLWrapper.GL.FramebufferTexture2D(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + i,
                    targetTexture,
                    targetMipLevel
                );
                GLWrapper.GL.Viewport(0, 0, (uint)currentTextureSize, (uint)currentTextureSize);
                GLWrapper.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
                GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                GLWrapper.GL.ActiveTexture(TextureUnit.Texture0);
                GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
                GLWrapper.GL.Uniform1(u_cubemapTextureLoc, 0);
                GLWrapper.GL.Uniform1(u_roughnessLoc, roughness);
                GLWrapper.GL.Uniform1(u_sampleCountLoc, sampleCount);
                GLWrapper.GL.Uniform1(u_widthLoc, _textureSize);
                GLWrapper.GL.Uniform1(u_lodBiasLoc, 0.0f);
                GLWrapper.GL.Uniform1(u_distributionLoc, distribution);
                GLWrapper.GL.Uniform1(u_currentFaceLoc, i);
                GLWrapper.GL.Uniform1(u_isGeneratingLUTLoc, 0);
                GLWrapper.GL.Uniform1(u_floatTextureLoc, 1);
                GLWrapper.GL.Uniform1(u_intensityScaleLoc, 1.0f);
                GLWrapper.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        void GenerateGGXLut() {
            GGXLut = CreateLutTexture();
            SampleLut(1, GGXLut);
        }

        void GenerateCharlieLut() {
            CharlieLut = CreateLutTexture();
            SampleLut(2, CharlieLut);
        }

        unsafe uint CreateLutTexture() {
            uint texture = GLWrapper.GL.GenTexture();
            GLWrapper.GL.BindTexture(TextureTarget.Texture2D, texture);
            GLWrapper.GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                InternalFormat.Rgba16f,
                (uint)_lutResolution,
                (uint)_lutResolution,
                0,
                PixelFormat.Rgba,
                PixelType.HalfFloat,
                null
            );
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            return texture;
        }

        void SampleLut(int distribution, uint targetTexture) {
            GLWrapper.GL.UseProgram(_iblFilteringShader);
            int u_cubemapTextureLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_cubemapTexture");
            int u_roughnessLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_roughness");
            int u_sampleCountLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_sampleCount");
            int u_widthLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_width");
            int u_lodBiasLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_lodBias");
            int u_distributionLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_distribution");
            int u_currentFaceLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_currentFace");
            int u_isGeneratingLUTLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_isGeneratingLUT");
            int u_floatTextureLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_floatTexture");
            int u_intensityScaleLoc = GLWrapper.GL.GetUniformLocation(_iblFilteringShader, "u_intensityScale");
            GLWrapper.GL.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
            GLWrapper.GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                targetTexture,
                0
            );
            GLWrapper.GL.Viewport(0, 0, (uint)_lutResolution, (uint)_lutResolution);
            GLWrapper.GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);
            GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
            GLWrapper.GL.ActiveTexture(TextureUnit.Texture0);
            GLWrapper.GL.BindTexture(TextureTarget.TextureCubeMap, _cubemapTexture);
            GLWrapper.GL.Uniform1(u_cubemapTextureLoc, 0);
            GLWrapper.GL.Uniform1(u_roughnessLoc, 0.0f);
            GLWrapper.GL.Uniform1(u_sampleCountLoc, 512);
            GLWrapper.GL.Uniform1(u_widthLoc, 0);
            GLWrapper.GL.Uniform1(u_lodBiasLoc, 0.0f);
            GLWrapper.GL.Uniform1(u_distributionLoc, distribution);
            GLWrapper.GL.Uniform1(u_currentFaceLoc, 0);
            GLWrapper.GL.Uniform1(u_isGeneratingLUTLoc, 1);
            GLWrapper.GL.Uniform1(u_floatTextureLoc, 1);
            GLWrapper.GL.Uniform1(u_intensityScaleLoc, 1.0f);
            GLWrapper.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }
    }
}