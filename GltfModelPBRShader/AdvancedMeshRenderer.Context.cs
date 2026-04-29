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

        protected static int ComputeContextHash(RenderContext context) {
            unchecked {
                int hash = 17;
                if (context.UseIBL) {
                    hash = hash * 31 + "USE_IBL 1".GetHashCode();
                }
                if (context.HasPunctualLight) {
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

        protected void SetHasPunctualLight(bool value) {
            if (CurrentContext.HasPunctualLight == value) {
                return;
            }
            CurrentContext.HasPunctualLight = value;
            CachedContextHash = ComputeContextHash(CurrentContext);
        }

        protected static int AdjustContextHashForMaterial(int contextHash, ModelMaterial material, RenderContext context) {
            unchecked {
                if (material?.DiffuseTransmission?.IsEnabled == true
                    && !context.UseIBL) {
                    contextHash ^= "USE_IBL 1".GetHashCode();
                }
                if (material?.Unlit?.IsEnabled == true
                    && context.HasPunctualLight) {
                    contextHash ^= "USE_PUNCTUAL 1".GetHashCode();
                }
                return contextHash;
            }
        }
    }
}