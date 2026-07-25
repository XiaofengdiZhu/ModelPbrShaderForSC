using System;
using System.Collections.Generic;
using Engine;
using Engine.Graphics;

namespace Game {
    public class ModelPbrShaderLoader : ModLoader {
        private PbrModelWidgetRenderer _widgetRenderer;

        public override void __ModInitialize() {
            Model.LoadTexturesInSrgb = true;
        }

        public override void OnLoadingFinished(List<Action> actions) {
            if (_widgetRenderer == null) {
                PbrModelWidgetRenderer renderer = null;
                try {
                    renderer = new PbrModelWidgetRenderer();
                    renderer.Initialize();
                    _widgetRenderer = renderer;
                }
                catch (Exception ex) {
                    Log.Error("[PBR Shader] Failed to initialize widget renderer: " + ex);
                    _widgetRenderer = null;
                    try {
                        renderer?.Dispose();
                    }
                    catch (Exception disposeException) {
                        Log.Error("[PBR Shader] Failed to dispose widget renderer after initialization failure: " + disposeException);
                    }
                }
            }
            if (_widgetRenderer != null) {
                ModelWidget.CustomRenderer = _widgetRenderer;
            }
        }

        public override void ModDispose() {
            PbrModelWidgetRenderer renderer = _widgetRenderer;
            _widgetRenderer = null;
            if (ReferenceEquals(ModelWidget.CustomRenderer, renderer)) {
                ModelWidget.CustomRenderer = null;
            }
            try {
                renderer?.Dispose();
            }
            catch (Exception ex) {
                Log.Error("[PBR Shader] Failed to dispose widget renderer: " + ex);
            }
        }

        // idPath = [pageId, itemId]（不含 packageName），取末段 itemId 分发到设置缓存
        public override void OnModSettingChanged(string[] idPath, object value) {
            if (idPath != null && idPath.Length > 0) {
                ModelPbrShaderSettings.ApplySettingChange(idPath[idPath.Length - 1], value);
            }
        }
    }
}
