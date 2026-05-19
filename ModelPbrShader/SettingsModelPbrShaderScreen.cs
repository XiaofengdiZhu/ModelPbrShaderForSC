using System.Xml.Linq;

namespace Game {
    public class SettingsModelPbrShaderScreen: Screen {
        public const string fName = "SettingsModelPbrShaderScreen";
        public readonly SliderWidget m_iblQualitySlider;
        public SettingsModelPbrShaderScreen() {
            XElement node = ContentManager.Get<XElement>("Screens/SettingsModelPbrShaderScreen");
            LoadContents(this, node);
            m_iblQualitySlider = Children.Find<SliderWidget>("IblQuality");
            m_iblQualitySlider.Value = ModelPbrShaderSettings.IblQuality;
            m_iblQualitySlider.Text = LanguageControl.Get(fName, "IblQuality", ModelPbrShaderSettings.IblQuality.ToString());
        }

        public override void Update() {
            if (m_iblQualitySlider.IsSliding) {
                ModelPbrShaderSettings.IblQuality = (int)m_iblQualitySlider.Value;
                m_iblQualitySlider.Value = ModelPbrShaderSettings.IblQuality;
                m_iblQualitySlider.Text = LanguageControl.Get(fName, "IblQuality", ModelPbrShaderSettings.IblQuality.ToString());
            }
            if (Input.Back
                || Input.Cancel
                || Children.Find<ButtonWidget>("TopBar.Back").IsClicked) {
                ScreensManager.GoBack();
            }
        }
    }
}