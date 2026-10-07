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
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        public bool enabled = true;
        public Dictionary<Type, object> components = new Dictionary<Type, object>();
        public T GetComponent<T>() where T : class { object o; return components.TryGetValue(typeof(T), out o) ? o as T : null; }
        public T GetComponentInChildren<T>(bool inactive = false) where T : class { return GetComponent<T>(); }
    }
    public class GameObject : Object { public GameObject() {} public GameObject(string n) { name=n; } public string name; public bool activeInHierarchy = true; public void SetActive(bool value) { activeInHierarchy=value; } public T AddComponent<T>() where T:new() { return new T(); } }
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
    public static class Mathf { public static float Max(float a,float b) => Math.Max(a,b); public static int Max(int a,int b) => Math.Max(a,b); public static float Abs(float v) => Math.Abs(v); public static float Clamp(float v,float a,float b) => Math.Min(b,Math.Max(a,v)); }
    public static class Input
    {
        public static HashSet<KeyCode> keys = new HashSet<KeyCode>();
        public static bool GetKey(KeyCode key) { return keys.Contains(key); }
        public static Dictionary<string,float> axes = new Dictionary<string,float>();
        public static Dictionary<string,bool> buttons = new Dictionary<string,bool>();
        public static string ErrorAxis;
        public static float GetAxis(string s) { float v; return axes.TryGetValue(s,out v) ? v : 0; }
        public static float GetAxisRaw(string s) { if (s==ErrorAxis) throw new ArgumentException("Unknown axis: "+s);return GetAxis(s); }
        public static bool GetButton(string s) { bool v; return buttons.TryGetValue(s,out v) && v; }
    }
    public static class Application { public static string unityVersion = "test", version = "1"; public static bool isFocused = true; }
    public static class Screen { public static int width=1920,height=1080; }
    public static class Debug { public static void Log(object s) {} public static void LogWarning(object s,object context=null) {} public static int Errors; public static void LogError(object s) { Errors++; } }
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
        public ClickEvent onClick=new ClickEvent();
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
public enum CraneWorkTargetXSelection { Random }
public class CraneStatusManager { public enum WorkPhase { Move1,LiftUp,Move2,Place,PlaceToTrack } public enum ErrorType { None } }
public class SimulatorStartManager : UnityEngine.MonoBehaviour
{
    public enum SimulatorMode { AutomaticIntervention,TaskSwitchExperiment }
    public SimulatorMode CurrentMode;
    public static bool IsOperationEnabled = true;
}
public class DisplayLayoutManager : UnityEngine.MonoBehaviour { public enum DisplayLayoutMode { TaskSwitchDisplay } public DisplayLayoutMode CurrentMode; }
public class CraneInstance : UnityEngine.MonoBehaviour { public UnityEngine.Transform InformationTarget = new UnityEngine.Transform(); public LifMagSystem LifMagSystem; }
public class CraneRegistry : UnityEngine.MonoBehaviour
{
    public CraneInstance[] cranes; public int ActiveCraneCount => cranes.Length;
    public CraneInstance GetCraneByRuntimeIndex(int i) { return i >= 0 && i < cranes.Length ? cranes[i] : null; }
}
public partial class TaskSwitchExperimentManager : UnityEngine.MonoBehaviour
{
    public TaskSwitchMethod SwitchMethod; public TaskSwitchExperimentState CurrentState;
    public TaskSwitchCraneCondition SourceCondition = new TaskSwitchCraneCondition { craneIndex=0 };
    public TaskSwitchCraneCondition TargetCondition = new TaskSwitchCraneCondition { craneIndex=1 };
    public int CurrentSwitchIndex; public bool TargetRunsFullCycle;
    public string WorkConditionsCsvText = ""; public TaskSwitchTargetTaskPattern ActiveTargetTaskPattern = TaskSwitchTargetTaskPattern.Move1ToLiftUp; public string ActiveTargetWorkConditionJson = "";
    public int SourceTotalCycleCount => 3; public float CountdownSeconds => 5; public string SwitchScheduleCsvText => "";
    public event Action<TaskSwitchEventData> ExperimentEventOccurred;
    public int Starts;
    public void StartExperiment() { Starts++;CurrentState=TaskSwitchExperimentState.OperatingSource; }
    public void Emit(string name)
    {
        ExperimentEventOccurred?.Invoke(new TaskSwitchEventData { eventName=name,switchMethod=SwitchMethod,state=CurrentState,
            sourceCraneIndex=0,targetCraneIndex=1,sourcePhase=SourceCondition.workPhase,targetPhase=TargetCondition.workPhase,detail="" });
    }
}
public class CraneOperationManager : UnityEngine.MonoBehaviour
{
    public int CurrentCraneIndex; public bool IsOperationInputLocked;
    public CraneInstance CurrentCraneInstance; public UnityEngine.Vector3 LastAcceptedMovement,requested;
    public float LastAcceptedSpread; public int LastAcceptedInputFrame=-1;
    public string MovementInputMode => "Joystick"; public float MovementDeadZone => .1f;
    public CraneInformationDisplay CurrentInformationDisplay = new CraneInformationDisplay();
    public UnityEngine.Vector3 ReadMovementCommand() { return requested; } public float ReadSpreadCommand() { return 0; }
    public event Action<CraneOperationManager,UnityEngine.Vector3,float> MovementInputAccepted;
    public void Accept(UnityEngine.Vector3 command) { LastAcceptedMovement=command;LastAcceptedInputFrame=UnityEngine.Time.frameCount;MovementInputAccepted?.Invoke(this,command,0); }
}
public class CraneInformationDisplay { public float CurrentDisplayWeightTon,FineAlignTolerance=.05f; public bool CurrentXHighlighted,CurrentZHighlighted,CurrentWeightHighlighted; public int AchievementFrame; }
public class CraneWorkTargetManager : UnityEngine.MonoBehaviour { public float targetX,targetZ; public bool TryGetTarget(out float x,out float z) { x=targetX;z=targetZ;return true; } }
public class CraneWorkLoadPlanManager : UnityEngine.MonoBehaviour { public bool HasPickupTarget=true,HasPlacementPlan=true; public float PickupTargetWeightKg=1000,TargetRemainingWeightKg; }
public class CraneWorkPhaseTracker : UnityEngine.MonoBehaviour
{
    public CraneStatusManager.WorkPhase CurrentMajorPhase; public string CurrentStepId="Move1.FineAlign";
    public bool IsMonitoring=true,PositionConditionLatched; public float StepElapsedSeconds=2,ConditionStableSeconds,PositionExitHysteresis=.01f,CurrentWeightErrorKg;
    public float LastInvalidationWeightKg,LastInvalidationWeightErrorKg,LastInvalidationStepElapsedSeconds; public int LastInvalidationRemovedBoardCount;
    public string CurrentStepConfigurationJson => "{}";
    public float targetX,targetZ; public bool TryGetTargetPosition(out float x,out float z) { x=targetX;z=targetZ;return true; }
    public event Action<CraneWorkPhaseTracker,CraneStatusManager.WorkPhase,string> StepStarted,StepCompleted,StepResumed,PickupWeightInvalidated;
    public event Action<CraneWorkPhaseTracker,CraneStatusManager.WorkPhase> MajorPhaseCompleted;
    public void CompleteStep() { StepCompleted?.Invoke(this,CurrentMajorPhase,CurrentStepId); }
    public void Invalidate() { PickupWeightInvalidated?.Invoke(this,CurrentMajorPhase,CurrentStepId); }
}
public class CraneWorkCycleController : UnityEngine.MonoBehaviour
{
    public bool IsRunning=true,IsPaused,IsWaitingAtBoundary; public int CurrentCycleNumber=1; public CraneStatusManager.WorkPhase CurrentPhase;
    public event Action<CraneWorkCycleController,CraneStatusManager.WorkPhase,int> PhaseStarted,PhaseCompleted;
    public event Action<CraneWorkCycleController,int> CycleCompleted;
    public event Action<CraneWorkCycleController,string> PickupWeightRollback;
    public void StartPhase() { PhaseStarted?.Invoke(this,CurrentPhase,CurrentCycleNumber); }
    public void Complete() { CycleCompleted?.Invoke(this,CurrentCycleNumber);IsRunning=false; }
    public void Rollback() { PickupWeightRollback?.Invoke(this,"test rollback"); }
}
public class LifMagSystem : UnityEngine.MonoBehaviour
{
    public List<UnityEngine.GameObject> AttachedBoards = new List<UnityEngine.GameObject>();
    public bool HasAttachedBoard => AttachedBoards.Count > 0; public bool IsTaskSwitchSafeCurrentHoldActive;
    public float CurrentElectricCurrentA,CurrentRequiredCurrentA,CurrentLiftCapacityKg;
    public string CurrentSliderAxis => "JoyStick1LeftSlider"; public float MaximumCurrentAmpere => 75; public float SafeHoldReleaseCurrentAmpere => 70;
    public float GetAttachedTotalWeightKgForDisplay() { return AttachedBoards.Count*1000; }
    public float ReadRawSliderInput() { return UnityEngine.Input.GetAxis(CurrentSliderAxis); }
    public float ReadSliderCurrentAmpere() { return Math.Max(0,ReadRawSliderInput())*75; }
    public event Action<LifMagSystem,float> TaskSwitchSafeCurrentHoldStarted,TaskSwitchSafeCurrentHoldReleased,ElectricCurrentInputAccepted;
    public event Action<LifMagSystem,string,string> BoardAttachmentChanged;
    public void Current(float v) { CurrentElectricCurrentA=v;ElectricCurrentInputAccepted?.Invoke(this,v); }
    public void Drop() { AttachedBoards.Clear();BoardAttachmentChanged?.Invoke(this,"BoardDetachedInsufficientCurrent","test board"); }
}
