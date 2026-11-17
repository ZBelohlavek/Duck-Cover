using DuckAndCover.Net;
using UnityEngine;
using UnityEngine.Rendering;

namespace DuckAndCover.Game
{
    /// <summary>
    /// First thing that runs in the Boot scene. For now it checks the project is wired up (render
    /// pipeline, network package), builds a small greybox room so there is something to look at,
    /// and starts a screenshot run when one was requested. Later packages hang the menu, sessions
    /// and map loading off this.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        public const string Version = "0.0.1";

        void Awake()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            Debug.Log("Duck and Cover " + Version + ": booted on Unity " + Application.unityVersion +
                      ", render pipeline " + (pipeline == null ? "none" : pipeline.name) +
                      ", network " + NetBridge.Status + ".");
            if (pipeline == null)
                Debug.LogWarning("Duck and Cover: no render pipeline asset is assigned. Check Project Settings > Graphics (URP).");

            if (GameObject.Find("Level") == null) BootRoom.Build();

            string captureDir = CaptureRequest.Consume();
            if (captureDir != null)
            {
                var director = gameObject.AddComponent<ScreenshotDirector>();
                director.outputDirectory = captureDir;
            }
        }
    }
}
