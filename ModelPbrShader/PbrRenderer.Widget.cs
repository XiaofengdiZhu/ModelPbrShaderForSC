using System;
using System.Numerics;
using Engine;
using Engine.Graphics;
using Engine.Media;
using Silk.NET.OpenGLES;
using Shader = Engine.Graphics.Shader;
using Vector3 = Engine.Vector3;
using Vector4 = System.Numerics.Vector4;
using Matrix = Engine.Matrix;

namespace Game {
    public partial class PbrRenderer {
        static readonly Comparison<PbrWidgetRenderEntry> WidgetBackToFrontComparison = (a, b) => b.Depth.CompareTo(a.Depth);

        internal void RenderWidget(ModelWidgetRenderContext context) {
            RenderTarget2D outputTarget = Display.RenderTarget;
            Viewport outputViewport = Display.Viewport;
            Rectangle outputScissor = Display.ScissorRectangle;
            try {
                PrepareWidgetQueues(context);
                ConfigureWidgetContext(context);

                if (_widgetScatterEntries.Count > 0) {
                    _framebufferManager.EnsureScatterFramebuffer();
                    _framebufferManager.BindScatter();
                    _framebufferManager.ClearScatter();
                    CurrentContext.UseLinearOutput = true;
                    CurrentContext.IsScatterPass = true;
                    UpdateContextHash(CurrentContext);
                    foreach (PbrWidgetRenderEntry entry in _widgetScatterEntries) {
                        RenderWidgetEntry(entry, context);
                    }
                    CurrentContext.UseLinearOutput = false;
                    CurrentContext.IsScatterPass = false;
                    UpdateContextHash(CurrentContext);
                    RestoreWidgetOutput(outputTarget, outputViewport, outputScissor);
                }

                foreach (PbrWidgetRenderEntry entry in _widgetOpaqueEntries) {
                    RenderWidgetEntry(entry, context);
                }

                SortWidgetTransparentEntries(context.ViewMatrix);
                if (_widgetHasTransmission) {
                    foreach (PbrWidgetRenderEntry entry in _widgetAllTransparentEntries) {
                        if (entry.QueueType != PartRenderQueue.Transmission) {
                            RenderWidgetEntry(entry, context);
                        }
                    }
                    _framebufferManager.EnsureTransmissionFramebuffer();
                    _framebufferManager.BlitSourceToTransmission(outputTarget, outputViewport);
                    _framebufferManager.GenerateTransmissionMipmap();
                    RestoreWidgetOutput(outputTarget, outputViewport, outputScissor);
                    foreach (PbrWidgetRenderEntry entry in _widgetAllTransparentEntries) {
                        if (entry.QueueType == PartRenderQueue.Transmission) {
                            RenderWidgetEntry(entry, context);
                        }
                    }
                }
                else {
                    foreach (PbrWidgetRenderEntry entry in _widgetAllTransparentEntries) {
                        RenderWidgetEntry(entry, context);
                    }
                }
            }
            finally {
                RestoreWidgetOutput(outputTarget, outputViewport, outputScissor);
                DisableInstanceAttributes();
                GLWrapper.ApplyDepthStencilState(DepthStencilState.Default);
                GLWrapper.ApplyRasterizerState(RasterizerState.CullCounterClockwiseScissor);
                GLWrapper.ApplyBlendState(BlendState.Opaque);
            }
        }

        static void RestoreWidgetOutput(RenderTarget2D target, Viewport viewport, Rectangle scissor) {
            GLWrapper.ApplyRenderTarget(target);
            GLWrapper.m_viewport = null;
            GLWrapper.m_scissorRectangle = null;
            GLWrapper.ApplyViewportScissor(viewport, scissor, Display.RasterizerState.ScissorTestEnable);
        }

        void SetupWidgetDepthState(ModelMaterial material, bool forceAlphaMask) {
            if (forceAlphaMask) {
                GLWrapper.ApplyDepthStencilState(DepthStencilState.Default);
                return;
            }
            SetupDepthState(material);
        }

        void SetupWidgetBlendMode(ModelMaterial material, bool forceAlphaMask) {
            if (forceAlphaMask) {
                GLWrapper.ApplyBlendState(BlendState.Opaque);
                return;
            }
            SetupBlendMode(material, CurrentContext);
        }

        bool _widgetHasTransmission;

        void PrepareWidgetQueues(ModelWidgetRenderContext context) {
            _widgetOpaqueEntries.Clear();
            _widgetScatterEntries.Clear();
            _widgetTransparentEntries.Clear();
            _widgetAllTransparentEntries.Clear();
            _widgetHasTransmission = false;
            Viewport viewport = Display.Viewport;
            _framebufferManager.SetSize(viewport.Width, viewport.Height);

            foreach (Model model in context.Models) {
                if (model == null) {
                    continue;
                }
                Texture2D textureOverride = context.GetTextureOverride(model);
                foreach (ModelMesh mesh in model.Meshes) {
                    if (!mesh.IsVisible) {
                        continue;
                    }
                    Matrix meshTransform = context.GetMeshTransform(model, mesh) * context.ModelTransform;
                    foreach (ModelMeshPart part in mesh.MeshParts) {
                        ModelMaterial material = model.GetMaterial(part.MaterialIndex);
                        PartRenderQueue queueType = PartRenderEntry.ComputeQueueType(material);
                        PbrWidgetRenderEntry entry = new() {
                            Model = model,
                            Mesh = mesh,
                            Part = part,
                            Material = material,
                            TextureOverride = textureOverride,
                            MeshTransform = meshTransform,
                            QueueType = queueType
                        };
                        switch (queueType) {
                            case PartRenderQueue.Opaque:
                                _widgetOpaqueEntries.Add(entry);
                                break;
                            case PartRenderQueue.Scatter:
                                _widgetScatterEntries.Add(entry);
                                _widgetTransparentEntries.Add(entry);
                                break;
                            case PartRenderQueue.Transmission:
                                _widgetHasTransmission = true;
                                _widgetTransparentEntries.Add(entry);
                                break;
                            default:
                                _widgetTransparentEntries.Add(entry);
                                break;
                        }
                    }
                }
            }
            _widgetAllTransparentEntries.AddRange(_widgetTransparentEntries);
        }

        void ConfigureWidgetContext(ModelWidgetRenderContext context) {
            CurrentContext.View = context.ViewMatrix;
            CurrentContext.Projection = context.ProjectionMatrix;
            CurrentContext.CameraView = context.ViewMatrix;
            CurrentContext.Wvp = context.ViewMatrix * context.ProjectionMatrix;
            CurrentContext.UseIBL = false;
            CurrentContext.UseLinearOutput = false;
            CurrentContext.IsScatterPass = false;
            CurrentContext.ToneMapMode = ToneMapMode.KhrPbrNeutral;
            CurrentContext.HasPunctualLight = true;
            CurrentContext.DebugChannel = DebugChannel.None;
            CurrentContext.EnableSkinning = false;
            CurrentContext.EnableMorphing = true;
            CurrentContext.LightDirection = Vector3.Normalize(new Vector3(1f, 1f, -1f));
            CurrentContext.LightColor = Vector3.One;
            UpdateContextHash(CurrentContext);
            Matrix4x4 cameraView = CurrentContext.CameraView;
            SceneData sceneData = new() {
                CameraPos = new Vector4(0f, 0f, 0f, 1f),
                Exposure = 1f,
                EnvironmentStrength = 0f,
                MipCount = 0,
                EnvRotationCol0 = new Vector4(cameraView.M11, cameraView.M21, cameraView.M31, 0f),
                EnvRotationCol1 = new Vector4(cameraView.M12, cameraView.M22, cameraView.M32, 0f),
                EnvRotationCol2 = new Vector4(cameraView.M13, cameraView.M23, cameraView.M33, 0f)
            };
            SceneUBO.Update(ref sceneData);

            Vector3 viewLightDirection = Vector3.Normalize(Vector3.TransformNormal(CurrentContext.LightDirection, cameraView));
            LightsData lightsData = new() {
                LightCount = 1,
                Light0 = new LightData {
                    Direction = viewLightDirection,
                    Color = CurrentContext.LightColor,
                    Intensity = 1f,
                    Type = 0
                }
            };
            LightsUBO.Update(ref lightsData);
            LastMaterial = null;
            LastMaterialVersion = -1;
            UvTransformDirty = true;
            MaterialTextureBinder.ResetFrameState();
        }

        void SortWidgetTransparentEntries(Matrix viewMatrix) {
            for (int i = 0; i < _widgetAllTransparentEntries.Count; i++) {
                PbrWidgetRenderEntry entry = _widgetAllTransparentEntries[i];
                Vector3 center = entry.Mesh.BoundingBox.Center();
                Vector3 viewCenter = Vector3.Transform(Vector3.Transform(center, entry.MeshTransform), viewMatrix);
                entry.Depth = viewCenter.Z;
                _widgetAllTransparentEntries[i] = entry;
            }
            _widgetAllTransparentEntries.Sort(WidgetBackToFrontComparison);
        }

        void RenderWidgetEntry(PbrWidgetRenderEntry entry, ModelWidgetRenderContext context) {
            if (entry.Part == null) {
                return;
            }
            ModelMaterial material = entry.Material;
            if (material == null) {
                material = entry.TextureOverride is RenderTarget2D ? DefaultDielectricMaskMaterial : DefaultDielectricMaterial;
            }
            bool hasSkin = entry.Model?.HasSkin == true;
            bool forceAlphaMask = context.UseAlphaThreshold;
            Shader shader = GetOrCreateWidgetShader(entry.Mesh, material, CurrentContext, forceAlphaMask, entry.TextureOverride != null);
            if (shader == null) {
                return;
            }
            shader.PrepareForDrawing();
            context.SetupShaderParameters(shader, entry.Model, entry.Mesh);
            GLWrapper.UseProgram(shader.m_program);
            Matrix renderStateModelMatrix = hasSkin ? Matrix.Identity : entry.MeshTransform;
            UpdateRenderStateUBO(CurrentContext.Wvp, renderStateModelMatrix);
            SetupMorphTargets(entry.Part, shader);
            UpdateWidgetMaterialUBOs(material, context.Color, forceAlphaMask);
            UpdateUVTransformUBO(material);
            BindWidgetTextures(entry, material, shader);
            SetupWidgetDepthState(material, forceAlphaMask);
            SetupCullMode(material, entry.MeshTransform.Determinant() < 0f);
            SetupWidgetBlendMode(material, forceAlphaMask);
            SetupTransmissionUniforms(material, shader);
            SetupVolumeScatterUniforms(material, shader);

            if (hasSkin) {
                JointTexture jointTexture = GetOrCreateJointTexture(entry.Model);
                int jointCount = context.CalculateJointMatrices(entry.Model, _widgetJointMatricesBuffer);
                jointTexture.Update(_widgetJointMatricesBuffer.AsSpan(0, jointCount));
                BindJointTexture(jointTexture, shader);
            }
            DrawMeshPart(entry.Part);
        }

        void UpdateWidgetMaterialUBOs(ModelMaterial material, Color color, bool forceAlphaMask) {
            Vector4 colorFactor = new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
            MaterialCoreData coreData = MaterialUboBuilder.BuildMaterialCoreData(material, false);
            coreData.BaseColorFactor *= colorFactor;
            if (forceAlphaMask) {
                coreData.AlphaMode = (int)ModelAlphaMode.Mask;
                // PBR's mask test is strict (<), so this preserves the stock zero-threshold discard of alpha 0.
                coreData.AlphaCutoff = 0.00001f;
            }
            _materialCoreUBO.Update(ref coreData);

            int extensionFlags = (int)MaterialUboBuilder.BuildExtensionFlags(material);
            if (LastMaterial != material || LastMaterialVersion != material.Version) {
                MaterialExtensionData extensionData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                _materialExtUBO.Update(ref extensionData);
                LastMaterial = material;
                LastMaterialVersion = material.Version;
                LastExtensionFlags = extensionFlags;
                UvTransformDirty = true;
            }
            else if (LastExtensionFlags != extensionFlags) {
                MaterialExtensionData extensionData = MaterialUboBuilder.BuildMaterialExtensionData(material);
                _materialExtUBO.Update(ref extensionData);
                LastExtensionFlags = extensionFlags;
            }
        }

        void BindWidgetTextures(PbrWidgetRenderEntry entry, ModelMaterial material, Shader shader) {
            if (entry.TextureOverride != null) {
                MaterialTextureBinder.BindTexture2D(entry.TextureOverride, MaterialTextureSlot.BaseColor);
            }
            else if (entry.Model != null) {
                BindMaterialTextures(entry.Model, material, shader, null);
            }
            MaterialTextureBinder.SetTextureSlotUniforms(shader);
        }
    }
}
