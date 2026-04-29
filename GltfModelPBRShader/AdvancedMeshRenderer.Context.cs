using Engine.Media;

namespace Game {
    partial class AdvancedMeshRenderer {
        protected void UpdateContextHash(RenderContext context) {
            (bool UseIBL, bool UseLinearOutput, ToneMapMode ToneMapMode, bool HasPunctualLight, DebugChannel DebugChannel) contextParams = (
                context.UseIBL, context.UseLinearOutput, context.ToneMapMode, context.HasPunctualLight, context.DebugChannel);
            if (_lastContextParams == contextParams) {
                return;
            }
            _lastContextParams = contextParams;
            CachedContextHash = ComputeContextHash(context);
        }

        protected static int ComputeContextHash(RenderContext context) => ComputeContextHash(context.UseIBL, context.HasPunctualLight, context);

        protected void SetHasPunctualLight(bool value) {
            if (CurrentContext.HasPunctualLight == value) {
                return;
            }
            CurrentContext.HasPunctualLight = value;
            CachedContextHash = ComputeContextHash(CurrentContext);
        }

        protected static int AdjustContextHashForMaterial(int contextHash, ModelMaterial material, RenderContext context) {
            // 材质覆盖后的实际 shader defines：DiffuseTransmission 需要 IBL，Unlit 不需要 punctual
            bool useIBL = context.UseIBL || material?.DiffuseTransmission?.IsEnabled == true;
            bool usePunctual = context.HasPunctualLight && material?.Unlit?.IsEnabled != true;
            // 如果不需要调整，直接返回原 hash
            if (useIBL == context.UseIBL
                && usePunctual == context.HasPunctualLight) {
                return contextHash;
            }
            // 用调整后的参数重新计算 hash，确保语义相同的 defines 得到相同 hash
            return ComputeContextHash(useIBL, usePunctual, context);
        }

        protected static int ComputeContextHash(bool useIBL, bool usePunctual, RenderContext context) {
            unchecked {
                int hash = 17;
                if (useIBL) {
                    hash = hash * 31 + "USE_IBL 1".GetHashCode();
                }
                if (usePunctual) {
                    hash = hash * 31 + "USE_PUNCTUAL 1".GetHashCode();
                }
                if (context.UseLinearOutput) {
                    hash = hash * 31 + "LINEAR_OUTPUT 1".GetHashCode();
                }
                else {
                    string tonemapDefine = context.ToneMapMode switch {
                        ToneMapMode.KhrPbrNeutral => "TONEMAP_KHR_PBR_NEUTRAL 1",
                        ToneMapMode.AcesNarkowicz => "TONEMAP_ACES_NARKOWICZ 1",
                        ToneMapMode.AcesHill => "TONEMAP_ACES_HILL 1",
                        ToneMapMode.AcesHillExposureBoost => "TONEMAP_ACES_HILL_EXPOSURE_BOOST 1",
                        _ => "LINEAR_OUTPUT 1"
                    };
                    hash = hash * 31 + tonemapDefine.GetHashCode();
                }
                if (context.DebugChannel != DebugChannel.None) {
                    hash = hash * 31 + $"DEBUG {(int)context.DebugChannel}".GetHashCode();
                }
                return hash;
            }
        }
    }
}