using Engine;
using Engine.Graphics;
using Game;

namespace Game
{
    public class CaptureCamera : Camera
    {
        private Vector3 m_position;

        public override Vector3 ViewPosition => m_position;
        public override Vector3 ViewDirection => Vector3.UnitZ;
        public override Vector3 ViewUp => Vector3.UnitY;
        public override Vector3 ViewRight => Vector3.UnitX;

        public override Matrix ViewMatrix => Matrix.Identity;
        public override Matrix InvertedViewMatrix => Matrix.Identity;
        public override Matrix ProjectionMatrix => Matrix.Identity;
        public override Matrix ScreenProjectionMatrix => Matrix.Identity;
        public override Matrix InvertedProjectionMatrix => Matrix.Identity;
        public override Matrix ViewProjectionMatrix => Matrix.Identity;

        public override Vector2 ViewportSize => Vector2.One;
        public override Matrix ViewportMatrix => Matrix.Identity;

        public override BoundingFrustum ViewFrustum => null;

        public override bool UsesMovementControls => false;
        public override bool IsEntityControlEnabled => false;

        public CaptureCamera(GameWidget gameWidget, Vector3 position) : base(gameWidget)
        {
            m_position = position;
        }

        public override void Update(float dt)
        {
        }
    }
}
