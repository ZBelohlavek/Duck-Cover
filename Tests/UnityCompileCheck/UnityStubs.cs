// Minimal stand-ins for the Unity, Unity Editor and Netcode APIs the game uses. Signatures mirror
// the real ones. When a script needs an API that isn't here, add it with its real signature; never
// invent an API just to make this build pass.
#pragma warning disable
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static void Destroy(Object obj, float t) { }
        public static void DestroyImmediate(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T FindAnyObjectByType<T>() where T : Object => null;
        public static T Instantiate<T>(T original) where T : Object => original;
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public class ScriptableObject : Object { }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public string tag { get; set; }
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => enabled;
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => null;
        public void StopCoroutine(Coroutine routine) { }
        public void StopAllCoroutines() { }
    }

    public sealed class Coroutine : YieldInstruction { }
    public class YieldInstruction { }
    public class CustomYieldInstruction : IEnumerator
    {
        public object Current => null;
        public bool MoveNext() => false;
        public void Reset() { }
    }
    public sealed class WaitForSeconds : YieldInstruction { public WaitForSeconds(float seconds) { } }
    public class WaitForSecondsRealtime : CustomYieldInstruction { public WaitForSecondsRealtime(float time) { } }
    public sealed class WaitForEndOfFrame : YieldInstruction { }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public sealed class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform => null;
        public bool activeSelf => true;
        public bool isStatic { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default;
        public static GameObject Find(string name) => null;
        public static GameObject CreatePrimitive(PrimitiveType type) => null;
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 forward => default;
        public Transform parent { get; set; }
        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
        public IEnumerator GetEnumerator() { yield break; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1 };
        public static Quaternion Euler(float x, float y, float z) => identity;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { }
    }

    public enum LightType { Spot = 0, Directional = 1, Point = 2 }
    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        public LightShadows shadows { get; set; }
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public bool orthographic { get; set; }
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public RenderTexture targetTexture { get; set; }
        public void Render() { }
    }

    public sealed class AudioListener : Behaviour { }

    public class Material : Object
    {
        public Color color { get; set; }
        public bool HasProperty(string name) => false;
        public void SetColor(string name, Color value) { }
    }

    public class Renderer : Component
    {
        public Material material { get; set; }
        public Material sharedMaterial { get; set; }
    }

    public class Texture : Object
    {
        public int width => 0;
        public int height => 0;
    }

    public enum TextureFormat { RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void Apply() { }
    }

    public static class ImageConversion
    {
        public static byte[] EncodeToPNG(this Texture2D tex) => new byte[0];
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth) { }
        public static RenderTexture active { get; set; }
        public void Release() { }
    }

    public enum LogType { Error = 0, Assert = 1, Warning = 2, Log = 3, Exception = 4 }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
        public static void LogException(Exception exception) { }
    }

    public static class Application
    {
        public delegate void LogCallback(string condition, string stackTrace, LogType type);
        public static event LogCallback logMessageReceived;
        public static string unityVersion => "";
        public static bool isBatchMode => false;
        public static bool isPlaying => false;
        public static string persistentDataPath => "";
        public static int targetFrameRate { get; set; }
        public static void Quit() { }
        public static void Quit(int exitCode) { }
    }

    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType => default;
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Null = 4, Direct3D11 = 2, Direct3D12 = 18, Vulkan = 21, Metal = 16, OpenGLCore = 17 }

    public abstract class RenderPipelineAsset : ScriptableObject { }

    public sealed class GraphicsSettings : Object
    {
        public static RenderPipelineAsset currentRenderPipeline => null;
        public static RenderPipelineAsset defaultRenderPipeline { get; set; }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name => "";
        public string path => "";
        public bool IsValid() => true;
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class MenuItem : Attribute
    {
        public string menuItem;
        public bool validate;
        public int priority;
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }

    public static class EditorPrefs
    {
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
        public static bool GetBool(string key, bool defaultValue) => defaultValue;
        public static void SetBool(string key, bool value) { }
        public static void DeleteKey(string key) { }
    }

    public sealed class EditorApplication
    {
        public static bool isPlaying { get; set; }
        public static void EnterPlaymode() { }
        public static void ExitPlaymode() { }
        public static void Exit(int returnValue) { }
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene(string path, bool enable) { }
    }

    public class EditorBuildSettings : UnityEngine.Object
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }

    public sealed class AssetDatabase
    {
        public static void SaveAssets() { }
        public static void Refresh() { }
    }
}

namespace UnityEditor.SceneManagement
{
    using UnityEngine.SceneManagement;

    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }
    public enum OpenSceneMode { Single = 0, Additive = 1, AdditiveWithoutLoading = 2 }

    public sealed class EditorSceneManager
    {
        public static Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => default;
        public static Scene OpenScene(string scenePath) => default;
        public static Scene OpenScene(string scenePath, OpenSceneMode mode) => default;
        public static bool SaveScene(Scene scene, string dstScenePath) => true;
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => true;
    }
}

namespace Unity.Netcode
{
    public class NetworkManager : UnityEngine.MonoBehaviour
    {
        public static NetworkManager Singleton { get; }
        public bool IsListening { get; }
        public bool IsServer { get; }
        public bool IsClient { get; }
        public bool IsHost { get; }
        public bool IsConnectedClient { get; }
    }
}
