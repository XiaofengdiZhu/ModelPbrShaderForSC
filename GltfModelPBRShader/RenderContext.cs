using System.Numerics;

namespace Game {
    /// <summary>
    /// 渲染上下文参数
    /// 封装单次渲染调用所需的所有上下文信息
    /// </summary>
    public readonly struct RenderContext {
        /// <summary>
        /// 视图矩阵
        /// </summary>
        public Matrix4x4 View { get; init; }

        /// <summary>
        /// 投影矩阵
        /// </summary>
        public Matrix4x4 Projection { get; init; }

        /// <summary>
        /// 是否启用 IBL
        /// </summary>
        public bool UseIBL { get; init; }

        /// <summary>
        /// 是否使用线性输出（用于离屏渲染）
        /// </summary>
        public bool UseLinearOutput { get; init; }

        /// <summary>
        /// 是否为 Scatter Pass
        /// </summary>
        public bool IsScatterPass { get; init; }

        /// <summary>
        /// 色调映射模式
        /// </summary>
        public ToneMapMode ToneMapMode { get; init; }

        /// <summary>
        /// 光源数量
        /// </summary>
        public int LightCount { get; init; }

        /// <summary>
        /// Debug 渲染通道
        /// </summary>
        public DebugChannel DebugChannel { get; init; }

        /// <summary>
        /// 实际的相机视图矩阵（用于光照空间变换）
        /// 与 View 不同：View 是 Identity（游戏引擎约定），CameraView 是真实相机矩阵
        /// </summary>
        public Matrix4x4 CameraView { get; init; }

        /// <summary>
        /// 是否启用蒙皮动画
        /// </summary>
        public bool EnableSkinning { get; init; }

        /// <summary>
        /// 是否启用 Morph Target 动画
        /// </summary>
        public bool EnableMorphing { get; init; }

        /// <summary>
        /// 主光源方向（世界空间，指向光源）
        /// </summary>
        public Vector3 LightDirection { get; init; }

        /// <summary>
        /// 主光源颜色
        /// </summary>
        public Vector3 LightColor { get; init; }

        /// <summary>
        /// 创建默认渲染上下文
        /// </summary>
        public static RenderContext Default => new() {
            UseIBL = true,
            ToneMapMode = ToneMapMode.KhrPbrNeutral,
            EnableSkinning = true,
            EnableMorphing = true
        };

        /// <summary>
        /// 创建用于 Scatter Pass 的上下文
        /// </summary>
        public RenderContext ForScatterPass() => this with { UseLinearOutput = true, IsScatterPass = true };

        /// <summary>
        /// 创建用于 Transmission Pass 的上下文
        /// </summary>
        public RenderContext ForTransmissionPass() => this with { UseLinearOutput = true, IsScatterPass = false };

        /// <summary>
        /// 创建用于 Main Pass 的上下文
        /// </summary>
        public RenderContext ForMainPass() => this with { UseLinearOutput = false, IsScatterPass = false };
    }
}
