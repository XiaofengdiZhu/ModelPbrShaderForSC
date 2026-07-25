using System;
using Engine;
using Engine.Graphics;
using GameEntitySystem;
using TemplatesDatabase;

namespace Game {
    public class SubsystemModelPbrShader : Subsystem {
        public SubsystemModelsRenderer _subsystemModelsRenderer;
        public SubsystemTerrain _subsystemTerrain;
        public SubsystemSky _subsystemSky;
        public SubsystemPlayers _subsystemPlayers;
        public static PbrRenderer Renderer { get; private set; }

        public override void Load(ValuesDictionary valuesDictionary) {
            // 获取子系统引用
            _subsystemModelsRenderer = Project.FindSubsystem<SubsystemModelsRenderer>();
            _subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>();
            _subsystemSky = Project.FindSubsystem<SubsystemSky>();
            _subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>();
            if (Renderer == null) {
                try {
                    Renderer = new PbrRenderer();
                    Renderer.EnvironmentStrength = 1.0f;
                    Log.Information("[PBR Shader] Renderer initialized successfully.");
                }
                catch (Exception ex) {
                    Log.Error("[PBR Shader] Failed to initialize: " + ex.Message + "\n" + ex.StackTrace);
                }
            }
            if (Renderer != null) {
                // 装载模组设置到缓存（设置值在进存档前已由 ModSettingsManager 注册就绪）
                ModelPbrShaderSettings.ApplyPlatformDefaultOnFirstRun();
                ModelPbrShaderSettings.LoadFromModSettings();

                _subsystemModelsRenderer.CustomRenderer = Renderer;
                _subsystemModelsRenderer.UseCustomRendering = true;
                Renderer.Initialize(_subsystemModelsRenderer);

                // 初始化动态 IBL
                if (_subsystemTerrain != null
                    && _subsystemSky != null) {
                    Renderer.InitializeDynamicIbl(_subsystemTerrain, _subsystemSky);
                    ModelPbrShaderSettings.Dirty = true;
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
            Renderer?.CelestialBodyCache.Clear();

            // 清理所有玩家环境数据
            int[] playerKeys = new int[Renderer.PlayerEnvironments.Count];
            Renderer.PlayerEnvironments.Keys.CopyTo(playerKeys, 0);
            foreach (int playerIndex in playerKeys) {
                Renderer.CleanupPlayerData(playerIndex);
            }

        }

        void OnPlayerRemoved(PlayerData playerData) {
            if (Renderer != null
                && playerData != null) {
                int playerIndex = playerData.PlayerIndex;
                Renderer.CleanupPlayerData(playerIndex);
                Log.Information("[PBR Shader] Cleaned up environment data for player " + playerIndex);
            }
        }

        public override void OnEntityRemoved(Entity entity) {
            if (Renderer == null) { return; }
            foreach (ComponentModel cm in entity.FindComponents<ComponentModel>()) {
                SubsystemModelsRenderer.ModelData toRemove = null;
                foreach (var kvp in Renderer.CelestialBodyCache) {
                    if (kvp.Key.ComponentModel == cm) {
                        toRemove = kvp.Key;
                        break;
                    }
                }
                if (toRemove != null) {
                    Renderer.CelestialBodyCache.Remove(toRemove);
                }
            }
        }
    }
}
