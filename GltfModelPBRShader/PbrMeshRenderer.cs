using System;
using System.Numerics;
using Engine.Graphics;
using Engine.Media;

namespace Game {
    /// <summary>
    /// PBR 渲染器基类
    /// 管理 PBR 专用材质 UBO（MaterialCore、MaterialExtension）
    /// </summary>
    public abstract class PbrMeshRenderer : AdvancedMeshRenderer {
        protected UniformBuffer<MaterialCoreData> MaterialCoreUBO;
        protected UniformBuffer<MaterialExtensionData> MaterialExtUBO;

        public PbrMeshRenderer() {
            MaterialCoreUBO = new(1);
            MaterialExtUBO = new(6);
        }

        public override bool HasIBL => false;

        protected void UpdateMaterialUBOs(ModelMaterial material, bool useGeneratedTangents) {
            int extensionFlags = (int)MaterialUboBuilder.BuildExtensionFlags(material);

            if (LastMaterial != material) {
                MaterialCoreData coreData = MaterialUboBuilder.BuildMaterialCoreData(material, useGeneratedTangents);
                MaterialCoreUBO.Update(ref coreData);

                MaterialExtensionData extData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                MaterialExtUBO.Update(ref extData);

                LastMaterial = material;
                LastExtensionFlags = extensionFlags;
                UvTransformDirty = true;
            }
            else if (LastExtensionFlags != extensionFlags) {
                MaterialExtensionData extData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                MaterialExtUBO.Update(ref extData);
                LastExtensionFlags = extensionFlags;
            }
        }

        public override void Dispose() {
            MaterialCoreUBO?.Dispose();
            MaterialExtUBO?.Dispose();
            base.Dispose();
        }
    }
}
