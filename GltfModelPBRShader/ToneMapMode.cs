namespace Game {
    /// <summary>
    /// 色调映射模式
    /// </summary>
    public enum ToneMapMode {
        /// <summary>Khronos PBR Neutral - 最佳色彩还原（官方默认）</summary>
        KhrPbrNeutral,

        /// <summary>ACES Narkowicz - 快速 ACES 近似</summary>
        AcesNarkowicz,

        /// <summary>ACES Hill - 基于 Stephen Hill 的 ACES 实现</summary>
        AcesHill,

        /// <summary>ACES Hill (Exposure Boost) - 带曝光增强的 ACES Hill</summary>
        AcesHillExposureBoost,

        /// <summary>无色调映射，仅 gamma 矫正</summary>
        None
    }
}
