using System.Xml.Linq;

namespace Game {
    public class SettingsModelPbrShaderScreen : Screen {
        public const string fName = "SettingsModelPbrShaderScreen";
        public readonly BevelledButtonWidget m_environmentReflectionButton;
        public readonly SliderWidget m_reflectionQualitySlider;
        public readonly SliderWidget m_captureFrequencySlider;
        public readonly BevelledButtonWidget m_debugChannelButton;

        public SettingsModelPbrShaderScreen() {
            XElement node = ContentManager.Get<XElement>("Screens/SettingsModelPbrShaderScreen");
            LoadContents(this, node);
            m_environmentReflectionButton = Children.Find<BevelledButtonWidget>("EnvironmentReflection");
            m_reflectionQualitySlider = Children.Find<SliderWidget>("ReflectionQuality");
            m_captureFrequencySlider = Children.Find<SliderWidget>("CaptureFrequency");
            m_debugChannelButton = Children.Find<BevelledButtonWidget>("DebugChannel");
            m_reflectionQualitySlider.Value = ModelPbrShaderSettings.ReflectionQuality;
            m_captureFrequencySlider.Value = ModelPbrShaderSettings.CaptureFrequency;
            UpdateText();
        }

        void UpdateText() {
            m_environmentReflectionButton.Text = LanguageControl.Get(fName, "EnvironmentReflection", ModelPbrShaderSettings.EnvironmentReflection.ToString());
            m_reflectionQualitySlider.Text = LanguageControl.Get(fName, "ReflectionQuality", ModelPbrShaderSettings.ReflectionQuality.ToString());
            m_captureFrequencySlider.Text = LanguageControl.Get(fName, "CaptureFrequency", ModelPbrShaderSettings.CaptureFrequency.ToString());
            m_debugChannelButton.Text = LanguageControl.Get(fName, "DebugChannel", ((int)ModelPbrShaderSettings.DebugChannel).ToString());
        }

        public override void Update() {
            if (m_environmentReflectionButton.IsClicked) {
                ModelPbrShaderSettings.EnvironmentReflection = 1 - ModelPbrShaderSettings.EnvironmentReflection;
                UpdateText();
            }
            if (m_reflectionQualitySlider.IsSliding) {
                ModelPbrShaderSettings.ReflectionQuality = (int)m_reflectionQualitySlider.Value;
                m_reflectionQualitySlider.Value = ModelPbrShaderSettings.ReflectionQuality;
                UpdateText();
            }
            if (m_captureFrequencySlider.IsSliding) {
                ModelPbrShaderSettings.CaptureFrequency = (int)m_captureFrequencySlider.Value;
                m_captureFrequencySlider.Value = ModelPbrShaderSettings.CaptureFrequency;
                UpdateText();
            }
            if (m_debugChannelButton.IsClicked) {
                DialogsManager.ShowDialog(
                    null,
                    new ListSelectionDialog(
                        LanguageControl.Get("ContentWidgets", fName, "5"),
                        EnumUtils.GetEnumValues<DebugChannel>(),
                        56f,
                        e => LanguageControl.Get(fName, "DebugChannel", ((int)(DebugChannel)e).ToString()),
                        e => {
                            ModelPbrShaderSettings.DebugChannel = (DebugChannel)e;
                            UpdateText();
                        }
                    )
                );
            }
            bool iblOn = ModelPbrShaderSettings.EnvironmentReflection > 0;
            m_reflectionQualitySlider.IsEnabled = iblOn;
            m_captureFrequencySlider.IsEnabled = iblOn;
            if (Input.Back
                || Input.Cancel
                || Children.Find<ButtonWidget>("TopBar.Back").IsClicked) {
                ScreensManager.GoBack();
            }
        }
    }
}
