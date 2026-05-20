using System;

namespace Game {
    public class ModelPbrShaderSettings {
        public static bool Dirty;

        static int _environmentReflection = VersionsManager.CurrentPlatform switch {
            VersionsManager.Platform.Windows or VersionsManager.Platform.Linux => 1,
            _ => 0
        };
        static int _reflectionQuality = 1;
        static int _captureFrequency = 1;

        public static int EnvironmentReflection {
            get => _environmentReflection;
            set {
                int clamped = Math.Clamp(value, 0, 1);
                if (_environmentReflection != clamped) {
                    _environmentReflection = clamped;
                    Dirty = true;
                }
            }
        }

        public static int ReflectionQuality {
            get => _reflectionQuality;
            set {
                int clamped = Math.Clamp(value, 0, 2);
                if (_reflectionQuality != clamped) {
                    _reflectionQuality = clamped;
                    Dirty = true;
                }
            }
        }

        public static int CaptureFrequency {
            get => _captureFrequency;
            set {
                int clamped = Math.Clamp(value, 0, 2);
                if (_captureFrequency != clamped) {
                    _captureFrequency = clamped;
                    Dirty = true;
                }
            }
        }

        public static bool IsIblEnabled => _environmentReflection > 0;

        // 画质预设：IblSampler 参数
        static readonly int[] FaceSizes = [128, 256, 256];
        static readonly int[] TextureSizes = [64, 128, 256];
        static readonly int[] LambertianSampleCounts = [64, 128, 256];
        static readonly int[] GgxSampleCounts = [32, 64, 256];
        static readonly int[] SheenSampleCounts = [64, 64, 64];
        static readonly int[] LowestMipLevels = [2, 2, 4];

        public static int GetFaceSize() => FaceSizes[_reflectionQuality];
        public static int GetTextureSize() => TextureSizes[_reflectionQuality];
        public static int GetLambertianSampleCount() => LambertianSampleCounts[_reflectionQuality];
        public static int GetGgxSampleCount() => GgxSampleCounts[_reflectionQuality];
        public static int GetSheenSampleCount() => SheenSampleCounts[_reflectionQuality];
        public static int GetLowestMipLevel() => LowestMipLevels[_reflectionQuality];
        public static bool IncludeSheen() => _reflectionQuality >= 1;

        // 捕获频率预设
        static readonly float[] MoveDistanceThresholds = [3.0f, 1.5f, 0.5f];
        static readonly float[] TimeThresholdsNear = [3.0f, 1.0f, 0.5f];
        static readonly float[] TimeThresholdsFar = [8.0f, 3.0f, 1.0f];

        public static float GetMoveDistanceThreshold() => MoveDistanceThresholds[_captureFrequency];
        public static float GetTimeThresholdNear() => TimeThresholdsNear[_captureFrequency];
        public static float GetTimeThresholdFar() => TimeThresholdsFar[_captureFrequency];
    }
}
