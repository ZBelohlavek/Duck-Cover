using System.IO;
using DuckAndCover.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DuckAndCover.EditorTools
{
    /// <summary>
    /// Builds the generated scenes. Boot is generated: edit this file and run
    /// "Duck and Cover > Rebuild Scenes" (or Tools/unity scenes) instead of editing the scene by hand.
    /// Maps are laid out in the editor and are not touched here.
    /// </summary>
    public static class SceneSetup
    {
        public const string ScenesFolder = "Assets/DuckAndCover/Scenes";
        public const string BootPath = ScenesFolder + "/Boot.unity";

        /// <summary>Builds any generated scene that is missing.</summary>
        public static void EnsureScenes()
        {
            if (!File.Exists(BootPath)) Build();
        }

        /// <summary>Rebuilds every generated scene and the build scene list.</summary>
        public static void Build()
        {
            Directory.CreateDirectory(ScenesFolder);
            BuildBoot();
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootPath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("Duck and Cover: scenes rebuilt.");
        }

        static void BuildBoot()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            var cam = camera.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            camera.AddComponent<AudioListener>();
            camera.transform.position = new Vector3(0f, 1.7f, -4f);

            var boot = new GameObject("Bootstrap");
            boot.AddComponent<GameBootstrap>();

            EditorSceneManager.SaveScene(scene, BootPath);
        }
    }
}
