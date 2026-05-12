using System.Collections.Generic;
using System.Numerics;
using Engine.Graphics;
using Vector3 = Engine.Vector3;
using Matrix = Engine.Matrix;

namespace Game {
    partial class AdvancedMeshRenderer {
        /// <summary>
        /// 从所有可见模型收集 glTF 灯光到全局列表（view space，按距相机距离排序）
        /// 在 PrepareCustomQueues 末尾调用
        /// </summary>
        protected void CollectGlobalLights(List<SubsystemModelsRenderer.ModelData> allModels) {
            _collectedLights.Clear();
            _collectedLightModels.Clear();
            foreach (SubsystemModelsRenderer.ModelData md in allModels) {
                ComponentModel cm = md.ComponentModel;
                if (cm == null
                    || !_collectedLightModels.Add(cm)) {
                    continue;
                }
                Model model = cm.Model;
                if (model == null
                    || model.Lights.Count == 0) {
                    continue;
                }
                Matrix wm = md.ComponentModel.AbsoluteBoneTransformsForCamera.Length > 0
                    ? md.ComponentModel.AbsoluteBoneTransformsForCamera[0]
                    : Matrix.Identity;
                Matrix4x4 viewMatrix = wm;
                foreach (ModelLight ml in model.Lights) {
                    if (!ml.IsVisible) {
                        continue;
                    }
                    System.Numerics.Vector3 viewPos;
                    System.Numerics.Vector3 viewDir;
                    if (ml.BoneIndex >= 0
                        && ml.BoneIndex < cm.AbsoluteBoneTransformsForCamera.Length) {
                        Matrix4x4 boneMatrix = cm.AbsoluteBoneTransformsForCamera[ml.BoneIndex];
                        viewPos = new System.Numerics.Vector3(boneMatrix.M41, boneMatrix.M42, boneMatrix.M43);
                        viewDir = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(-boneMatrix.M31, -boneMatrix.M32, -boneMatrix.M33));
                    }
                    else {
                        System.Numerics.Vector3 localPos = new(ml.Position.X, ml.Position.Y, ml.Position.Z);
                        System.Numerics.Vector3 localDir = new(ml.Direction.X, ml.Direction.Y, ml.Direction.Z);
                        viewPos = System.Numerics.Vector3.Transform(localPos, viewMatrix);
                        viewDir = System.Numerics.Vector3.Normalize(System.Numerics.Vector3.TransformNormal(localDir, viewMatrix));
                    }
                    _collectedLights.Add(
                        new CollectedLight {
                            ViewPosition = new Vector3(viewPos.X, viewPos.Y, viewPos.Z),
                            ViewDirection = new Vector3(viewDir.X, viewDir.Y, viewDir.Z),
                            Color = ml.Color,
                            Intensity = ml.Intensity,
                            Range = ml.Range,
                            Type = (int)ml.Type,
                            InnerConeCos = ml.InnerConeCos,
                            OuterConeCos = ml.OuterConeCos,
                            DistanceSq = viewPos.LengthSquared()
                        }
                    );
                }
            }
            _collectedLights.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            if (_collectedLights.Count > ModelLight.MaxPunctualLights) {
                _collectedLights.RemoveRange(ModelLight.MaxPunctualLights, _collectedLights.Count - ModelLight.MaxPunctualLights);
            }
        }

        /// 更新光照 UBO：太阳/月亮 + 全局 glTF 灯光
        /// </summary>
        protected void UpdateLightsUBO(float intensity) {
            LightsData lightsData = new() {
                LightCount = 1, Light0 = new LightData { Direction = _viewLightDir, Color = _baseLightColor * intensity, Intensity = 1f, Type = 0 }
            };
            unsafe {
                LightData* basePtr = &lightsData.Light0;
                for (int i = 0; i < _collectedLights.Count && lightsData.LightCount < 8; i++) {
                    CollectedLight cl = _collectedLights[i];
                    basePtr[lightsData.LightCount] = new LightData {
                        Direction = cl.ViewDirection,
                        Color = cl.Color,
                        Intensity = cl.Intensity,
                        Position = cl.ViewPosition,
                        Type = cl.Type,
                        Range = cl.Range,
                        InnerConeCos = cl.InnerConeCos,
                        OuterConeCos = cl.OuterConeCos
                    };
                    lightsData.LightCount++;
                }
            }
            LightsUBO.Update(ref lightsData);
        }

        // 全局灯光（每帧收集，按距相机距离排序）
        protected struct CollectedLight {
            public Vector3 ViewPosition;
            public Vector3 ViewDirection;
            public Vector3 Color;
            public float Intensity;
            public float Range;
            public int Type;
            public float InnerConeCos;
            public float OuterConeCos;
            public float DistanceSq;
        }
    }
}