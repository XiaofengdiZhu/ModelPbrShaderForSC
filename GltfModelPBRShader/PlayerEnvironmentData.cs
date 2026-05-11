using System;
using Engine;
using Engine.Graphics;

namespace Game {
    /// <summary>
    /// 每玩家的环境捕获数据
    /// 存储玩家的 IBL 采样器、全景图渲染目标和光照缓存
    /// </summary>
    public class PlayerEnvironmentData : IDisposable {
        /// <summary>
        /// 上次捕获位置
        /// </summary>
        public Vector3 LastCapturePosition;

        /// <summary>
        /// 上次捕获时间
        /// </summary>
        public double LastCaptureTime;

        /// <summary>
        /// IBL 采样器
        /// </summary>
        public IblSampler IblSampler;

        /// <summary>
        /// Mip 级别数
        /// </summary>
        public int MipCount;

        /// <summary>
        /// 全景图渲染目标
        /// </summary>
        public RenderTarget2D PanoramaRenderTarget;

        /// <summary>
        /// 玩家光照缓存
        /// </summary>
        public float CachedPlayerLight;

        /// <summary>
        /// 当前捕获阶段
        /// </summary>
        public CapturePhase Phase;

        /// <summary>
        /// 当前调度表帧索引
        /// </summary>
        public int ScheduleFrameIndex;

        /// <summary>
        /// 待处理的捕获位置
        /// </summary>
        public Vector3 PendingCapturePosition;

        /// <summary>
        /// 释放所有资源
        /// </summary>
        public void Dispose() {
            IblSampler?.Dispose();
            IblSampler = null;
            PanoramaRenderTarget?.Dispose();
            PanoramaRenderTarget = null;
        }
    }
}