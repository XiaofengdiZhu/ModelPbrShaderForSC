using System;
using System.IO;
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
        public SubsystemGameWidgets _subsystemGameWidgets;
        public static GltfPbrRenderer PbrRenderer { get; private set; }

        /// <summary>
        /// 是否启用动态 IBL（从配置读取，默认 false）
        /// </summary>
        public bool DynamicIblEnabled { get; private set; }

        public override void Load(ValuesDictionary valuesDictionary) {
            // 获取子系统引用
            _subsystemModelsRenderer = Project.FindSubsystem<SubsystemModelsRenderer>();
            _subsystemTerrain = Project.FindSubsystem<SubsystemTerrain>();
            _subsystemSky = Project.FindSubsystem<SubsystemSky>();
            _subsystemPlayers = Project.FindSubsystem<SubsystemPlayers>();
            _subsystemGameWidgets = Project.FindSubsystem<SubsystemGameWidgets>();

            // 读取配置
            DynamicIblEnabled = valuesDictionary.GetValue("DynamicIblEnabled", false);

            if (PbrRenderer == null) {
                Model.LoadTexturesInSrgb = true;
                try {
                    PbrRenderer = new GltfPbrRenderer();

                    if (DynamicIblEnabled) {
                        // 动态 IBL 模式：不加载静态环境贴图
                        // 由 GltfPbrRenderer.BeginFrame 按玩家动态捕获
                        Log.Information("[glTF PBR Shader] Dynamic IBL mode enabled.");
                    }
                    else {
                        // 静态 IBL 模式：加载默认环境贴图
                        string envPath = "Environments/Cannon_Exterior.hdr";
                        Stream envStream = ContentManager.GetStream(envPath);
                        if (envStream != null) {
                            PbrRenderer.LoadEnvironmentMap(envStream);
                            PbrRenderer.EnvironmentStrength = 1.0f;
                            PbrRenderer.MipCount = PbrRenderer.IblSampler.MipCount;
                            Log.Information($"[glTF PBR Shader] Loaded static environment map: {envPath}, MipCount={PbrRenderer.MipCount}");
                        }
                        else {
                            Log.Warning($"[glTF PBR Shader] Environment map not found: {envPath}");
                        }
                    }
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
                if (DynamicIblEnabled && _subsystemTerrain != null && _subsystemSky != null) {
                    // 使用第一个 GameWidget 初始化（后续会在 BeginFrame 中按玩家更新）
                    GameWidget gameWidget = _subsystemGameWidgets?.GameWidgets.Count > 0
                        ? _subsystemGameWidgets.GameWidgets[0]
                        : null;
                    if (gameWidget != null) {
                        PbrRenderer.InitializeDynamicIbl(_subsystemTerrain, _subsystemSky, gameWidget);
                    }
                }
            }

            // 订阅玩家断开事件，清理玩家环境数据
            if (DynamicIblEnabled && _subsystemPlayers != null) {
                _subsystemPlayers.PlayerRemoved += OnPlayerRemoved;
            }
        }

        public override void Dispose() {
            // 取消订阅事件
            if (DynamicIblEnabled && _subsystemPlayers != null) {
                _subsystemPlayers.PlayerRemoved -= OnPlayerRemoved;
            }

            PbrRenderer?.CelestialBodyCache.Clear();

            // 清理所有玩家环境数据
            if (DynamicIblEnabled) {
                foreach (int playerIndex in PbrRenderer.PlayerEnvironments.Keys) {
                    PbrRenderer.CleanupPlayerData(playerIndex);
                }
            }
        }

        /// <summary>
        /// 玩家断开时调用，清理对应的环境数据
        /// </summary>
        /// <param name="playerData">断开的玩家数据</param>
        void OnPlayerRemoved(PlayerData playerData) {
            if (DynamicIblEnabled && PbrRenderer != null && playerData != null) {
                int playerIndex = playerData.PlayerIndex;
                PbrRenderer.CleanupPlayerData(playerIndex);
                Log.Information($"[glTF PBR Shader] Cleaned up environment data for player {playerIndex}");
            }
        }
    }
}
