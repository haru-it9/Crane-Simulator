// Standalone bindings: the complete production tracker/profile and production
// movement dispatch/input methods run against deterministic transforms and input.
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public class Object
    {
        public static List<object> Scene=new List<object>();
        public static T FindObjectOfType<T>(bool inactive=false) where T:class { return Scene.Find(x=>x is T) as T; }
        public static void Destroy(Object o) {}
    }
    public class MonoBehaviour:Object
    {
        public string name="test";public bool isActiveAndEnabled=true;
        public T GetComponent<T>() where T:class { return null; }
        public T GetComponentInParent<T>() where T:class { return null; }
        public T GetComponentInChildren<T>(bool inactive=false) where T:class { return null; }
    }
    public class ScriptableObject:Object {}
    public class GameObject:Object
    {
        public Transform transform=new Transform();public bool activeSelf=true;
        public T GetComponent<T>() where T:class { return null; }
        public void SetActive(bool active) { activeSelf=active; }
    }
    public class Transform { public Vector3 position; }
    public class Rigidbody { public Vector3 velocity,angularVelocity; }
    public struct Vector2
    {
        public float x,y;public Vector2(float x,float y) { this.x=x;this.y=y; }
        public static Vector2 zero=>new Vector2();public float magnitude=>(float)Math.Sqrt(x*x+y*y);
        public static float Distance(Vector2 a,Vector2 b)=>new Vector2(a.x-b.x,a.y-b.y).magnitude;
    }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 zero=>new Vector3();public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    }
    public static class Mathf
    {
        public static float Abs(float v)=>Math.Abs(v);public static float Max(float a,float b)=>Math.Max(a,b);
        public static float Clamp(float v,float a,float b)=>Math.Max(a,Math.Min(b,v));
    }
    public static class Time { public static float deltaTime=.05f,unscaledDeltaTime=.05f;public static int frameCount; }
    public enum KeyCode { D,A,W,S,L,J }
    public static class Input
    {
        public static Dictionary<string,float> axes=new Dictionary<string,float>();public static HashSet<KeyCode> keys=new HashSet<KeyCode>();
        public static float GetAxis(string key) { float v;return axes.TryGetValue(key,out v)?v:0; }
        public static bool GetKey(KeyCode key)=>keys.Contains(key);
    }
    public static class Debug { public static void Log(object s,object c=null) {} public static void LogWarning(object s,object c=null) {} public static void LogError(object s,object c=null) { throw new Exception(s.ToString()); } }
    public static class JsonUtility { public static string ToJson(object v)=>"{}"; }
    public class SerializeField:Attribute {}
    public class Header:Attribute { public Header(string v) {} }
    public class Tooltip:Attribute { public Tooltip(string v) {} }
    public class Min:Attribute { public Min(float v) {} }
    public class DisallowMultipleComponent:Attribute {}
    public class ContextMenu:Attribute { public ContextMenu(string v) {} }
    public class CreateAssetMenu:Attribute { public string fileName,menuName; }
}
public static class ExperimentPauseManager { public static bool IsPaused; }
public class CraneStatusManager { public enum WorkPhase { Move1,LiftUp,Move2,Place,PlaceToTrack } }
public class CraneInstance
{
    public Transform InformationTarget=new Transform();public LifMagSystem LifMagSystem=new LifMagSystem();
    public CraneUnit CraneUnit;public string DisplayName="test";public int CraneId=1;
}
public class CraneUnit
{
    public Transform Target;public Vector3 LastMovement;
    public event Action<CraneUnit,CraneWorkTouchdownKind,float> TouchdownDetected;
    public bool TryGetMainLifMagLocalY(out float y) { y=0;return true; }
    public void MoveMainLifMagX(float v) { LastMovement.x=v;var p=Target.position;p.x+=v*.001f;Target.position=p; }
    public void MoveMainLifMagY(float v) { LastMovement.y=v; }
    public void MoveMainCraneZ(float v) { LastMovement.z=v;var p=Target.position;p.z+=v*.001f;Target.position=p; }
    public void MoveLifMagX(int i,float v) {}
}
public class LifMagSystem
{
    public List<GameObject> AttachedBoards=new List<GameObject>();public bool HasAttachedBoard=>AttachedBoards.Count>0;
    public GameObject LastAttachedBoard=>HasAttachedBoard?AttachedBoards[0]:null;
    public bool IsAttachedBoard(GameObject board)=>AttachedBoards.Contains(board);
    public float GetAttachedTotalWeightKgForDisplay()=>AttachedBoards.Count*100;
}
public class CraneWorkTargetManager { public bool TryGetTarget(out float x,out float z) { x=z=0;return true; } }
public class CraneSchematicDisplay { public bool TryGetCurrentTargetPosition(out float x,out float z) { x=z=0;return true; } }
public class CraneWorkLoadPlanManager
{
    public bool TryGetPickupTargetWeightKg(out float v) { v=100;return true; }
    public bool TryGetTargetRemainingWeightKg(out float v) { v=0;return true; }
    public bool IsPickupBoardCountSatisfied(int n)=>true;public bool IsPlacementBoardCountSatisfied(int n)=>true;
    public void SetPickupTargetWeightKg(float v) {} public void SetPlannedReleaseWeightKg(float v) {} public void SetTargetRemainingWeightKg(float v) {}
    public bool PreparePlacementPlan(float weight) { return true; }
}
public partial class CraneOperationManager
{
    public enum InputMode { Keyboard,Joystick }
    public enum SpeedControlMode { ButtonAndKeyboard,JoystickStep }
    private InputMode inputMode=InputMode.Joystick;private SpeedControlMode speedControlMode=SpeedControlMode.JoystickStep;
    private bool debugMode,taskSwitchExperimentMode=true;private float deadZone=.05f;
    private string debugJoyStick2Horizontal="debugZ",debugJoyStick2Vertical="debugY",debugJoyStick3Vertical="debugX";
    private string joyStick2Horizontal="z",joyStick2Vertical="y",joyStick3Vertical="x",joyStick3MiniVertical="spread";
    private CraneWorkPhaseTracker selectedWorkPhaseTracker;
    public CraneInstance CurrentCraneInstance;public CraneUnit CurrentCrane=>CurrentCraneInstance.CraneUnit;
    public Vector3 LastAcceptedMovement;public float LastAcceptedSpread;public int LastAcceptedInputFrame;
    public event Action<CraneOperationManager,Vector3,float> MovementInputAccepted;
}
