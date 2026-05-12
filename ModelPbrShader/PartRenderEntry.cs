using Engine.Graphics;
using Engine.Media;

namespace Game {
    /// <summary>
    /// Mesh part 渲染队列类型
    /// </summary>
    public enum PartRenderQueue {
        Opaque,
        Transparent,
        Transmission,
        Scatter
    }

    /// <summary>
    /// 单个 mesh part 的渲染条目
    /// 在 PrepareCustomQueues 中创建，按队列分类存储
    /// </summary>
    public struct PartRenderEntry {
        public ModelMesh Mesh;
        public ModelMeshPart Part;
        public ModelMaterial Material;
        public SubsystemModelsRenderer.ModelData ModelData;
        public Texture2D TextureOverride;
        public PartRenderQueue QueueType;
        public float Depth;

        public static PartRenderQueue ComputeQueueType(ModelMaterial mat) {
            if (mat?.VolumeScatter?.IsEnabled == true
                && mat?.DiffuseTransmission?.IsEnabled == true) {
                return PartRenderQueue.Scatter;
            }
            if (mat?.Transmission?.IsEnabled == true) {
                return PartRenderQueue.Transmission;
            }
            if (mat?.AlphaMode == ModelAlphaMode.Blend) {
                return PartRenderQueue.Transparent;
            }
            return PartRenderQueue.Opaque;
        }
    }
}