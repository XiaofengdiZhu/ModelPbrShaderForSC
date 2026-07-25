using System;

namespace Game {
    public sealed class PbrModelWidgetRenderer : ICustomModelWidgetRenderer {
        readonly PbrRenderer _renderer = new();
        bool _disposed;

        public void Initialize() {
            _renderer.DynamicIblEnabled = false;
            _renderer.IblSampler = null;
            _renderer.EnvironmentStrength = 0f;
        }

        public void Render(ModelWidgetRenderContext context) {
            _renderer.RenderWidget(context);
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _renderer.Dispose();
        }
    }
}
