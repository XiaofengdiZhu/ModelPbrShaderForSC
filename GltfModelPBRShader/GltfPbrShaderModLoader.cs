using System;
using System.Collections.Generic;
using System.IO;
using Engine;
using Engine.Graphics;
using HarmonyLib;

namespace Game {
    public class GltfPbrShaderModLoader : ModLoader {
        public static GltfPbrRenderer PbrRenderer { get; private set; }

        public override void __ModInitialize() {
            ModsManager.RegisterHook("OnLoadingFinished", this);

            Harmony harmony = new Harmony("gltf.model.pbr.shader");
            harmony.PatchAll();
        }

        public override void OnLoadingFinished(List<Action> actions) {

            try {
                PbrRenderer = new GltfPbrRenderer();

                string envPath = "Environments/Cannon_Exterior.hdr";
                Stream envStream = ContentManager.GetStream(envPath);
                if (envStream != null) {
                    PbrRenderer.LoadEnvironmentMap(envStream);
                    PbrRenderer.EnvironmentStrength = 1.0f;
                    PbrRenderer.MipCount = PbrRenderer.IblSampler.MipCount;
                    Log.Information($"[glTF PBR Shader] Loaded environment map: {envPath}, MipCount={PbrRenderer.MipCount}");
                }
                else {
                    Log.Warning($"[glTF PBR Shader] Environment map not found: {envPath}");
                }

                Log.Information("[glTF PBR Shader] PBR renderer initialized successfully.");
            }
            catch (Exception ex) {
                Log.Error($"[glTF PBR Shader] Failed to initialize: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    /// <summary>
    /// Harmony patch：将 PBR 渲染器注入到 SubsystemModelsRenderer
    /// </summary>
    [HarmonyPatch(typeof(SubsystemModelsRenderer))]
    public static class SubsystemModelsRendererPatch {
        [HarmonyPrefix]
        [HarmonyPatch("Load")]
        public static void LoadPrefix(SubsystemModelsRenderer __instance) {
            if (GltfPbrShaderModLoader.PbrRenderer != null) {
                __instance.AdvancedRenderer = GltfPbrShaderModLoader.PbrRenderer;
                __instance.UseCustomRendering = true;
                Log.Information("[glTF PBR Shader] PBR renderer attached to SubsystemModelsRenderer");
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch("Dispose")]
        public static void DisposePrefix(SubsystemModelsRenderer __instance) {
            __instance.AdvancedRenderer = null;
            __instance.UseCustomRendering = false;
        }
    }
}
