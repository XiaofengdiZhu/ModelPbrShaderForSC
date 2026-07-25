using Engine;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    internal struct PbrWidgetRenderEntry {
        public Model Model;
        public ModelMesh Mesh;
        public ModelMeshPart Part;
        public ModelMaterial Material;
        public Texture2D TextureOverride;
        public Matrix MeshTransform;
        public PartRenderQueue QueueType;
        public float Depth;
    }
}
