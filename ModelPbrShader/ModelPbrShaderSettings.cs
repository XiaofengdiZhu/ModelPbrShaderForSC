using System;

namespace Game {
    public class ModelPbrShaderSettings {
        public static int IblQuality {
            get;
            set {
                field = Math.Clamp(value, 0, 3);
                // 更新 IblSampler、PbrRenderer
            }
        } = 2;
    }
}