using System;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using PrimitiveType = Silk.NET.OpenGLES.PrimitiveType;

namespace Game {
    public class IblSampler : IDisposable {
        readonly int _ggxSampleCount = 1024;
        readonly int _lambertianSampleCount = 2048;
        readonly int _lowestMipLevel = 4;
        readonly int _lutResolution = 1024;
        readonly int _sheenSampleCount = 64;
        int _textureSize = 256;
        CubemapTexture _sourceCubemap;
        bool _disposed;
        CubemapRenderTarget _tempRenderTarget;
        IblFilteringShader _iblFilteringShader;
        RenderTarget2D _lutRenderTarget;

        public CubemapTexture LambertianTexture { get; private set; }
        public CubemapTexture GGXTexture { get; private set; }
        public CubemapTexture SheenTexture { get; private set; }
        public Texture2D GGXLut { get; private set; }
        public Texture2D CharlieLut { get; private set; }
        public int MipCount { get; private set; }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _sourceCubemap?.Dispose();
            _sourceCubemap = null;
            LambertianTexture?.Dispose();
            LambertianTexture = null;
            GGXTexture?.Dispose();
            GGXTexture = null;
            SheenTexture?.Dispose();
            SheenTexture = null;
            GGXLut?.Dispose();
            GGXLut = null;
            CharlieLut?.Dispose();
            CharlieLut = null;
            _tempRenderTarget?.Dispose();
            _tempRenderTarget = null;
            _lutRenderTarget?.Dispose();
            _lutRenderTarget = null;
            _iblFilteringShader?.Dispose();
            _iblFilteringShader = null;
        }

        public void Process(CubemapTexture cubemapTexture, int size) {
            Viewport savedViewport = Display.Viewport;

            GLWrapper.Disable(EnableCap.CullFace);
            GLWrapper.Disable(EnableCap.ScissorTest);

            try {
                _textureSize = size;
                _sourceCubemap = cubemapTexture;

                int maxMipLevels = (int)Math.Floor(Math.Log2(size)) + 1;
                LambertianTexture = new CubemapTexture(size, 1, ColorFormat.Rgba16f);
                LambertianTexture.SetFilterMode(true);
                GGXTexture = new CubemapTexture(size, maxMipLevels, ColorFormat.Rgba16f);
                SheenTexture = new CubemapTexture(size, maxMipLevels, ColorFormat.Rgba16f);

                GGXTexture.SetFilterMode(true);
                GGXTexture.GenerateMipMaps();
                SheenTexture.SetFilterMode(true);
                SheenTexture.GenerateMipMaps();

                MipCount = maxMipLevels - _lowestMipLevel;

                _tempRenderTarget = new CubemapRenderTarget(size, 1, ColorFormat.Rgba16f, DepthFormat.None);

                _iblFilteringShader = IblFilteringShader.Create();
                CubeMapToLambertian();
                CubeMapToGGX();
                CubeMapToSheen();
                GenerateGGXLut();
                GenerateCharlieLut();

                _tempRenderTarget.Dispose();
                _tempRenderTarget = null;
                _iblFilteringShader.Dispose();
                _iblFilteringShader = null;
            }
            finally {
                GLWrapper.UseProgram(0);
                GLWrapper.ActiveTexture(TextureUnit.Texture0);
                GLWrapper.BindTexture(TextureTarget.Texture2D, 0, true);
                GLWrapper.BindTexture(TextureTarget.TextureCubeMap, 0, true);
                Display.Viewport = savedViewport;

                GLWrapper.Enable(EnableCap.CullFace);
                GLWrapper.Enable(EnableCap.ScissorTest);

                _sourceCubemap = null;
            }
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

        void ApplyFilter(int distribution, float roughness, int targetMipLevel, CubemapTexture targetTexture, int sampleCount) {
            _iblFilteringShader.CubemapTexture = _sourceCubemap;
            _iblFilteringShader.Roughness = roughness;
            _iblFilteringShader.SampleCount = sampleCount;
            _iblFilteringShader.Width = _textureSize;
            _iblFilteringShader.LodBias = 0.0f;
            _iblFilteringShader.Distribution = distribution;
            _iblFilteringShader.IsGeneratingLUT = 0;
            _iblFilteringShader.FloatTexture = 1;
            _iblFilteringShader.IntensityScale = 1.0f;

            int currentTextureSize = Math.Max(1, _textureSize >> targetMipLevel);

            for (int i = 0; i < 6; i++) {
                GLWrapper.BindFramebuffer(_tempRenderTarget.m_frameBuffer);
                GLWrapper.GL.FramebufferTexture2D(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + i,
                    (uint)targetTexture.m_texture,
                    targetMipLevel
                );
                GLWrapper.GL.Viewport(0, 0, (uint)currentTextureSize, (uint)currentTextureSize);
                GLWrapper.m_viewport = null;
                GLWrapper.ClearColor(new Engine.Vector4(0.0f, 0.0f, 0.0f, 1.0f));
                GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                _iblFilteringShader.CurrentFace = i;
                _iblFilteringShader.FlushUniforms();

                GLWrapper.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
            }
        }

        void GenerateGGXLut() {
            GGXLut = new Texture2D(_lutResolution, _lutResolution, 1, ColorFormat.Rgba16f);
            SetLinearFilter(GGXLut);
            _lutRenderTarget = new RenderTarget2D(_lutResolution, _lutResolution, 1, ColorFormat.Rgba16f, DepthFormat.None);
            SampleLut(1, GGXLut);
        }

        void GenerateCharlieLut() {
            CharlieLut = new Texture2D(_lutResolution, _lutResolution, 1, ColorFormat.Rgba16f);
            SetLinearFilter(CharlieLut);
            if (_lutRenderTarget == null) {
                _lutRenderTarget = new RenderTarget2D(_lutResolution, _lutResolution, 1, ColorFormat.Rgba16f, DepthFormat.None);
            }
            SampleLut(2, CharlieLut);
        }

        static void SetLinearFilter(Texture2D texture) {
            GLWrapper.BindTexture(TextureTarget.Texture2D, texture.m_texture, true);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        }

        void SampleLut(int distribution, Texture2D targetTexture) {
            _iblFilteringShader.CubemapTexture = _sourceCubemap;
            _iblFilteringShader.Roughness = 0.0f;
            _iblFilteringShader.SampleCount = 512;
            _iblFilteringShader.Width = 0;
            _iblFilteringShader.LodBias = 0.0f;
            _iblFilteringShader.Distribution = distribution;
            _iblFilteringShader.CurrentFace = 0;
            _iblFilteringShader.IsGeneratingLUT = 1;
            _iblFilteringShader.FloatTexture = 1;
            _iblFilteringShader.IntensityScale = 1.0f;

            GLWrapper.BindFramebuffer(_lutRenderTarget.m_frameBuffer);
            GLWrapper.GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                (uint)targetTexture.m_texture,
                0
            );
            GLWrapper.GL.Viewport(0, 0, (uint)_lutResolution, (uint)_lutResolution);
            GLWrapper.m_viewport = null;
            GLWrapper.ClearColor(new Engine.Vector4(0.0f, 0.0f, 0.0f, 1.0f));
            GLWrapper.GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            _iblFilteringShader.FlushUniforms();
            GLWrapper.GL.DrawArrays(PrimitiveType.Triangles, 0, 3);
        }

        static readonly string[] FaceNames = ["posX", "negX", "posY", "negY", "posZ", "negZ"];

        public unsafe void SaveIblResults(int processCount) {
            try {
                string outputDir = RunPath.GetOperatingPath();

                SaveCubemapTexture(LambertianTexture, _textureSize, 0, outputDir, processCount, "lambertian");
                for (int mip = 0; mip <= Math.Min(MipCount, 2); mip++) {
                    int mipSize = Math.Max(1, _textureSize >> mip);
                    SaveCubemapTexture(GGXTexture, mipSize, mip, outputDir, processCount, string.Format("ggx_mip{0}", mip));
                }
                SaveLutTexture(GGXLut, outputDir, processCount, "ggx_lut");

                Log.Information(string.Format("[glTF PBR Shader] Saved IBL results to {0}", outputDir));
            }
            catch (Exception ex) {
                Log.Warning(string.Format("[glTF PBR Shader] Failed to save IBL results: {0}", ex.Message));
            }
        }

        unsafe void SaveCubemapTexture(CubemapTexture texture, int faceSize, int mipLevel, string outputDir, int processCount, string prefix) {
            using CubemapRenderTarget tempRt = new(faceSize, 1, ColorFormat.Rgba16f, DepthFormat.None);
            for (int face = 0; face < 6; face++) {
                GLWrapper.BindFramebuffer(tempRt.m_frameBuffer);
                GLWrapper.GL.FramebufferTexture2D(
                    FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0,
                    TextureTarget.TextureCubeMapPositiveX + face,
                    (uint)texture.m_texture,
                    mipLevel
                );

                Half[] halfPixels = new Half[faceSize * faceSize * 4];
                fixed (Half* ptr = halfPixels) {
                    GLWrapper.GL.ReadPixels(0, 0, (uint)faceSize, (uint)faceSize, PixelFormat.Rgba, PixelType.HalfFloat, ptr);
                }

                var sharpImage = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                    Image.DefaultImageSharpConfiguration, faceSize, faceSize);
                for (int y = 0; y < faceSize; y++) {
                    for (int x = 0; x < faceSize; x++) {
                        int srcY = faceSize - 1 - y;
                        int srcIdx = (srcY * faceSize + x) * 4;
                        float r = (float)halfPixels[srcIdx];
                        float g = (float)halfPixels[srcIdx + 1];
                        float b = (float)halfPixels[srcIdx + 2];
                        sharpImage[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                            (byte)Math.Clamp(r * 255, 0, 255),
                            (byte)Math.Clamp(g * 255, 0, 255),
                            (byte)Math.Clamp(b * 255, 0, 255),
                            255
                        );
                    }
                }

                var image = new Engine.Media.Image(sharpImage);
                string filename = string.Format("{0}_ibl_{1}_{2}.png", processCount, prefix, FaceNames[face]);
                string filepath = Storage.CombinePaths(outputDir, filename);
                Engine.Media.Image.Save(image, filepath, ImageFileFormat.Png, true);
                Log.Information(string.Format("[glTF PBR Shader] Saved: {0}", filepath));
            }
        }

        unsafe void SaveLutTexture(Texture2D texture, string outputDir, int processCount, string prefix) {
            using RenderTarget2D tempRt = new(_lutResolution, _lutResolution, 1, ColorFormat.Rgba16f, DepthFormat.None);
            GLWrapper.BindFramebuffer(tempRt.m_frameBuffer);
            GLWrapper.GL.FramebufferTexture2D(
                FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D,
                (uint)texture.m_texture,
                0
            );

            int size = _lutResolution;
            Half[] halfPixels = new Half[size * size * 4];
            fixed (Half* ptr = halfPixels) {
                GLWrapper.GL.ReadPixels(0, 0, (uint)size, (uint)size, PixelFormat.Rgba, PixelType.HalfFloat, ptr);
            }

            var sharpImage = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(
                Image.DefaultImageSharpConfiguration, size, size);
            for (int y = 0; y < size; y++) {
                for (int x = 0; x < size; x++) {
                    int srcY = size - 1 - y;
                    int srcIdx = (srcY * size + x) * 4;
                    float r = (float)halfPixels[srcIdx];
                    float g = (float)halfPixels[srcIdx + 1];
                    float b = (float)halfPixels[srcIdx + 2];
                    sharpImage[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(
                        (byte)Math.Clamp(r * 255, 0, 255),
                        (byte)Math.Clamp(g * 255, 0, 255),
                        (byte)Math.Clamp(b * 255, 0, 255),
                        255
                    );
                }
            }

            var image = new Engine.Media.Image(sharpImage);
            string filename = string.Format("{0}_ibl_{1}.png", processCount, prefix);
            string filepath = Storage.CombinePaths(outputDir, filename);
            Engine.Media.Image.Save(image, filepath, ImageFileFormat.Png, true);
            Log.Information(string.Format("[glTF PBR Shader] Saved: {0}", filepath));
        }
    }
}
