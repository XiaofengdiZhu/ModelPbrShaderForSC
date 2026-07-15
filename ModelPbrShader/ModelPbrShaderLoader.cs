namespace Game {
    public class ModelPbrShaderLoader : ModLoader {
        // idPath = [pageId, itemId]（不含 packageName），取末段 itemId 分发到设置缓存
        public override void OnModSettingChanged(string[] idPath, object value) {
            if (idPath != null && idPath.Length > 0) {
                ModelPbrShaderSettings.ApplySettingChange(idPath[idPath.Length - 1], value);
            }
        }
    }
}
