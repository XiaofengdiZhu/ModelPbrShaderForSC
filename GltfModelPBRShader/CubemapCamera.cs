using Engine;

namespace Game {
    /// <summary>
    /// 用于 Cubemap 捕获的简化相机
    /// 使用传入的 GameWidget，直接设置视图和投影矩阵
    /// </summary>
    public class CubemapCamera : Camera {
        Matrix _viewMatrix;
        Matrix _projectionMatrix;
        Matrix _invertedViewMatrix;
        BoundingFrustum _viewFrustum;
        Vector3 _viewPosition;
        Vector3 _viewDirection;
        Vector3 _viewUp;
        Vector3 _viewRight;

        public override Vector3 ViewPosition => _viewPosition;
        public override Vector3 ViewDirection => _viewDirection;
        public override Vector3 ViewUp => _viewUp;
        public override Vector3 ViewRight => _viewRight;
        public override Matrix ViewMatrix => _viewMatrix;
        public override Matrix InvertedViewMatrix => _invertedViewMatrix;
        public override Matrix ProjectionMatrix => _projectionMatrix;
        public override Matrix ScreenProjectionMatrix => _projectionMatrix;
        public override Matrix InvertedProjectionMatrix => Matrix.Invert(_projectionMatrix);
        public override Matrix ViewProjectionMatrix => _viewMatrix * _projectionMatrix;
        Vector2 _viewportSize = new(256, 256);
        public override Vector2 ViewportSize => _viewportSize;
        public override Matrix ViewportMatrix => Matrix.Identity;
        public override BoundingFrustum ViewFrustum => _viewFrustum;
        public override bool UsesMovementControls => false;
        public override bool IsEntityControlEnabled => false;

        public CubemapCamera(GameWidget gameWidget) : base(gameWidget) { }

        public void SetFaceSize(int size) => _viewportSize = new Vector2(size, size);

        /// <summary>
        /// 设置相机朝向 Cubemap 的某个面
        /// </summary>
        public void SetupForCubemapFace(Vector3 position, Vector3 target, Vector3 up, float farPlane) {
            _viewPosition = position;
            _viewMatrix = Matrix.CreateLookAt(position, position + target, up);
            _invertedViewMatrix = Matrix.Invert(_viewMatrix);

            // 90° FOV, 1:1 aspect ratio
            _projectionMatrix = Matrix.CreatePerspectiveFieldOfView(MathUtils.DegToRad(90f), 1.0f, 0.1f, farPlane);
            _viewFrustum = new BoundingFrustum(ViewProjectionMatrix);
            _viewDirection = target;
            _viewUp = up;
            _viewRight = Vector3.Normalize(Vector3.Cross(target, up));
        }

        public override void Update(float dt) { }
    }
}