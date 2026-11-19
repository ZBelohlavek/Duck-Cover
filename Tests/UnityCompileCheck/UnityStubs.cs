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
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object => new T[0];
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent, bool instantiateInWorldSpace) where T : Object => original;
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public enum FindObjectsSortMode { None = 0, InstanceID = 1 }

    public class ScriptableObject : Object { }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public string tag { get; set; }
        public T GetComponent<T>() => default;
    }

    public class Collider : Component
    {
        public bool enabled { get; set; }
    }

    [Flags] public enum CollisionFlags { None = 0, Sides = 1, Above = 2, Below = 4 }

    public class CharacterController : Collider
    {
        public bool isGrounded => false;
        public float radius { get; set; }
        public float height { get; set; }
        public Vector3 center { get; set; }
        public float stepOffset { get; set; }
        public float slopeLimit { get; set; }
        public Vector3 velocity => default;
        public CollisionFlags Move(Vector3 motion) => CollisionFlags.None;
    }

    public struct RaycastHit
    {
        public Vector3 point { get; set; }
        public Vector3 normal { get; set; }
        public float distance { get; set; }
        public Collider collider => null;
        public Transform transform => null;
    }

    public enum QueryTriggerInteraction { UseGlobal = 0, Ignore = 1, Collide = 2 }

    public class Physics
    {
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) { hitInfo = default; return false; }
        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => false;
        public static void SyncTransforms() { }
    }

    public enum CursorLockMode { None = 0, Locked = 1, Confined = 2 }

    public class Cursor
    {
        public static CursorLockMode lockState { get; set; }
        public static bool visible { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type requiredComponent) { }
    }

    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public sealed class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
        public static T[] LoadAll<T>(string path) where T : Object => null;
        public static T Load<T>(string path) where T : Object => null;
    }

    public class TextAsset : Object
    {
        public string text => null;
        public byte[] bytes => null;
    }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => default;
        public static string ToJson(object obj) => null;
    }

    public sealed class Font : Object
    {
        public static string[] GetOSInstalledFontNames() => new string[0];
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) => null;
    }

    public sealed class CanvasRenderer : Component
    {
        public bool cull { get; set; }
        public bool cullTransparentMesh { get; set; }
        public int materialCount { get; set; }
        public float GetInheritedAlpha() => 1f;
    }

    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => default;
    }

    public class GUIUtility
    {
        public static int keyboardControl { get; set; }
    }

    public sealed class RectTransformUtility
    {
        public static bool RectangleContainsScreenPoint(RectTransform rect, Vector2 screenPoint, Camera cam) => false;
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) => null;
    }

    public sealed class RectTransform : Transform
    {
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 anchoredPosition { get; set; }
        public Vector2 sizeDelta { get; set; }
    }

    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }

    public sealed class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public Camera worldCamera { get; set; }
        public float planeDistance { get; set; }
        public int sortingOrder { get; set; }
        public bool pixelPerfect { get; set; }
        public float scaleFactor { get; set; }
        public bool isRootCanvas => true;
        public static void ForceUpdateCanvases() { }
    }

    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap = 0, Overflow = 1 }
    public enum VerticalWrapMode { Truncate = 0, Overflow = 1 }

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
        public bool activeInHierarchy => true;
        public bool isStatic { get; set; }
        public string tag { get; set; }
        public int layer { get; set; }
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default;
        public T GetComponentInChildren<T>(bool includeInactive) => default;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
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
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 InverseTransformPoint(Vector3 position) => position;
        public Transform parent { get; set; }
        public Vector3 TransformPoint(Vector3 position) => position;
        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
        public Transform Find(string n) => null;
        public IEnumerator GetEnumerator() { yield break; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float magnitude => 0f;
        public float sqrMagnitude => 0f;
        public Vector3 normalized => this;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static Vector3 Min(Vector3 lhs, Vector3 rhs) => lhs;
        public static Vector3 Max(Vector3 lhs, Vector3 rhs) => lhs;
        public static Vector3 operator *(float d, Vector3 a) => a;
        public static Vector3 operator -(Vector3 a) => a;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => b;
        public static Vector2 zero => default;
        public static Vector2 one => new Vector2(1, 1);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) => default;
        public static implicit operator Color(Color32 c) => default;
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size) { this.center = center; this.size = size; }
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
        public void Encapsulate(Bounds bounds) { }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion identity => new Quaternion { w = 1 };
        public static Quaternion Euler(float x, float y, float z) => identity;
        public static Quaternion LookRotation(Vector3 forward) => identity;
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => identity;
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection) => identity;
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) => lhs;
        public static Quaternion Inverse(Quaternion rotation) => rotation;
        public static Quaternion AngleAxis(float angle, Vector3 axis) => identity;
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color operator *(Color a, float b) => a;
        public static Color operator +(Color a, Color b) => a;
        public static bool operator ==(Color lhs, Color rhs) => lhs.r == rhs.r && lhs.g == rhs.g && lhs.b == rhs.b && lhs.a == rhs.a;
        public static bool operator !=(Color lhs, Color rhs) => !(lhs == rhs);
        public override bool Equals(object other) => other is Color c && this == c;
        public override int GetHashCode() => 0;
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
        public CameraClearFlags clearFlags { get; set; }
        public Color backgroundColor { get; set; }
        public Vector3 WorldToScreenPoint(Vector3 position) => position;
        public void Render() { }
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }

    public sealed class AudioListener : Behaviour { }

    public class Material : Object
    {
        public Material(Material source) { }
        public Material(Shader shader) { }
        public Shader shader { get; set; }
        public void SetTexture(string name, Texture value) { }
        public void SetFloat(string name, float value) { }
        public Color color { get; set; }
        public bool HasProperty(string name) => false;
        public void EnableKeyword(string keyword) { }
        public void SetColor(string name, Color value) { }
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        public void SetVertices(List<Vector3> inVertices) { }
        public void SetNormals(List<Vector3> inNormals) { }
        public void SetUVs(int channel, List<Vector2> uvs) { }
        public void SetTriangles(List<int> triangles, int submesh) { }
        public void SetTriangles(int[] triangles, int submesh) { }
        public UnityEngine.Rendering.IndexFormat indexFormat { get; set; }
        public Bounds bounds { get; set; }
        public void RecalculateBounds() { }
        public void RecalculateTangents() { }
    }

    public sealed class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
        public Mesh mesh { get; set; }
    }

    public enum FogMode { Linear = 1, Exponential = 2, ExponentialSquared = 3 }

    public sealed class RenderSettings : Object
    {
        public static bool fog { get; set; }
        public static FogMode fogMode { get; set; }
        public static Color fogColor { get; set; }
        public static float fogStartDistance { get; set; }
        public static float fogEndDistance { get; set; }
        public static UnityEngine.Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientSkyColor { get; set; }
        public static Color ambientEquatorColor { get; set; }
        public static Color ambientGroundColor { get; set; }
        public static Material skybox { get; set; }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material material { get; set; }
        public Material sharedMaterial { get; set; }
        public Material[] sharedMaterials { get; set; }
        public UnityEngine.Rendering.ShadowCastingMode shadowCastingMode { get; set; }
    }

    public class Motion : Object { }

    public sealed class AnimationClip : Motion
    {
        public float length => 0f;
    }

    public enum AnimatorCullingMode { AlwaysAnimate = 0, CullUpdateTransforms = 1, CullCompletely = 2 }

    public class Animator : Behaviour
    {
        public bool applyRootMotion { get; set; }
        public AnimatorCullingMode cullingMode { get; set; }
    }

    public class MeshRenderer : Renderer { }

    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }

    public class Texture : Object
    {
        public int width => 0;
        public int height => 0;
        public TextureWrapMode wrapMode { get; set; }
        public FilterMode filterMode { get; set; }
    }

    public enum TextureFormat { RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void ReadPixels(Rect source, int destX, int destY) { }
        public void SetPixel(int x, int y, Color color) { }
        public void SetPixels(Color[] colors) { }
        public void Apply(bool updateMipmaps) { }
        public Color GetPixel(int x, int y) => default;
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

    public static class Time
    {
        public static float deltaTime => 0f;
        public static float unscaledDeltaTime => 0f;
        public static float unscaledTime => 0f;
        public static float time => 0f;
        public static float realtimeSinceStartup => 0f;
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
    }

    public struct Mathf
    {
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Abs(float f) => Math.Abs(f);
        public static float Exp(float power) => 0f;
        public static float Pow(float f, float p) => 0f;
        public static float Tan(float f) => 0f;
        public static float Acos(float f) => 0f;
        public const float PI = 3.14159274f;
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = 0.0174532924f;
        public static float Clamp(float value, float min, float max) => value;
        public static int Clamp(int value, int min, int max) => value;
        public static float Max(float a, float b) => a;
        public static int Max(int a, int b) => a;
        public static float Min(float a, float b) => a;
        public static float Lerp(float a, float b, float t) => a;
        public static float LerpAngle(float a, float b, float t) => a;
        public static float MoveTowards(float current, float target, float maxDelta) => target;
        public static float DeltaAngle(float current, float target) => target - current;
        public static float Repeat(float t, float length) => t;
        public static float Atan2(float y, float x) => 0f;
        public static float Asin(float f) => 0f;
        public static float Sin(float f) => 0f;
        public static float Cos(float f) => 0f;
        public static float Sqrt(float f) => 0f;
        public static float SmoothStep(float from, float to, float t) => from;
        public static bool Approximately(float a, float b) => a == b;
    }

    public class GUIStyle
    {
        public static implicit operator GUIStyle(string str) => null;
    }

    public class GUISkin : ScriptableObject
    {
        public GUIStyle box { get; set; }
        public GUIStyle label { get; set; }
        public GUIStyle button { get; set; }
    }

    public class GUI
    {
        public static GUISkin skin { get; set; }
    }

    public sealed class GUILayoutOption { }

    public class GUILayout
    {
        public static void BeginArea(Rect screenRect) { }
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static void EndArea() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static string TextField(string text, params GUILayoutOption[] options) => text;
        public static string TextField(string text, int maxLength, params GUILayoutOption[] options) => text;
        public static void Space(float pixels) { }
    }

    public static class SystemInfo
    {
        public static Rendering.GraphicsDeviceType graphicsDeviceType => default;
    }
}

namespace UnityEngine.UI
{
    public abstract class Graphic : MonoBehaviour
    {
        public virtual Color color { get; set; }
        public virtual bool raycastTarget { get; set; }
        public RectTransform rectTransform => null;
        public virtual void SetVerticesDirty() { }
        public virtual void SetAllDirty() { }
        protected bool useLegacyMeshGeneration { get; set; }
        public CanvasRenderer canvasRenderer => null;
        protected virtual void OnPopulateMesh(VertexHelper vh) { }
    }

    public abstract class MaskableGraphic : Graphic { }

    public class VertexHelper : IDisposable
    {
        public int currentVertCount => 0;
        public void Clear() { }
        public void AddVert(Vector3 position, Color32 color, Vector4 uv0) { }
        public void AddTriangle(int idx0, int idx1, int idx2) { }
        public void Dispose() { }
    }

    public abstract class BaseMeshEffect : MonoBehaviour { }

    public class Shadow : BaseMeshEffect
    {
        public Color effectColor { get; set; }
        public Vector2 effectDistance { get; set; }
    }

    public class Outline : Shadow { }

    public class Image : Graphic { }

    public class Text : Graphic
    {
        public virtual string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
    }

    public class CanvasScaler : MonoBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public float matchWidthOrHeight { get; set; }
    }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType { Null = 4, Direct3D11 = 2, Direct3D12 = 18, Vulkan = 21, Metal = 16, OpenGLCore = 17 }

    public abstract class RenderPipelineAsset : ScriptableObject { }

    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }

    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }

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

namespace UnityEngine.Playables
{
    public enum DirectorUpdateMode { DSPClock = 0, GameTime = 1, UnscaledGameTime = 2, Manual = 3 }

    public struct PlayableHandle { }

    public interface IPlayable { PlayableHandle GetHandle(); }

    public interface IPlayableOutput { }

    public struct PlayableGraph
    {
        public static PlayableGraph Create(string name) => default;
        public bool IsValid() => false;
        public void Destroy() { }
        public void SetTimeUpdateMode(DirectorUpdateMode value) { }
        public void Evaluate(float deltaTime) { }
        public bool Connect<U, V>(U source, int sourceOutputPort, V destination, int destinationInputPort) where U : struct, IPlayable where V : struct, IPlayable => true;
    }

    public static class PlayableExtensions
    {
        public static void SetInputWeight<U>(this U playable, int inputIndex, float weight) where U : struct, IPlayable { }
        public static void SetTime<U>(this U playable, double value) where U : struct, IPlayable { }
    }

    public static class PlayableOutputExtensions
    {
        public static void SetSourcePlayable<U, V>(this U output, V value) where U : struct, IPlayableOutput where V : struct, IPlayable { }
    }
}

namespace UnityEngine.Animations
{
    using UnityEngine.Playables;

    public struct AnimationPlayableOutput : IPlayableOutput
    {
        public static AnimationPlayableOutput Create(PlayableGraph graph, string name, Animator target) => default;
    }

    public struct AnimationMixerPlayable : IPlayable
    {
        public PlayableHandle GetHandle() => default;
        public static AnimationMixerPlayable Create(PlayableGraph graph, int inputCount = 0) => default;
    }

    public struct AnimationClipPlayable : IPlayable
    {
        public PlayableHandle GetHandle() => default;
        public static AnimationClipPlayable Create(PlayableGraph graph, AnimationClip clip) => default;
        public void SetApplyFootIK(bool value) { }
    }
}

namespace UnityEditor
{
    public class AssetImporter : UnityEngine.Object { }

    public enum ModelImporterMaterialImportMode { None = 0, ImportStandard = 1, ImportViaMaterialDescription = 2 }
    public enum ModelImporterAnimationType { None = 0, Legacy = 1, Generic = 2, Human = 3 }
    public enum ModelImporterAvatarSetup { NoAvatar = 0, CreateFromThisModel = 1, CopyFromOther = 2 }
    public enum ModelImporterAnimationCompression { Off = 0, KeyframeReduction = 1, KeyframeReductionAndCompression = 2, Optimal = 3 }

    public class ModelImporter : AssetImporter
    {
        public ModelImporterMaterialImportMode materialImportMode { get; set; }
        public bool importCameras { get; set; }
        public bool importLights { get; set; }
        public bool useFileScale { get; set; }
        public float globalScale { get; set; }
        public ModelImporterAnimationType animationType { get; set; }
        public bool importAnimation { get; set; }
        public ModelImporterAvatarSetup avatarSetup { get; set; }
        public ModelImporterAnimationCompression animationCompression { get; set; }
    }

    public class AssetPostprocessor
    {
        public string assetPath { get; set; }
        public AssetImporter assetImporter => null;
    }

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
        public string path { get; set; }
        public bool enabled { get; set; }
    }

    public enum BuildTarget { StandaloneOSX = 2, StandaloneWindows = 5, StandaloneWindows64 = 19, StandaloneLinux64 = 24 }

    [Flags]
    public enum BuildOptions { None = 0, Development = 1 }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
    }

    public class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions buildPlayerOptions) => null;
    }

    public class EditorUserBuildSettings
    {
        public static BuildTarget activeBuildTarget => default;
    }

    public class EditorBuildSettings : UnityEngine.Object
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
    }

    public enum TextureImporterType { Default = 0, NormalMap = 1, GUI = 2, Sprite = 8, Cursor = 7, Cookie = 4, Lightmap = 6, SingleChannel = 10 }

    public enum TextureImporterShape { Texture2D = 1, TextureCube = 2, Texture2DArray = 4, Texture3D = 8 }

    public sealed class TextureImporter : AssetImporter
    {
        public TextureImporterShape textureShape { get; set; }
        public TextureImporterType textureType { get; set; }
        public bool sRGBTexture { get; set; }
        public bool mipmapEnabled { get; set; }
        public int maxTextureSize { get; set; }
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

namespace UnityEditor.Build.Reporting
{
    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    public struct BuildSummary
    {
        public BuildResult result { get; }
        public int totalErrors { get; }
    }

    public sealed class BuildReport : UnityEngine.Object
    {
        public BuildSummary summary => default;
    }
}

namespace UnityEngine.InputSystem
{
    public class InputControl { }

    public class InputDevice : InputControl { }

    namespace Controls
    {
        public class ButtonControl : InputControl
        {
            public bool isPressed => false;
            public bool wasPressedThisFrame => false;
            public bool wasReleasedThisFrame => false;
        }

        public class KeyControl : ButtonControl { }
        public class Vector2Control : InputControl<UnityEngine.Vector2> { }
        public class DeltaControl : Vector2Control { }
    }

    public class InputControl<TValue> : InputControl where TValue : struct
    {
        public TValue ReadValue() => default;
    }

    public class Keyboard : InputDevice
    {
        public static Keyboard current { get; }
        public Controls.KeyControl f1Key => null;
        public Controls.KeyControl wKey => null;
        public Controls.KeyControl aKey => null;
        public Controls.KeyControl sKey => null;
        public Controls.KeyControl dKey => null;
        public Controls.KeyControl cKey => null;
        public Controls.KeyControl qKey => null;
        public Controls.KeyControl rKey => null;
        public Controls.KeyControl spaceKey => null;
        public Controls.KeyControl escapeKey => null;
        public Controls.KeyControl leftShiftKey => null;
        public Controls.KeyControl leftCtrlKey => null;
        public Controls.KeyControl digit1Key => null;
        public Controls.KeyControl digit2Key => null;
        public Controls.KeyControl digit3Key => null;
        public Controls.KeyControl digit4Key => null;
        public Controls.KeyControl digit5Key => null;
        public Controls.KeyControl digit6Key => null;
        public Controls.KeyControl digit7Key => null;
        public Controls.KeyControl eKey => null;
        public Controls.KeyControl fKey => null;
        public Controls.KeyControl xKey => null;
        public Controls.KeyControl zKey => null;
        public Controls.KeyControl vKey => null;
    }

    public class Pointer : InputDevice
    {
        public Controls.Vector2Control position => null;
        public Controls.DeltaControl delta => null;
    }

    public class Mouse : Pointer
    {
        public static Mouse current { get; }
        public Controls.ButtonControl leftButton => null;
        public Controls.ButtonControl rightButton => null;
        public Controls.DeltaControl scroll => null;
    }
}

namespace Unity.Collections
{
    public enum Allocator { Invalid = 0, None = 1, Temp = 2, TempJob = 3, Persistent = 4 }
}

namespace Unity.Networking.Transport.Relay
{
    public struct RelayServerData { }
}

namespace Unity.Netcode
{
    public enum NetworkDelivery { Unreliable, UnreliableSequenced, Reliable, ReliableSequenced, ReliableFragmentedSequenced }

    public abstract class NetworkTransport : UnityEngine.MonoBehaviour { }

    [Serializable]
    public class NetworkConfig
    {
        public NetworkTransport NetworkTransport = null;
        public uint TickRate = 30;
        public bool ConnectionApproval = false;
        public byte[] ConnectionData = new byte[0];
        public bool EnableSceneManagement = true;
    }

    public class NetworkManager : UnityEngine.MonoBehaviour
    {
        public const ulong ServerClientId = 0;
        public static NetworkManager Singleton { get; private set; }
        public NetworkConfig NetworkConfig;
        public ulong LocalClientId { get; internal set; }
        public bool IsListening { get; }
        public bool IsServer { get; }
        public bool IsClient { get; }
        public bool IsHost { get; }
        public bool IsConnectedClient { get; }
        public string DisconnectReason => "";
        public CustomMessagingManager CustomMessagingManager { get; private set; }
        public Action<ConnectionApprovalRequest, ConnectionApprovalResponse> ConnectionApprovalCallback { get; set; }
        public event Action<ulong> OnClientConnectedCallback;
        public event Action<ulong> OnClientDisconnectCallback;
        public bool StartServer() => true;
        public bool StartClient() => true;
        public bool StartHost() => true;
        public void DisconnectClient(ulong clientId) { }
        public void DisconnectClient(ulong clientId, string reason = null) { }
        public void Shutdown(bool discardMessageQueue = false) { }

        public class ConnectionApprovalResponse
        {
            public bool Approved;
            public bool CreatePlayerObject;
            public uint? PlayerPrefabHash;
            public UnityEngine.Vector3? Position;
            public UnityEngine.Quaternion? Rotation;
            public bool Pending;
            public string Reason;
        }

        public struct ConnectionApprovalRequest
        {
            public byte[] Payload;
            public ulong ClientNetworkId;
        }
    }

    public class CustomMessagingManager
    {
        public delegate void HandleNamedMessageDelegate(ulong senderClientId, FastBufferReader messagePayload);
        public void RegisterNamedMessageHandler(string name, HandleNamedMessageDelegate callback) { }
        public void UnregisterNamedMessageHandler(string name) { }
        public void SendNamedMessage(string messageName, ulong clientId, FastBufferWriter messageStream, NetworkDelivery networkDelivery = NetworkDelivery.ReliableSequenced) { }
    }

    public struct FastBufferWriter : IDisposable
    {
        public struct ForPrimitives { }
        public FastBufferWriter(int size, Unity.Collections.Allocator allocator, int maxSize = -1) { }
        public int Length => 0;
        public int Position => 0;
        public void WriteValueSafe<T>(in T value, ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { }
        public void WriteBytesSafe(byte[] value, int size = -1, int offset = 0) { }
        public void Dispose() { }
    }

    public struct FastBufferReader : IDisposable
    {
        public int Length => 0;
        public int Position => 0;
        public void ReadValueSafe<T>(out T value, FastBufferWriter.ForPrimitives unused = default) where T : unmanaged, IComparable, IConvertible, IComparable<T>, IEquatable<T> { value = default; }
        public void ReadBytesSafe(ref byte[] value, int size, int offset = 0) { }
        public void Dispose() { }
    }
}

namespace Unity.Netcode.Transports.UTP
{
    public class UnityTransport : NetworkTransport
    {
        public void SetConnectionData(string ipv4Address, ushort port, string listenAddress = null) { }
        public void SetRelayServerData(Unity.Networking.Transport.Relay.RelayServerData serverData) { }
    }
}

#if DUCK_RELAY
// Unity services (com.unity.services.multiplayer). Written from the documented API, not checked
// against the package source: the registry is out of reach from the cloud.
namespace Unity.Services.Core
{
    public enum ServicesInitializationState { Uninitialized, Initializing, Initialized }

    public static class UnityServices
    {
        public static ServicesInitializationState State => default;
        public static System.Threading.Tasks.Task InitializeAsync() => System.Threading.Tasks.Task.CompletedTask;
    }
}

namespace Unity.Services.Authentication
{
    public interface IAuthenticationService
    {
        bool IsSignedIn { get; }
        System.Threading.Tasks.Task SignInAnonymouslyAsync();
    }

    public static class AuthenticationService
    {
        public static IAuthenticationService Instance => null;
    }
}

namespace Unity.Services.Relay.Models
{
    public class Allocation { public Guid AllocationId { get; } }
    public class JoinAllocation { public Guid AllocationId { get; } }

    public static class AllocationUtils
    {
        public static Unity.Networking.Transport.Relay.RelayServerData ToRelayServerData(this Allocation allocation, string connectionType) => default;
        public static Unity.Networking.Transport.Relay.RelayServerData ToRelayServerData(this JoinAllocation allocation, string connectionType) => default;
    }
}

namespace Unity.Services.Relay
{
    using Unity.Services.Relay.Models;

    public interface IRelayService
    {
        System.Threading.Tasks.Task<Allocation> CreateAllocationAsync(int maxConnections, string region = null);
        System.Threading.Tasks.Task<string> GetJoinCodeAsync(Guid allocationId);
        System.Threading.Tasks.Task<JoinAllocation> JoinAllocationAsync(string joinCode);
    }

    public static class RelayService
    {
        public static IRelayService Instance => null;
    }
}
#endif
