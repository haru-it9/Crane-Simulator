// Standalone test doubles. Never imported into Assets or used in a Unity build.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object
    {
        public static List<object> Scene = new List<object>();
        public static T FindObjectOfType<T>(bool inactive = false) where T : class { return Scene.Find(x => x is T) as T; }
        public static T[] FindObjectsOfType<T>(bool inactive = false) where T : class { return Scene.FindAll(x => x is T).ConvertAll(x => x as T).ToArray(); }
        public static void Destroy(Object o) { }
        public static void DontDestroyOnLoad(Object o) { }
    }
    public class Coroutine {}
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        public bool enabled = true; public string name="test"; public Transform transform=new Transform();
        public Coroutine StartCoroutine(System.Collections.IEnumerator routine) { return new Coroutine(); } public void StopCoroutine(Coroutine routine) {}
        public T GetComponentInParent<T>() where T:class { return GetComponent<T>(); }
        public Dictionary<Type, object> components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class { object o; return components.TryGetValue(typeof(T), out o) ? o as T : null; }
        public T GetComponentInChildren<T>(bool inactive = false) where T : class { return GetComponent<T>(); }
    }
    public class GameObject : Object { public Transform transform=new Transform(); public Dictionary<Type,object> components=new Dictionary<Type,object>(); public bool activeSelf=>activeInHierarchy; public T GetComponent<T>() where T:class { object o;return components.TryGetValue(typeof(T),out o)?o as T:null; } public T GetComponentInChildren<T>(bool inactive=false) where T:class { return GetComponent<T>(); } public GameObject() {} public GameObject(string n) { name=n; } public string name; public bool activeInHierarchy = true; public void SetActive(bool value) { activeInHierarchy=value; } public T AddComponent<T>() where T:new() { return new T(); } }
    public class AudioClip : Object
    {
        public float[] Samples; public int SampleRate;
        public static AudioClip Create(string n,int count,int channels,int rate,bool stream) { return new AudioClip { Samples=new float[count],SampleRate=rate }; }
        public bool SetData(float[] samples,int offset) { Samples=samples;return true; }
    }
    public class AudioSource : Object
    {
        public bool playOnAwake,loop,ignoreListenerPause,Stopped; public float spatialBlend,volume,pitch; public AudioClip clip; public double Scheduled; public int Plays,priority;
        public void Stop() { Stopped=true; } public void PlayScheduled(double t) { Scheduled=t;Stopped=false;Plays++; }
    }
    public static class AudioSettings { public static double dspTime; public static int outputSampleRate=48000; }
    public static class AudioListener { public static bool pause; }
    public class Transform { public Vector3 position; }
    public class TextAsset : Object { public string text,name="test.csv"; public TextAsset(string content) { text=content; } }
    public class RectTransform : Transform { public GameObject gameObject = new GameObject(); public void GetWorldCorners(Vector3[] p) {} }
    public class Camera { }
    public struct Vector2 { public float x,y; public Vector2(float a,float b) { x=a;y=b; } public static Vector2 zero => new Vector2(); }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public float sqrMagnitude => x*x+y*y+z*z; public static Vector3 zero => new Vector3();
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
    }
    public static class Time { public static double realtimeSinceStartupAsDouble, timeAsDouble; public static float timeScale=1,deltaTime,unscaledDeltaTime; public static float realtimeSinceStartup => (float)realtimeSinceStartupAsDouble; public static float unscaledTime => realtimeSinceStartup; public static float time => (float)timeAsDouble; public static int frameCount; }
    public static class Mathf { public static float Max(float a,float b) => Math.Max(a,b); public static int Max(int a,int b) => Math.Max(a,b); public static float Abs(float v) => Math.Abs(v); public static int CeilToInt(float value)=>(int)Math.Ceiling(value); public static bool Approximately(float a,float b)=>Math.Abs(a-b)<.00001; public static int Clamp(int v,int a,int b)=>Math.Min(b,Math.Max(a,v)); public static float Clamp(float v,float a,float b) => Math.Min(b,Math.Max(a,v)); }
    public static class Random { public static int Range(int min,int max)=>min; public static float Range(float min,float max)=>min; }
    public static class Input
    {
        public static HashSet<KeyCode> keys = new HashSet<KeyCode>();
        public static bool GetKey(KeyCode key) { return keys.Contains(key); }
        public static Dictionary<string,float> axes = new Dictionary<string,float>();
        public static Dictionary<string,bool> buttons = new Dictionary<string,bool>();
        public static string ErrorAxis;
        public static float GetAxis(string s) { float v; return axes.TryGetValue(s,out v) ? v : 0; }
        public static float GetAxisRaw(string s) { if (s==ErrorAxis) throw new ArgumentException("Unknown axis: "+s);return GetAxis(s); }
        public static bool GetButtonDown(string s) { return GetButton(s); }
        public static bool GetButton(string s) { bool v; return buttons.TryGetValue(s,out v) && v; }
    }
    public static class Application { public static string unityVersion = "test", version = "1"; public static bool isFocused = true,isPlaying=true; }
    public static class Screen { public static int width=1920,height=1080; }
    public static class Debug { public static void Log(object s,object context=null) {} public static void LogWarning(object s,object context=null) {} public static int Errors; public static void LogError(object s,object context=null) { Errors++; Console.WriteLine(s); } }
    public static class JsonUtility
    {
        static readonly Dictionary<string,object> values=new Dictionary<string,object>();
        public static string ToJson(object v) { string json="{\"test\":true,\"id\":"+values.Count+"}";values[json]=v;return json; }
        public static T FromJson<T>(string json) where T:new()
        {
            T copy=new T(); foreach(var f in typeof(T).GetFields()) f.SetValue(copy,f.GetValue(values[json]));return copy;
        }
    }
    public static class RectTransformUtility { public static Vector2 WorldToScreenPoint(Camera c, Vector3 p) { return new Vector2(p.x,p.y); } public static bool RectangleContainsScreenPoint(RectTransform r, Vector2 p, Camera c) { return true; } }
    public class DisallowMultipleComponent : Attribute {}
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int n) {} }
    public enum RuntimeInitializeLoadType { AfterSceneLoad }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute { public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType t) {} }
    public class SerializeField : Attribute {}
    public class Header : Attribute { public Header(string s) {} }
    public class Tooltip : Attribute { public Tooltip(string s) {} }
    public class Min : Attribute { public Min(float v) {} }
    public class Range : Attribute { public Range(float a,float b) {} }
    public class HideInInspector : Attribute {}
    public class TextArea : Attribute { public TextArea(int min,int max) {} }
    public class ContextMenu : Attribute { public ContextMenu(string label) {} }
    public enum KeyCode { None=0,Minus=45,Equals=61,Semicolon=59,KeypadMinus=269,KeypadPlus=270 }
    public enum EventType { KeyDown,KeyUp,Repaint }
    public class Event { public static Event current; public EventType type; public KeyCode keyCode; }
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1) { this.r=r;this.g=g;this.b=b;this.a=a; }
        public static Color red => new Color(1,0,0);
        public static Color blue => new Color(0,0,1);
    }
}
namespace UnityEngine.Events { public enum UnityEventCallState { Off,EditorAndRuntime,RuntimeOnly } }
namespace AOT { public class MonoPInvokeCallbackAttribute : Attribute { public MonoPInvokeCallbackAttribute(Type t) {} } }
namespace UnityEngine.UI
{
    public struct ColorBlock { public UnityEngine.Color normalColor,highlightedColor,selectedColor,pressedColor,disabledColor; }
    public class Image : UnityEngine.MonoBehaviour { public UnityEngine.Color color; public bool raycastTarget=true; }
    public class Text : UnityEngine.MonoBehaviour { public string text; }
    public class Button : UnityEngine.MonoBehaviour
    {
        public class ClickEvent
        {
            public class Persistent { public UnityEngine.Object Target;public string Method;public Action Callback;public UnityEngine.Events.UnityEventCallState State=UnityEngine.Events.UnityEventCallState.RuntimeOnly; }
            public List<Persistent> persistent=new List<Persistent>();readonly List<Action> callbacks=new List<Action>();
            public int GetPersistentEventCount() { return persistent.Count; }
            public UnityEngine.Object GetPersistentTarget(int i) { return persistent[i].Target; }
            public string GetPersistentMethodName(int i) { return persistent[i].Method; }
            public void SetPersistentListenerState(int i,UnityEngine.Events.UnityEventCallState state) { persistent[i].State=state; }
            public void RemoveListener(Action callback) { callbacks.RemoveAll(c=>c==callback); }
            public void AddListener(Action callback) { callbacks.Add(callback); }
            public void Invoke() { foreach(var p in persistent) if(p.State!=UnityEngine.Events.UnityEventCallState.Off) p.Callback();foreach(var callback in callbacks.ToArray()) callback(); }
        }
        public ClickEvent onClick=new ClickEvent(); public bool interactable; public ColorBlock colors; public Image targetGraphic=new Image();
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
    public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "TestScene" }; } }
}
namespace Tobii.Gaming
{
    public class GazePoint { public bool IsValid; public UnityEngine.Vector2 Viewport,Screen; public float Timestamp; public static GazePoint Invalid => new GazePoint { Viewport=new UnityEngine.Vector2(float.NaN,float.NaN),Screen=new UnityEngine.Vector2(float.NaN,float.NaN) }; }
    public static class TobiiAPI { public static bool IsConnected; public static GazePoint gaze = new GazePoint(); public static GazePoint GetGazePoint() { return gaze; } }
}

public class CraneStatusManager { public enum WorkPhase { Move1,LiftUp,Move2,Place,PlaceToTrack } public enum ErrorType { None,ErrorA,ErrorB,ErrorC } }
public static class ExperimentPauseManager { public static bool IsPaused; public static double ActiveRealtime=>UnityEngine.Time.realtimeSinceStartupAsDouble; }
public class CraneInstance:UnityEngine.MonoBehaviour { public int CraneId; public BoardGenerator BoardGenerator; public CraneUnit CraneUnit; public LifMagSystem LifMagSystem; }
public class CraneUnit { public LifMagSystem magnet; public void ClearInterventionBoardAttachment() { magnet.AttachedBoards.Clear(); } public void SetInterventionBoardAttached(UnityEngine.GameObject board,UnityEngine.Vector3 pos,UnityEngine.Vector3 rot,bool append=false) { if(!append) magnet.AttachedBoards.Clear(); magnet.AttachedBoards.Add(board); } }
public class CraneRegistry { public CraneInstance[] cranes; public int ActiveCraneCount; public void SetActiveCraneCount(int n) { ActiveCraneCount=n; } public CraneInstance GetCraneByRuntimeIndex(int i) { return i>=0&&i<cranes.Length?cranes[i]:null; } }
public class CraneOperationManager { public bool IsTaskSwitchExperimentMode,IsOperationInputLocked; public int CurrentCraneIndex; public bool FailSelect; public void BeginTaskSwitchExperimentMode() { IsTaskSwitchExperimentMode=true; } public void EndTaskSwitchExperimentMode() { IsTaskSwitchExperimentMode=false; } public bool SelectCraneForTaskSwitch(int i) { if(FailSelect)return false;CurrentCraneIndex=i;return true; } public void SetTaskSwitchOperationInputLocked(bool value) { IsOperationInputLocked=value; } public bool TryGetInterventionStartLocalZ(int index,out float value) {value=0;return false;} }
public class CraneInterventionScenarioManager { public BoardGenerator generator; public void ClearScenarioByCraneIndex(int i) {} public void SetupInterventionState(CraneUnit u,CraneStatusManager.WorkPhase phase,CraneStatusManager.ErrorType error,int i,float? z) {u.ClearInterventionBoardAttachment();} public BoardGenerator GetBoardGeneratorForCrane(int i) {return generator;} }
public class TaskSwitchPhaseTracker { public int CraneIndex; public CraneStatusManager.WorkPhase CurrentPhase; public event Action<TaskSwitchPhaseTracker,CraneStatusManager.WorkPhase,CraneStatusManager.WorkPhase> PhaseBoundaryReached; public void Configure(int i,string name,CraneStatusManager.WorkPhase phase) {CraneIndex=i;CurrentPhase=phase;} }
public class CraneWorkTargetManager:UnityEngine.MonoBehaviour { public bool HasTarget; public int CraneIndex; public CraneWorkTargetData CurrentTarget; public event Action<CraneWorkTargetManager,CraneWorkTargetData> TargetChanged; public void SetFixedTarget(float x,float z,int point,CraneWorkTargetKind kind,CraneWorkTargetSource source=CraneWorkTargetSource.Manual) {HasTarget=true;CurrentTarget=new CraneWorkTargetData {targetX=x,targetZ=z,pointIndex=point,targetKind=kind,isValid=true};TargetChanged?.Invoke(this,CurrentTarget);} public bool SetFixedTargetFromPoint(int point,CraneWorkTargetXSelection selection,CraneWorkTargetKind kind,CraneWorkTargetSource source) {float x,z;CraneWorkCoordinateUtility.TryCreatePointTarget(CraneIndex,point,selection,out x,out z);SetFixedTarget(x,z,point,kind,source);return true;} public void ReleaseFixedTarget(bool clear) {if(clear)HasTarget=false;} }
public class CraneWorkPhaseTracker:UnityEngine.MonoBehaviour { public bool IsMonitoring,IsMajorPhaseCompleted; public CraneStatusManager.WorkPhase CurrentMajorPhase; public string CurrentStepId="Step"; public event Action<CraneWorkPhaseTracker,CraneStatusManager.WorkPhase> MajorPhaseCompleted; public event Action<CraneWorkPhaseTracker,CraneStatusManager.WorkPhase,string> PickupWeightInvalidated; public bool ConfigurePhase(CraneStatusManager.WorkPhase phase,bool start) {CurrentMajorPhase=phase;IsMonitoring=start;IsMajorPhaseCompleted=false;return true;} public bool ConfigurePhaseAtStep(CraneStatusManager.WorkPhase phase,string step,bool start) {CurrentStepId=step;return ConfigurePhase(phase,start);} public void StopMonitoring() {IsMonitoring=false;} public bool ResumeMonitoring() {IsMonitoring=true;return true;} public void Complete() {IsMajorPhaseCompleted=true;IsMonitoring=false;MajorPhaseCompleted?.Invoke(this,CurrentMajorPhase);} }
public class LifMagSystem { public List<UnityEngine.GameObject> AttachedBoards=new List<UnityEngine.GameObject>(); public float GetAttachedTotalWeightKgForDisplay() {float weight=0;foreach(var board in AttachedBoards)weight+=board.GetComponent<BoardInfo>().Weight;return weight;} }
public class BoardInfo { public float Weight,SizeY=.1f; }
public class BoardGenerator { public int RequestedCount; public List<UnityEngine.GameObject> Boards=new List<UnityEngine.GameObject>(); public bool TryGetPickupTargetWeightKg(float x,float z,out float w,out int c,out int spawn) {return TryGetPickupTargetWeightKg(x,z,1,out w,out c,out spawn);} public bool TryGetPickupTargetWeightKg(float x,float z,int count,out float w,out int c,out int spawn) {List<UnityEngine.GameObject> boards;bool ok=TryGetPickupBoards(x,z,count,out boards,out w,out spawn);c=ok?count:0;return ok;} public bool TryGetPickupBoards(float x,float z,int count,out List<UnityEngine.GameObject> boards,out float weight,out int spawn) {RequestedCount=count;boards=Boards.GetRange(0,Math.Min(count,Boards.Count));weight=0;spawn=0;foreach(var b in boards)weight+=b.GetComponent<BoardInfo>().Weight;return boards.Count==count;} }
public partial class TaskSwitchExperimentManager { private void InitializeSecondaryTaskUi() {} private void SubscribeSecondaryPauseEvents() {} private void UnsubscribeSecondaryPauseEvents() {} private void DisposeSecondarySubtask() {} private void UpdateSecondarySubtask() {} private void StartSecondarySubtask() {} private void StopSecondarySubtask(string s) {} private void ResetSecondaryExperiment() {} }
