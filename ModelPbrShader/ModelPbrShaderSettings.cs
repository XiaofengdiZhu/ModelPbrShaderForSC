using System;
using System.Xml.Linq;

namespace Game {
    /// <summary>
    /// 运行时设置缓存门面：数据源为 ModSettingsManager（modinfo.json 数据驱动设置）。
    /// 热路径每帧读取，故在装载/变更时缓存到静态字段，避免字典查找与字符串拼接。
    /// </summary>
    public class ModelPbrShaderSettings {
        public static bool Dirty;

        public const string PackageName = "xfdz.ModelPbrShader";
        public const string PageId = "Settings";
        public const string EnvironmentReflectionId = "EnvironmentReflection";
        public const string ReflectionQualityId = "ReflectionQuality";
        public const string CaptureFrequencyId = "CaptureFrequency";
        public const string DebugChannelId = "DebugChannel";

        static bool _environmentReflection;
        static ReflectionQuality _reflectionQuality = ReflectionQuality.Medium;
        static CaptureFrequency _captureFrequency = CaptureFrequency.Medium;
        static DebugChannel _debugChannel = DebugChannel.None;

        public static bool EnvironmentReflection => _environmentReflection;
        public static ReflectionQuality ReflectionQuality => _reflectionQuality;
        public static CaptureFrequency CaptureFrequency => _captureFrequency;
        public static bool IsIblEnabled => _environmentReflection;
        public static DebugChannel DebugChannel => _debugChannel;

        /// <summary>从 ModSettingsManager 装载全部设置项到缓存。在设置值就绪后（进存档时）调用。</summary>
        public static void LoadFromModSettings() {
            _environmentReflection = ModSettingsManager.Get<bool>(PackageName, PageId, EnvironmentReflectionId);
            _reflectionQuality = ModSettingsManager.Get<ReflectionQuality>(PackageName, PageId, ReflectionQualityId);
            _captureFrequency = ModSettingsManager.Get<CaptureFrequency>(PackageName, PageId, CaptureFrequencyId);
            _debugChannel = ModSettingsManager.Get<DebugChannel>(PackageName, PageId, DebugChannelId);
            Dirty = true;
        }

        /// <summary>
        /// 首次运行（持久化中无 EnvironmentReflection）且为桌面平台时，写入开启动态 IBL 的默认值，
        /// 复刻旧逻辑（桌面默认开、安卓默认关）。已持久化则尊重玩家设置，不再覆盖。
        /// </summary>
        public static void ApplyPlatformDefaultOnFirstRun() {
            if (HasPersisted(EnvironmentReflectionId)) return;
            bool desktop = VersionsManager.CurrentPlatform is VersionsManager.Platform.Windows
                or VersionsManager.Platform.Linux;
            if (desktop) {
                ModSettingsManager.Set(new[] { PackageName, PageId, EnvironmentReflectionId }, true);
            }
        }

        /// <summary>ModLoader.OnModSettingChanged 收到变更时调用，更新缓存并标记 Dirty。</summary>
        public static void ApplySettingChange(string itemId, object value) {
            switch (itemId) {
                case EnvironmentReflectionId:
                    _environmentReflection = value is bool b ? b : Convert.ToBoolean(value);
                    break;
                case ReflectionQualityId:
                    _reflectionQuality = (ReflectionQuality)value;
                    break;
                case CaptureFrequencyId:
                    _captureFrequency = (CaptureFrequency)value;
                    break;
                case DebugChannelId:
                    _debugChannel = (DebugChannel)value;
                    break;
                default:
                    return;
            }
            Dirty = true;
        }

        /// <summary>持久化（ModSettingsCache 的 DataDrivenSettings）中是否已记录指定 item。</summary>
        static bool HasPersisted(string itemId) {
            if (!ModSettingsManager.ModSettingsCache.TryGetValue(PackageName, out XElement modEl)) return false;
            XElement dd = modEl.Element("DataDrivenSettings");
            if (dd == null) return false;
            string path = $"{PageId}/{itemId}";
            foreach (XElement itemEl in dd.Elements("Item")) {
                if (itemEl.Attribute("Path")?.Value == path) return true;
            }
            return false;
        }

        // 画质预设：IblSampler 参数
        static readonly int[] FaceSizes = [128, 256, 256];
        static readonly int[] TextureSizes = [64, 128, 256];
        static readonly int[] LambertianSampleCounts = [64, 128, 256];
        static readonly int[] GgxSampleCounts = [32, 64, 256];
        static readonly int[] SheenSampleCounts = [64, 64, 64];
        static readonly int[] LowestMipLevels = [2, 2, 4];

        public static int GetFaceSize() => FaceSizes[(int)_reflectionQuality];
        public static int GetTextureSize() => TextureSizes[(int)_reflectionQuality];
        public static int GetLambertianSampleCount() => LambertianSampleCounts[(int)_reflectionQuality];
        public static int GetGgxSampleCount() => GgxSampleCounts[(int)_reflectionQuality];
        public static int GetSheenSampleCount() => SheenSampleCounts[(int)_reflectionQuality];
        public static int GetLowestMipLevel() => LowestMipLevels[(int)_reflectionQuality];
        public static bool IncludeSheen() => (int)_reflectionQuality >= 1;

        // 捕获频率预设
        static readonly float[] MoveDistanceThresholds = [3.0f, 1.5f, 0.5f];
        static readonly float[] TimeThresholdsNear = [3.0f, 1.0f, 0.5f];
        static readonly float[] TimeThresholdsFar = [8.0f, 3.0f, 1.0f];

        public static float GetMoveDistanceThreshold() => MoveDistanceThresholds[(int)_captureFrequency];
        public static float GetTimeThresholdNear() => TimeThresholdsNear[(int)_captureFrequency];
        public static float GetTimeThresholdFar() => TimeThresholdsFar[(int)_captureFrequency];
    }
}
