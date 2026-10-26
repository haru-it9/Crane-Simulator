using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public string name; }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T:class { return gameObject.GetComponent<T>(); }
        public T GetComponentInChildren<T>(bool includeInactive=false) where T:class { return gameObject.GetComponent<T>(); }
    }
    public class MonoBehaviour : Component
    {
        public bool isActiveAndEnabled = true;
        public static T FindObjectOfType<T>() where T:class { return null; }
    }
    public class ScriptableObject : Object { }
    public class GameObject : Object
    {
        public bool activeSelf = true;
        public Dictionary<Type,object> Components = new Dictionary<Type,object>();
        public void SetActive(bool value) { activeSelf=value; }
        public T GetComponent<T>() where T:class { object v;return Components.TryGetValue(typeof(T),out v) ? v as T : null; }
    }
    public class Transform : Component { public Vector3 position,localEulerAngles; }
    public class RectTransform : Transform { public Rect rect = new Rect(-50,-80,100,160); }
    public struct Vector2 { public float x,y; public Vector2(float x,float y) { this.x=x;this.y=y; } }
    public struct Vector3
    {
        public float x,y,z; public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero => new Vector3();
    }
    public struct Rect
    {
        public float xMin,yMin,width,height;
        public Rect(float x,float y,float w,float h) { xMin=x;yMin=y;width=w;height=h; }
        public float xMax=>xMin+width; public float yMax=>yMin+height;
        public Vector2 center=>new Vector2(xMin+width/2,yMin+height/2);
    }
    public struct Color { public float r,g,b,a; }
    public struct UIVertex { public Vector3 position;public Color color;public static UIVertex simpleVert=>new UIVertex(); }
    public static class Mathf { public static float Max(float a,float b)=>Math.Max(a,b);public static float Abs(float v)=>Math.Abs(v); }
    public static class Debug { public static void LogWarning(object message,object context=null) { } }
    public class SerializeField : Attribute { }
    public class Header : Attribute { public Header(string s) { } }
    public class Tooltip : Attribute { public Tooltip(string s) { } }
    public class Min : Attribute { public Min(float v) { } }
    public class Range : Attribute { public Range(float a,float b) { } }
    public class DisallowMultipleComponent : Attribute { }
    public class AddComponentMenu : Attribute { public AddComponentMenu(string s) { } }
    public class CreateAssetMenu : Attribute { public string fileName,menuName; }
    public class ContextMenu : Attribute { public ContextMenu(string s) { } }
}
namespace UnityEngine.UI
{
    public class Text : UnityEngine.Component { public string text; }
    public class MaskableGraphic : UnityEngine.MonoBehaviour
    {
        public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
        public UnityEngine.Color color;
        protected virtual void OnPopulateMesh(VertexHelper v) { }
    }
    public class VertexHelper
    {
        public int Vertices,Triangles;
        public void Clear() { Vertices=Triangles=0; }
        public void AddVert(UnityEngine.UIVertex v) { Vertices++; }
        public void AddTriangle(int a,int b,int c)
        { if(a>=Vertices||b>=Vertices||c>=Vertices)throw new Exception("Invalid triangle");Triangles++; }
    }
}
public class CraneStatusManager { public enum WorkPhase { Move1,LiftUp,Move2,Place,PlaceToTrack } public enum ErrorType { None } }
public enum CraneWorkTargetXSelection { Random }
public class CraneInstance : UnityEngine.Component { public UnityEngine.Transform InformationTarget; public CraneUnit CraneUnit; }
public class CraneUnit { public bool IsDownwardMovementBlockedByTouchdown; public float LocalY; public bool TryGetMainLifMagLocalY(out float y) { y=LocalY;return true; } }
public class CraneWorkPhaseTracker
{
    public CraneStatusManager.WorkPhase CurrentMajorPhase;
    public CraneWorkStepDefinition CurrentStepDefinition;
    public float GoalX,GoalZ;
    public CraneWorkStepDefinition GetFirstStepDefinition(CraneStatusManager.WorkPhase p)
    { return CraneWorkPhaseProfile.CreateBuiltInDefaults().Find(s=>s.majorPhase==p); }
    public bool TryGetTargetPosition(out float x,out float z) { x=GoalX;z=GoalZ;return true; }
}
public class CraneWorkCycleController { public bool HasPendingNextPhase;public CraneStatusManager.WorkPhase PendingNextPhase; }
public class CraneWorkTargetManager { public bool TryGetTarget(out float x,out float z) { x=999;z=999;return true; } }
public class CraneOperationManager
{
    public bool IsTaskSwitchExperimentMode=true;public int CurrentCraneIndex;
    public CraneInstance CurrentCraneInstance;
}
public static class ExperimentPauseManager { public static bool IsPaused;public static double ActiveRealtime; }
public class TaskSwitchExperimentManager : UnityEngine.MonoBehaviour
{
    public TaskSwitchExperimentState CurrentState=TaskSwitchExperimentState.WaitingForConfirmation;
    public TaskSwitchCraneCondition TargetCondition=new TaskSwitchCraneCondition(),SourceCondition=new TaskSwitchCraneCondition();
    public bool HasPreview=true;public float GoalX=2,GoalZ=-2;
    public event Action<TaskSwitchEventData> ExperimentEventOccurred;
    public List<string> Recorded=new List<string>();
    public bool TryGetCognitiveSupportTargetPosition(int index,CraneStatusManager.WorkPhase p,out float x,out float z)
    { x=GoalX;z=GoalZ;return HasPreview; }
    public void Raise(string name) { ExperimentEventOccurred?.Invoke(new TaskSwitchEventData { eventName=name }); }
    public void RecordCognitiveSupportEvent(string action,string detail) { Recorded.Add(action+":"+detail);Raise("CognitiveSupport"+action); }
}
