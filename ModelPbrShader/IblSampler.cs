using System;
using Engine;
using Engine.Graphics;
using Silk.NET.OpenGLES;

namespace Game {
    public class IblSampler : IDisposable {
        readonly int _ggxSampleCount;
        readonly int _lambertianSampleCount;
        readonly int _lowestMipLevel;
        readonly int _lutResolution;
        readonly int _sheenSampleCount;
        public int TextureSize { get; }

        public IblSampler(int textureSize = 128, int ggxLutResolution = 512, int lambertianSampleCount = 512, int ggxSampleCount = 256, int sheenSampleCount = 64, int lowestMipLevel = 4) {
            TextureSize = textureSize;
            _lutResolution = ggxLutResolution;
            _lambertianSampleCount = lambertianSampleCount;
            _ggxSampleCount = ggxSampleCount;
            _sheenSampleCount = sheenSampleCount;
            _lowestMipLevel = lowestMipLevel;
        }
        int _textureSize;
        CubemapTexture _sourceCubemap;
        bool _disposed;
        ComputeShader _computeShader;
        bool _lutGenerated;

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
            _computeShader?.Dispose();
            _computeShader = null;
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
        }

        public void BeginProcess(CubemapTexture cubemapTexture) {
            _textureSize = TextureSize;
            _sourceCubemap = cubemapTexture;
            int maxMipLevels = (int)Math.Floor(Math.Log2(TextureSize)) + 1;
            MipCount = Math.Min(maxMipLevels - _lowestMipLevel, 4);
            EnsureCubemapTextures(TextureSize, maxMipLevels);
            _computeShader ??= ComputeShader.Create(LoadShaderSource("ibl_filtering.comp"));
        }

        public void ProcessLambertian() {
            CubeMapToLambertian();
            CleanupComputeState();
        }

        public void ProcessGGX() {
            CubeMapToGGX();
            if (!_lutGenerated) {
                GenerateGGXLut();
                _lutGenerated = true;
            }
            CleanupComputeState();
            _sourceCubemap = null;
        }

        public void ProcessSheen() {
            CubeMapToSheen();
            GenerateCharlieLut();
            CleanupComputeState();
            _sourceCubemap = null;
        }

        void CleanupComputeState() {
            GLWrapper.UseProgram(0);
            GLWrapper.ActiveTexture(TextureUnit.Texture0);
            GLWrapper.BindTexture(TextureTarget.TextureCubeMap, 0, true);
        }

        void EnsureCubemapTextures(int size, int maxMipLevels) {
            if (LambertianTexture == null
                || LambertianTexture.Size != size) {
                LambertianTexture?.Dispose();
                LambertianTexture = CreateImmutableCubemap(size, 1);
                LambertianTexture.SetFilterMode(true);
            }
            if (GGXTexture == null
                || GGXTexture.MipLevelsCount != maxMipLevels) {
                GGXTexture?.Dispose();
                GGXTexture = CreateImmutableCubemap(size, maxMipLevels);
                GGXTexture.SetFilterMode(true);
            }
            if (SheenTexture == null
                || SheenTexture.MipLevelsCount != maxMipLevels) {
                SheenTexture?.Dispose();
                SheenTexture = CreateImmutableCubemap(size, maxMipLevels);
                SheenTexture.SetFilterMode(true);
            }
        }

        void CubeMapToLambertian() {
            ApplyFilterCompute(0, 0.0f, 0, LambertianTexture, _lambertianSampleCount);
        }

        void CubeMapToGGX() {
            for (int mipLevel = 0; mipLevel <= MipCount; mipLevel++) {
                float roughness = MipCount > 1 ? (float)Math.Pow((double)mipLevel / (MipCount - 1), 2) : 0.0f;
                ApplyFilterCompute(1, roughness, mipLevel, GGXTexture, _ggxSampleCount);
            }
        }

        void CubeMapToSheen() {
            const float minSheenRoughness = 0.05f;
            for (int mipLevel = 0; mipLevel <= MipCount; mipLevel++) {
                float roughness = MipCount > 1 ? (float)Math.Pow((double)mipLevel / (MipCount - 1), 2) : minSheenRoughness;
                roughness = Math.Max(roughness, minSheenRoughness);
                ApplyFilterCompute(2, roughness, mipLevel, SheenTexture, _sheenSampleCount);
            }
        }

        void ApplyFilterCompute(int distribution, float roughness, int targetMipLevel, CubemapTexture targetTexture, int sampleCount) {
            int mipSize = Math.Max(1, _textureSize >> targetMipLevel);
            int groups = (mipSize + 7) / 8;
            _computeShader.Use();
            _computeShader.BindImageCubemap(0, targetTexture, targetMipLevel);
            _computeShader.SetSamplerCube("u_cubemapTexture", 0, _sourceCubemap);
            _computeShader.SetFloat("u_roughness", roughness);
            _computeShader.SetInt("u_sampleCount", sampleCount);
            _computeShader.SetInt("u_width", mipSize);
            _computeShader.SetFloat("u_lodBias", 0.0f);
            _computeShader.SetInt("u_distribution", distribution);
            _computeShader.SetInt("u_isGeneratingLUT", 0);
            _computeShader.Dispatch(groups, groups, 6);
            ComputeShader.MemoryBarrier();
        }

        void GenerateGGXLut() {
            GGXLut = CreateImmutableTexture2D(_lutResolution, _lutResolution, 1);
            GGXLut.SamplerState = SamplerState.LinearClamp;
            SetLinearFilter(GGXLut);
            GenerateLutCompute(1, GGXLut);
        }

        void GenerateCharlieLut() {
            CharlieLut = CreateImmutableTexture2D(_lutResolution, _lutResolution, 1);
            CharlieLut.SamplerState = SamplerState.LinearClamp;
            SetLinearFilter(CharlieLut);
            GenerateLutCompute(2, CharlieLut);
        }

        void GenerateLutCompute(int distribution, Texture2D targetTexture) {
            int groups = (_lutResolution + 7) / 8;
            _computeShader.Use();
            _computeShader.BindImage2D(0, targetTexture, 0);
            _computeShader.SetSamplerCube("u_cubemapTexture", 0, _sourceCubemap);
            _computeShader.SetFloat("u_roughness", 0.0f);
            _computeShader.SetInt("u_sampleCount", 512);
            _computeShader.SetInt("u_width", _lutResolution);
            _computeShader.SetFloat("u_lodBias", 0.0f);
            _computeShader.SetInt("u_distribution", distribution);
            _computeShader.SetInt("u_isGeneratingLUT", 1);
            _computeShader.Dispatch(groups, groups);
            ComputeShader.MemoryBarrier();
        }

        static CubemapTexture CreateImmutableCubemap(int size, int mipLevels) {
            CubemapTexture tex = new();
            tex.Size = size;
            tex.ColorFormat = ColorFormat.Rgba16f;
            tex.MipLevelsCount = mipLevels;
            GLWrapper.GL.GenTextures(1, out uint glTex);
            GLWrapper.BindTexture(TextureTarget.TextureCubeMap, (int)glTex, true);
            GLWrapper.GL.TexStorage2D(TextureTarget.TextureCubeMap, (uint)mipLevels, SizedInternalFormat.Rgba16f, (uint)size, (uint)size);
            tex.m_texture = (int)glTex;
            return tex;
        }

        static Texture2D CreateImmutableTexture2D(int width, int height, int mipLevels) {
            Texture2D tex = new();
            tex.Width = width;
            tex.Height = height;
            tex.ColorFormat = ColorFormat.Rgba16f;
            tex.MipLevelsCount = mipLevels;
            GLWrapper.GL.GenTextures(1, out uint glTex);
            GLWrapper.BindTexture(TextureTarget.Texture2D, (int)glTex, true);
            GLWrapper.GL.TexStorage2D(TextureTarget.Texture2D, (uint)mipLevels, SizedInternalFormat.Rgba16f, (uint)width, (uint)height);
            tex.m_texture = (int)glTex;
            return tex;
        }

        static void SetLinearFilter(Texture2D texture) {
            GLWrapper.BindTexture(TextureTarget.Texture2D, texture.m_texture, true);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GLWrapper.GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }

        static string LoadShaderSource(string shaderName) {
            string path = Storage.CombinePaths("ModelPbrShaders", shaderName);
            System.IO.Stream stream = ContentManager.GetStream(path);
            return new System.IO.StreamReader(stream).ReadToEnd();
        }
    }
}