using System;
using System.Collections.Generic;
using UnityEngine;

// Minimal physics boundary: query results are controlled while production filtering remains real.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float x,float y,float z) { this.x=x;this.y=y;this.z=z; }
        public static Vector3 down => new Vector3(0,-1,0);
    }
    public struct Quaternion { }
    public struct Bounds { public Vector3 center,min,extents; }
    public class Transform
    {
        public Vector3 position,localPosition,lossyScale=new Vector3(1,1,1);
        public Quaternion rotation; public Transform parent;public string tag;
        public bool CompareTag(string t) => tag==t;
        public bool IsChildOf(Transform t) { for(var p=this;p!=null;p=p.parent)if(p==t)return true;return false; }
    }
    public class GameObject
    {
        public Transform transform=new Transform();public List<Collider> colliders=new List<Collider>();
        public T[] GetComponentsInChildren<T>(bool inactive) where T:class
        { return colliders.ConvertAll(c=>c as T).ToArray(); }
    }
    public class Collider { public bool enabled=true,isTrigger;public Bounds bounds;public Transform transform=new Transform(); }
    public struct RaycastHit { public Collider collider;public float distance; }
    public enum QueryTriggerInteraction { Ignore }
    public static class Mathf
    {
        public static float Abs(float v)=>Math.Abs(v);
        public static float Max(float a,float b)=>Math.Max(a,b);
    }
    public static class Physics
    {
        public static RaycastHit[] Hits=new RaycastHit[0];public static int Calls,Next;public static float MaxDistance;
        public static void Reset(params RaycastHit[] hits) { Hits=hits;Calls=Next=0; }
        public static RaycastHit[] BoxCastAll(Vector3 a,Vector3 b,Vector3 d,Quaternion r,float max,int layer,QueryTriggerInteraction q)
        { Calls++;MaxDistance=max;return Hits; }
        public static bool BoxCast(Vector3 a,Vector3 b,Vector3 d,out RaycastHit hit,Quaternion r,float max,int layer,QueryTriggerInteraction q)
        { Calls++;MaxDistance=max;hit=Next<Hits.Length?Hits[Next++]:new RaycastHit();return hit.collider!=null; }
    }
}
public class LifMagSystem { public bool HasAttachedBoard;public List<GameObject> AttachedBoards=new List<GameObject>(); }
public partial class CraneUnit
{
    public class LifMagSetting { public Transform downCheckOrigin; }
    public Transform mainLifMag;public float minMainY=-5.31f,skinWidth=.01f;
    public LifMagSystem lifMagSystem;public LifMagSetting[] lifMags;
    public bool IsDownwardMovementBlockedByTouchdown;
    public int boardLayer;public Vector3 downCheckHalfExtents;
}
public static class DistanceProbeTests
{
    static int checks;
    static void Check(bool ok,string message) { checks++;if(!ok)throw new Exception(message); }
    static RaycastHit Hit(float distance,string tag=null,Transform parent=null)
    { return new RaycastHit { distance=distance,collider=new Collider { transform=new Transform { tag=tag,parent=parent } } }; }
    public static void Main()
    {
        float d;
        var unit=new CraneUnit();
        Check(!unit.TryGetSupportRaisingDistance(-1,out d)&&!unit.TryGetSupportLoweringDistance(out d),"Missing main magnet is unknown");
        var parent=new Transform { lossyScale=new Vector3(1,2,1) };
        unit.mainLifMag=new Transform { localPosition=new Vector3(0,-3,0),parent=parent };
        Check(unit.TryGetSupportRaisingDistance(-1,out d)&&d==4,"Raising converts local height to world distance");
        Check(unit.TryGetSupportRaisingDistance(-4,out d)&&d==0,"Raising never returns negative distance");
        Check(!unit.TryGetSupportRaisingDistance(float.NaN,out d),"Invalid target is unknown");
        unit.lifMags=new[]{new CraneUnit.LifMagSetting { downCheckOrigin=new Transform() },null,
            new CraneUnit.LifMagSetting { downCheckOrigin=new Transform() }};
        Physics.Reset(Hit(.9f),Hit(.4f));
        Check(unit.TryGetSupportLoweringDistance(out d)&&d==.4f&&Physics.Calls==2,"Pickup uses nearest magnet surface");
        Check(Math.Abs(Physics.MaxDistance-4.62f)<.0001f,"Query range follows remaining physical descent limit");
        Check(unit.mainLifMag.localPosition.y==-3&&!unit.IsDownwardMovementBlockedByTouchdown,"Probe does not move or latch touchdown");
        Physics.Reset();Check(!unit.TryGetSupportLoweringDistance(out d),"No surface is unknown");
        var board=new GameObject();board.colliders.Add(new Collider { bounds=new Bounds { min=new Vector3(0,1,0) } });
        unit.lifMagSystem=new LifMagSystem { HasAttachedBoard=true };unit.lifMagSystem.AttachedBoards.Add(board);
        var stageParent=new Transform { tag="BoardStage" };
        Physics.Reset(Hit(.01f,"Board",board.transform),Hit(.1f,"Unrelated"),Hit(1.3f,"Board"),Hit(.7f,null,stageParent));
        Check(unit.TryGetSupportLoweringDistance(out d)&&d==.7f,"Loaded probe filters held boards and accepts nearest tagged parent");
        Check(unit.lifMagSystem.AttachedBoards.Count==1&&unit.lifMagSystem.HasAttachedBoard,"Loaded query leaves attachments intact");
        Physics.Reset(Hit(.1f,"Unrelated"));Check(!unit.TryGetSupportLoweringDistance(out d),"Unrelated loaded collision is not a goal");
        unit.IsDownwardMovementBlockedByTouchdown=true;Physics.Reset();
        Check(unit.TryGetSupportLoweringDistance(out d)&&d==0&&Physics.Calls==0,"Touchdown returns zero without more queries");
        unit.IsDownwardMovementBlockedByTouchdown=false;unit.mainLifMag.localPosition=new Vector3(0,-5.31f,0);
        Check(!unit.TryGetSupportLoweringDistance(out d),"No remaining descent range is not a fabricated surface");
        Console.WriteLine("Support distance probe checks passed: "+checks);
    }
}
