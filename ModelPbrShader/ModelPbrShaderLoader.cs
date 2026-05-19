using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Engine;
using XmlUtilities;

namespace Game {
    public class ModelPbrShaderLoader : ModLoader {
        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);
            ModsManager.RegisterHook("OnSettingsScreenCreated", this);
        }

        public override void OnLoadingFinished(List<Action> actions) {
            ScreensManager.AddScreen("SettingsModelPbrShader", new SettingsModelPbrShaderScreen());
        }

        public override void OnSettingsScreenCreated(SettingsScreen settingsScreen, out Dictionary<ButtonWidget, Action> buttonsToAdd) {
            buttonsToAdd = [];
            buttonsToAdd.Add(
                new BevelledButtonWidget() {
                    Text = LanguageControl.Get("ModelPbrShaderLoader", "1"),
                    Size = new Vector2(310, 60),
                    FontScale = 1.25f,
                    HorizontalAlignment = WidgetAlignment.Center,
                    VerticalAlignment = WidgetAlignment.Center,
                    Margin = new Vector2(0f, 5f)
                },
                () => ScreensManager.SwitchScreen("SettingsModelPbrShader")
            );
        }

        public override void SaveSettings(XElement xElement) {
            XElement container = new XElement("ModelPbrShader");
            container.SetAttributeValue("IblQuality", ModelPbrShaderSettings.IblQuality);
            xElement.Add(container);
        }

        public override void LoadSettings(XElement xElement) {
            XElement container = xElement.Element("ModelPbrShader");
            if (container != null) {
                string iblQualityString = container.Attribute("IblQuality")?.Value;
                if (int.TryParse(iblQualityString, out int iblQuality)) {
                    ModelPbrShaderSettings.IblQuality = iblQuality;
                }
            }
        }
    }
}