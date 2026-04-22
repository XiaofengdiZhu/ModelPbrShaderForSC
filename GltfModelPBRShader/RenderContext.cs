using System.Numerics;
using Engine;
using Vector3 = Engine.Vector3;

namespace Game {
    /// <summary>
    /// 渲染上下文参数
    /// 单实例，BeginFrame 更新字段，pre-pass 可直接修改
    /// </summary>
    public class RenderContext {
        /// <summary>
        /// 视图矩阵
        /// </summary>
        public Matrix4x4 View { get; set; }

        /// <summary>
        /// 投影矩阵
        /// </summary>
        public Matrix4x4 Projection { get; set; }

        /// <summary>
        /// 实际的相机视图矩阵（用于光照空间变换）
        /// 与 View 不同：View 是 Identity（游戏引擎约定），CameraView 是真实相机矩阵
        /// </summary>
        public Matrix4x4 CameraView { get; set; }

        /// <summary>
        /// 世界视图投影矩阵
        /// </summary>
        public Matrix4x4 Wvp { get; set; }

        /// <summary>
        /// 是否启用 IBL
        /// </summary>
        public bool UseIBL { get; set; }

        /// <summary>
        /// 是否使用线性输出（用于离屏渲染）
        /// </summary>
        public bool UseLinearOutput { get; set; }

        /// <summary>
        /// 是否为 Scatter Pass
        /// </summary>
        public bool IsScatterPass { get; set; }

        /// <summary>
        /// 色调映射模式
        /// </summary>
        public ToneMapMode ToneMapMode { get; set; }

        /// <summary>
        /// 光源数量
        /// </summary>
        public int LightCount { get; set; }

        /// <summary>
        /// Debug 渲染通道
        /// </summary>
        public DebugChannel DebugChannel { get; set; }

        /// <summary>
        /// 是否启用蒙皮动画
        /// </summary>
        public bool EnableSkinning { get; set; }

        /// <summary>
        /// 是否启用 Morph Target 动画
        /// </summary>
        public bool EnableMorphing { get; set; }

        /// <summary>
        /// 主光源方向（世界空间，指向光源）
        /// </summary>
        public Vector3 LightDirection { get; set; }

        /// <summary>
        /// 主光源颜色
        /// </summary>
        public Vector3 LightColor { get; set; }
    }
}
