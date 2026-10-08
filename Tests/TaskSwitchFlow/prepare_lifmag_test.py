"""Compile the full production LifMagSystem with a small Unity physics test surface."""
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
output = Path(sys.argv[1])
stubs = (root / 'Tests/TaskSwitchFlow/UnityFlowStubs.cs').read_text()
stubs = stubs[:stubs.index('public class CraneStatusManager')]
stubs = stubs.replace('public class GameObject : Object {', 'public class GameObject : Object { public bool CompareTag(string tag) => tag=="Board"; public T[] GetComponentsInChildren<T>() where T:class {var c=GetComponent<T>();return c==null?new T[0]:new[]{c};}')
stubs = stubs.replace('public class Transform { public Vector3 position; }', '''public class Transform {
    public Vector3 localPosition,localScale=Vector3.one; public Quaternion rotation,localRotation; public Transform parent;
    public Vector3 lossyScale=>parent==null?localScale:Scale(parent.lossyScale,localScale);
    public Vector3 position {get=>parent==null?localPosition:parent.position+Scale(parent.lossyScale,localPosition);set {localPosition=parent==null?value:Divide(value-parent.position,parent.lossyScale);}}
    public static Vector3 Scale(Vector3 a,Vector3 b)=>new Vector3(a.x*b.x,a.y*b.y,a.z*b.z);
    static Vector3 Divide(Vector3 a,Vector3 b)=>new Vector3(a.x/b.x,a.y/b.y,a.z/b.z);
    public void SetParent(Transform p,bool keepWorld) {var pos=position;var size=lossyScale;parent=p;if(keepWorld) {position=pos;localScale=p==null?size:Divide(size,p.lossyScale);}}
}''')
stubs = stubs.replace('public static Vector3 zero', 'public static Vector3 one => new Vector3(1,1,1); public static Vector3 operator *(Vector3 a,float b) => new Vector3(a.x*b,a.y*b,a.z*b); public static Vector3 zero')
stubs = stubs.replace('public static Vector3 operator -', 'public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z); public static Vector3 operator -')
stubs = stubs.replace('public static class Mathf {', 'public static class Mathf { public static float Clamp01(float x)=>Clamp(x,0,1);')
stubs = stubs.replace('public static class Random {', 'public static class Random { public static float value=>1;')
stubs = stubs.replace('public static bool GetKey(KeyCode key)', 'public static bool GetKeyDown(KeyCode key)=>GetKey(key); public static bool GetKey(KeyCode key)')
stubs = stubs.replace('None=0,Minus=', 'E=101,R=114,None=0,Minus=')
stubs = stubs.replace('public static Color red', 'public static Color green=>new Color(0,1,0); public static Color red')
output.joinpath('LifMagUnityStubs.cs').write_text(stubs + '''
namespace UnityEngine {
    public struct Quaternion { public static Quaternion identity=>new Quaternion(); public static Quaternion Euler(Vector3 e)=>identity; }
    public class Rigidbody { public bool isKinematic,useGravity=true; public Vector3 velocity,angularVelocity; }
    public struct Bounds {
        public Vector3 center,size; public Bounds(Vector3 c,Vector3 s) {center=c;size=s;}
        public Vector3 extents=>size*.5f; public Vector3 min=>center-extents; public Vector3 max=>center+extents;
        public void Encapsulate(Bounds b) {var lo=new Vector3(Math.Min(min.x,b.min.x),Math.Min(min.y,b.min.y),Math.Min(min.z,b.min.z));var hi=new Vector3(Math.Max(max.x,b.max.x),Math.Max(max.y,b.max.y),Math.Max(max.z,b.max.z));center=(lo+hi)*.5f;size=hi-lo;}
    }
    public class Collider {
        public GameObject gameObject; public bool enabled=true,isTrigger; public Vector3 LocalCenter,LocalSize=Vector3.one;
        public Bounds bounds=>new Bounds(gameObject.transform.position+Transform.Scale(LocalCenter,gameObject.transform.lossyScale),Transform.Scale(LocalSize,gameObject.transform.lossyScale));
    }
    public class Renderer { public Bounds bounds; public bool enabled=true; }
    public static class Physics { public static void SyncTransforms() {} public static Collider[] OverlapBox(Vector3 p,Vector3 size,Quaternion q)=>new Collider[0]; }
    public struct Matrix4x4 { public static Matrix4x4 identity=>new Matrix4x4(); public static Matrix4x4 TRS(Vector3 p,Quaternion q,Vector3 s)=>identity; }
    public static class Gizmos { public static Matrix4x4 matrix; public static Color color; public static void DrawWireCube(Vector3 p,Vector3 s) {} public static void DrawSphere(Vector3 p,float r) {} }
}
public class BoardInfo { public float Weight,SizeX=1,SizeY=.1f,SizeZ=1; public bool UsesExplicitWeight=true; }
public class MagnetSensor:UnityEngine.MonoBehaviour { public List<UnityEngine.GameObject> TouchingBoards=new List<UnityEngine.GameObject>(); }
public class HoldBoardSensor { public UnityEngine.GameObject Owner; public UnityEngine.GameObject TouchingBoard; public void SetOwnerBoard(UnityEngine.GameObject b) {Owner=b;} public void ClearOwnerBoard() {Owner=null;} }
public static class ExperimentPauseManager {public static bool IsPaused;}
public static class SimulatorStartManager {public static bool IsOperationEnabled=true;}
public class CraneStatusManager {public enum WorkPhase {Move1,LiftUp,Move2,Place,PlaceToTrack} public enum ErrorType {None,ErrorA,ErrorB,ErrorC}}
public class CraneWorkPhaseTracker {public bool IsMonitoring,IsPlacementTouchdownConfirmed; public CraneStatusManager.WorkPhase CurrentMajorPhase;}
public class CraneOperationManager {public bool IsTaskSwitchExperimentMode=true,IsOperationInputLocked; public CraneUnit CurrentCrane;}
''')

# Use the unchanged production parenting and Rigidbody methods, not fake attachment lists.
source = (root / 'Assets/Scripts/CraneUnit.cs').read_text()
start = source.index('    public void SetInterventionBoardAttached(')
end = source.index('    private void OnDrawGizmos()', start)
output.joinpath('CraneAttachmentProduction.cs').write_text('''using UnityEngine;
public class CraneUnit {
    public Transform mainLifMag=new Transform(); public LifMagSystem lifMagSystem;
    public LifMagSystem LifMagSystem=>lifMagSystem;
    public void ConfigureZRangeForCraneIndex(int i) {} public void SetInterventionPose(float z,float x,float y) {}
    public bool TryGetMainLifMagLocalY(out float y) {y=mainLifMag.localPosition.y;return true;}
''' + source[start:end] + '}\n')

# Execute the actual scenario setup branch with generated-object counters.
source = (root / 'Assets/Scripts/CraneInterventionScenarioManager.cs').read_text()
start = source.index('    public void SetupInterventionState(')
end = source.index('    public void ClearCurrentScenarioObjects()', start)
output.joinpath('ScenarioSetupProduction.cs').write_text('''using System.Collections.Generic; using UnityEngine;
public class CraneInstance {public CraneUnit CraneUnit;}
public class CraneInterventionScenarioManager {
    class InterventionScenarioState {public bool isInitialized; public CraneUnit craneUnit; public GameObject plate,human,trailer; public Vector3 attachedPlateTargetSize;}
    struct CranePose {public float mainCraneLocalZ,mainLifMagLocalX,mainLifMagLocalY;}
    Dictionary<int,InterventionScenarioState> scenarioStates=new Dictionary<int,InterventionScenarioState>();
    int currentScenarioCraneIndex; CraneUnit currentCraneUnit; GameObject currentPlate,currentHuman,currentTrailer; Vector3 currentAttachedPlateTargetSize;
    public int SyntheticPlateCount;
    bool TryResolveCraneInstance(CraneUnit u,int index,out CraneInstance c,out int resolved) {c=new CraneInstance {CraneUnit=u};resolved=index;return true;}
    CranePose GetRandomCranePose(CraneStatusManager.ErrorType e)=>new CranePose();
    void SetupPlate(CraneUnit u,CraneStatusManager.WorkPhase p,CraneStatusManager.ErrorType e) {SyntheticPlateCount++;currentPlate=new GameObject("synthetic");}
    void SetupHuman(CraneStatusManager.ErrorType e,int i) {} void SetupTrailer(CraneStatusManager.ErrorType e,int i) {}
''' + source[start:end] + '}\n')
