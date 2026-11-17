using DuckAndCover.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckAndCover.Game
{
    /// <summary>
    /// First thing that runs in the Boot scene. For now it checks the project is wired up (render
    /// pipeline, network package), builds the greybox test arena, starts the player and combat view,
    /// and starts a screenshot run when one was requested. Later packages hang the menu, sessions
    /// and map loading off this.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public const string Version = "0.0.3";

        void Awake()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            Debug.Log("Duck and Cover " + Version + ": booted on Unity " + Application.unityVersion +
                      ", render pipeline " + (pipeline == null ? "none" : pipeline.name) +
                      ", network " + NetBridge.Status + ".");
            if (pipeline == null)
                Debug.LogWarning("Duck and Cover: no render pipeline asset is assigned. Check Project Settings > Graphics (URP).");

            if (GameObject.Find("Level") == null) BootRoom.Build();

            SessionRunner.Ensure();
            if (NetSmoke.TryStart(gameObject)) return; // a scripted network run: no panel, no screenshots, no bodies

            new GameObject("CombatView").AddComponent<CombatView>();

            var panel = gameObject.AddComponent<SessionDebugPanel>();
            string captureDir = CaptureRequest.Consume();
            if (captureDir != null)
            {
                panel.visible = false;
                var director = gameObject.AddComponent<ScreenshotDirector>();
                director.outputDirectory = captureDir;
            }
        }
    }
}
