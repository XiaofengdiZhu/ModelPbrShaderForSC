using System;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemGltfModelPBRShader : Subsystem {
        public SubsystemModelsRenderer _subsystemModelsRenderer;
        public SubsystemTerrain _subsystemTerrain;
        public SubsystemSky _subsystemSky;
        public SubsystemPlayers _subsystemPlayers;
        public static GltfPbrRenderer PbrRenderer { get; private set; }

        public override void Load(ValuesDictionary valuesDictionary) {
            // 获取子系统引用
            _subsystemModelsRenderer = Project.FindSubsystem<SubsystemModelsRenderer>();
            _subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>();
            _subsystemSky = Project.FindSubsystem<SubsystemSky>();
            _subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>();
            if (PbrRenderer == null) {
                Model.LoadTexturesInSrgb = true;
                try {
                    PbrRenderer = new GltfPbrRenderer();
                    PbrRenderer.EnvironmentStrength = 1.0f;
                    Log.Information("[glTF PBR Shader] PBR renderer initialized successfully.");
                }
                catch (Exception ex) {
                    Log.Error($"[glTF PBR Shader] Failed to initialize: {ex.Message}\n{ex.StackTrace}");
                }
            }
            if (PbrRenderer != null) {
                _subsystemModelsRenderer.CustomRenderer = PbrRenderer;
                _subsystemModelsRenderer.UseCustomRendering = true;
                PbrRenderer.Initialize(_subsystemModelsRenderer);

                // 初始化动态 IBL
                if (_subsystemTerrain != null
                    && _subsystemSky != null) {
                    PbrRenderer.InitializeDynamicIbl(_subsystemTerrain, _subsystemSky);
                }
            }

            // 订阅玩家断开事件，清理玩家环境数据
            if (_subsystemPlayers != null) {
                _subsystemPlayers.PlayerRemoved += OnPlayerRemoved;
            }
        }

        public override void Dispose() {
            if (_subsystemPlayers != null) {
                _subsystemPlayers.PlayerRemoved -= OnPlayerRemoved;
            }
            PbrRenderer?.CelestialBodyCache.Clear();

            // 清理所有玩家环境数据
            foreach (int playerIndex in PbrRenderer.PlayerEnvironments.Keys) {
                PbrRenderer.CleanupPlayerData(playerIndex);
            }
        }

        void OnPlayerRemoved(PlayerData playerData) {
            if (PbrRenderer != null
                && playerData != null) {
                int playerIndex = playerData.PlayerIndex;
                PbrRenderer.CleanupPlayerData(playerIndex);
                Log.Information($"[glTF PBR Shader] Cleaned up environment data for player {playerIndex}");
            }
        }
    }
}