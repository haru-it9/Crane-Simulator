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
        public T AddComponent<T>() where T:Component,new() { var c=new T();c.gameObject=this;Components[typeof(T)]=c;return c; }
    }
    public class CanvasGroup : Component { public float alpha=1f;public bool interactable,blocksRaycasts; }
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
    public struct Color
    {
        public float r,g,b,a;
        public Color(float r,float g,float b,float a=1) {this.r=r;this.g=g;this.b=b;this.a=a;}
        public static Color white => new Color(1,1,1,1);
        public static Color Lerp(Color a,Color b,float t) {t=Mathf.Clamp01(t);return new Color(a.r+(b.r-a.r)*t,a.g+(b.g-a.g)*t,a.b+(b.b-a.b)*t,a.a+(b.a-a.a)*t);}
    }
    public struct UIVertex { public Vector3 position;public Color color;public static UIVertex simpleVert=>new UIVertex(); }
    public static class Mathf
    {
        public static float Max(float a,float b)=>Math.Max(a,b);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Min(float a,float b)=>Math.Min(a,b);
        public static int Clamp(int v,int a,int b)=>Math.Max(a,Math.Min(v,b));
        public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(v,b));
        public static float Clamp01(float v)=>Clamp(v,0,1);
        public static int CeilToInt(float v)=>(int)Math.Ceiling(v);
        public static float Abs(float v)=>Math.Abs(v);
    }
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
    public class Text : UnityEngine.Component { public string text; public UnityEngine.Color color=UnityEngine.Color.white; }
    public class MaskableGraphic : UnityEngine.MonoBehaviour
    {
        public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
        public UnityEngine.Color color;
        public void SetVerticesDirty() { }
        protected virtual void OnPopulateMesh(VertexHelper v) { }
    }
    public class VertexHelper
    {
        public int Vertices,Triangles,BadWinding;
        public List<UnityEngine.Vector3> Points = new List<UnityEngine.Vector3>();
        public void Clear() { Vertices=Triangles=BadWinding=0;Points.Clear(); }
        public void AddVert(UnityEngine.UIVertex v) { Vertices++;Points.Add(v.position); }
        public void AddTriangle(int a,int b,int c)
        {
            if(a>=Vertices||b>=Vertices||c>=Vertices)throw new Exception("Invalid triangle");
            var p=Points[a];var q=Points[b];var r=Points[c];
            if((q.x-p.x)*(r.y-p.y)-(q.y-p.y)*(r.x-p.x)<=0)BadWinding++;
            Triangles++;
        }
    }
}
public class CraneStatusManager { public enum WorkPhase { Move1,LiftUp,Move2,Place,PlaceToTrack } public enum ErrorType { None } }
public class CraneInformationDisplay : UnityEngine.MonoBehaviour
{ public UnityEngine.Color AchievedValueColor=new UnityEngine.Color(.65f,1f,.1f); }
public enum CraneWorkTargetXSelection { Random }
public class CraneInstance : UnityEngine.Component { public UnityEngine.Transform InformationTarget; public CraneUnit CraneUnit; public LifMagSystem LifMagSystem; }
public class LifMagSystem
{
    public bool IsInputValueLiftMode=true;public float CurrentLiftCapacityKg,CurrentElectricCurrentA;
    public float MaximumCurrentAmpere=75f;
    public bool IsElectricCurrentInputReady=true;
    public event Action<LifMagSystem,float> ElectricCurrentInputAccepted;
    public int CurrentInputSubscriberCount=>ElectricCurrentInputAccepted==null?0:ElectricCurrentInputAccepted.GetInvocationList().Length;
    public void AcceptCurrent(float value) { CurrentElectricCurrentA=value;ElectricCurrentInputAccepted?.Invoke(this,value); }
    public float AttachedWeight;
    public List<UnityEngine.GameObject> AttachedBoards=new List<UnityEngine.GameObject>();
    public bool HasAttachedBoard=>AttachedBoards.Count>0;
    public float GetAttachedTotalWeightKgForDisplay()=>AttachedWeight;
    public bool IsDisplayedCurrentZeroForSupport=>IsInputValueLiftMode&&Math.Abs(CurrentElectricCurrentA)<=.01f;
    public bool IsLifMagCurrentOn(int i)=>true;
    public float GetLifMagDisplayAccumValue(int i)=>0;
}
public class CraneWorkLoadPlanManager
{
    public bool HasWeight=true,HasRemainingWeight=true;public float Weight=1000,RemainingWeight;
    public int PickupCount=-1,RemainingCount=-1;
    public bool TryGetPickupTargetWeightKg(out float w) {w=Weight;return HasWeight;}
    public bool TryGetTargetRemainingWeightKg(out float w) {w=RemainingWeight;return HasRemainingWeight;}
    public bool TryGetDisplayTargetWeightKg(CraneStatusManager.WorkPhase phase,float held,out float w)
    {return phase==CraneStatusManager.WorkPhase.Move1||phase==CraneStatusManager.WorkPhase.LiftUp
        ? TryGetPickupTargetWeightKg(out w) : TryGetTargetRemainingWeightKg(out w);}
    public bool IsPickupBoardCountSatisfied(int count)=>PickupCount<0||count==PickupCount;
    public bool IsPlacementBoardCountSatisfied(int count)=>RemainingCount<0||count==RemainingCount;
}
public class CraneUnit
{
    public bool IsDownwardMovementBlockedByTouchdown;
    public float LocalY, LoweringDistance=1.2f; public bool DistanceKnown=true;
    public int RaisingQueries,LoweringQueries;
    public bool TryGetMainLifMagLocalY(out float y) { y=LocalY;return true; }
    public bool TryGetSupportRaisingDistance(float target,out float d) { RaisingQueries++;d=Math.Max(0,target-LocalY);return DistanceKnown; }
    public bool TryGetSupportLoweringDistance(out float d) { LoweringQueries++;d=LoweringDistance;return DistanceKnown; }
}
public partial class CraneWorkPhaseTracker
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
    public CraneInstance CurrentCrane=>CurrentCraneInstance;
}
public static class ExperimentPauseManager { public static bool IsPaused;public static double ActiveRealtime; }
public class TaskSwitchExperimentManager : UnityEngine.MonoBehaviour
{
    public TaskSwitchExperimentState CurrentState=TaskSwitchExperimentState.WaitingForConfirmation;
    public TaskSwitchCraneCondition TargetCondition=new TaskSwitchCraneCondition(),SourceCondition=new TaskSwitchCraneCondition();
    public bool HasPreview=true;public float GoalX=2,GoalZ=-2;
    public bool HasPickupWeightPreview=true;public float PickupWeight=1000;
    public bool TryGetCognitiveSupportPickupTargetWeightKg(int index,out float weight) { weight=PickupWeight;return HasPickupWeightPreview; }
    public event Action<TaskSwitchEventData> ExperimentEventOccurred;
    public List<string> Recorded=new List<string>();
    public bool TryGetCognitiveSupportTargetPosition(int index,CraneStatusManager.WorkPhase p,out float x,out float z)
    { x=GoalX;z=GoalZ;return HasPreview; }
    public void Raise(string name) { ExperimentEventOccurred?.Invoke(new TaskSwitchEventData { eventName=name }); }
    public void RecordCognitiveSupportEvent(string action,string detail) { Recorded.Add(action+":"+detail);Raise("CognitiveSupport"+action); }
}
