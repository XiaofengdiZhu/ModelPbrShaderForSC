using System;
using System.IO;
using Engine;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemGltfModelPBRShader : Subsystem {
        public SubsystemModelsRenderer _subsystemModelsRenderer;
        public static GltfPbrRenderer PbrRenderer { get; private set; }

        public override void Load(ValuesDictionary valuesDictionary) {
            if (PbrRenderer == null) {
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
            if (PbrRenderer != null) {
                _subsystemModelsRenderer = Project.FindSubsystem<SubsystemModelsRenderer>();
                _subsystemModelsRenderer.CustomRenderer = PbrRenderer;
                _subsystemModelsRenderer.UseCustomRendering = true;
                PbrRenderer.Initialize(_subsystemModelsRenderer);
            }
        }

        public override void Dispose() {
            PbrRenderer.CelestialBodyCache.Clear();
        }
    }
}