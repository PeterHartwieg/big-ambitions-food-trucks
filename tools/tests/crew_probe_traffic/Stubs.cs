#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
#pragma warning disable CS0649,CS0414,CS0108
namespace UnityEngine {
 public class Object { public string name=""; public int GetInstanceID()=>GetHashCode(); public static object? Scene; public static T? FindObjectOfType<T>() where T:class => Scene as T; public static void Destroy(Object o) {} public static void DontDestroyOnLoad(Object o) {} }
 public class GameObject:Object { static int next; public int Id=++next; public int layer=12; public bool activeInHierarchy=true; public Transform transform; public List<Component> components=new(); public GameObject(string name=""){this.name=name;transform=new Transform(this);} public T AddComponent<T>() where T:Component,new(){var t=new T();t.gameObject=this;components.Add(t);return t;} }
 public class Component:Object { public void GetComponents<T>(List<T> list) where T:Component {list.Clear();foreach(var c in gameObject.components)if(c is T t)list.Add(t);} public GameObject gameObject=null!; public Transform transform=>gameObject.transform; public int GetInstanceID()=>gameObject.Id;public T? GetComponent<T>() where T:Component {foreach(var c in gameObject.components)if(c is T t)return t;return null;}
 public T? GetComponentInParent<T>(bool all=false) where T:Component {for(var p=transform;p!=null;p=p.parent)foreach(var c in p.gameObject.components)if(c is T t)return t;return null;} public T? GetComponentInChildren<T>(bool all) where T:Component=>gameObject.components.OfType<T>().FirstOrDefault(); public T[] GetComponentsInChildren<T>(bool all) where T:Component=>gameObject.components.OfType<T>().ToArray(); }
 public class MonoBehaviour:Component {}
 public class ScriptableObject:Object {}
 public class Transform:Component { public Transform? parent; public int childCount=>0; public Transform GetChild(int i)=>throw new Exception(); public Vector3 localPosition {get=>position;set=>position=value;} public Vector3 localScale {get=>lossyScale;set=>lossyScale=value;} public Quaternion localRotation {get=>rotation;set=>rotation=value;} public Vector3 position,lossyScale=Vector3.one; public Vector3 forward=>rotation.Rotate(Vector3.forward); public Quaternion rotation; public Transform(GameObject go){gameObject=go;} public Vector3 TransformPoint(Vector3 v)=>rotation.Rotate(v)+position; public Vector3 InverseTransformPoint(Vector3 v)=>rotation.InverseRotate(v-position); }
 public struct Vector3 { public float x,y,z; public Vector3(float a,float b,float c){x=a;y=b;z=c;} public float this[int i]{get=>i==0?x:i==1?y:z;set{if(i==0)x=value;else if(i==1)y=value;else z=value;}} public static Vector3 zero=>new(0,0,0); public static Vector3 one=>new(1,1,1); public static Vector3 up=>new(0,1,0);public static Vector3 down=>new(0,-1,0);public static Vector3 forward=>new(0,0,1);public float sqrMagnitude=>x*x+y*y+z*z;public float magnitude=>MathF.Sqrt(sqrMagnitude);public Vector3 normalized=>magnitude>0?this/magnitude:zero;public static Vector3 operator +(Vector3 a,Vector3 b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator -(Vector3 a,Vector3 b)=>new(a.x-b.x,a.y-b.y,a.z-b.z);public static Vector3 operator -(Vector3 a)=>zero-a;public static Vector3 operator *(Vector3 a,float b)=>new(a.x*b,a.y*b,a.z*b);public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);public override bool Equals(object? o)=>o is Vector3 v&&v==this;public override int GetHashCode()=>0;public static float Dot(Vector3 a,Vector3 b)=>a.x*b.x+a.y*b.y+a.z*b.z;public static Vector3 Scale(Vector3 a,Vector3 b)=>new(a.x*b.x,a.y*b.y,a.z*b.z);public static Vector3 Min(Vector3 a,Vector3 b)=>new(MathF.Min(a.x,b.x),MathF.Min(a.y,b.y),MathF.Min(a.z,b.z));public static Vector3 Max(Vector3 a,Vector3 b)=>new(MathF.Max(a.x,b.x),MathF.Max(a.y,b.y),MathF.Max(a.z,b.z)); }
 public struct Quaternion {public static Vector3 operator *(Quaternion q,Vector3 v)=>q.Rotate(v);public float angle,pitch; public static Quaternion identity=>new();public static Quaternion LookRotation(Vector3 v)=>new(){angle=MathF.Atan2(v.x,v.z)}; public Vector3 Rotate(Vector3 v){var q=new Vector3(v.x,v.y*MathF.Cos(pitch)-v.z*MathF.Sin(pitch),v.y*MathF.Sin(pitch)+v.z*MathF.Cos(pitch));return new(q.x*MathF.Cos(angle)+q.z*MathF.Sin(angle),q.y,-q.x*MathF.Sin(angle)+q.z*MathF.Cos(angle));} public Vector3 InverseRotate(Vector3 v){var q=new Vector3(v.x*MathF.Cos(angle)-v.z*MathF.Sin(angle),v.y,v.x*MathF.Sin(angle)+v.z*MathF.Cos(angle));return new(q.x,q.y*MathF.Cos(pitch)+q.z*MathF.Sin(pitch),-q.y*MathF.Sin(pitch)+q.z*MathF.Cos(pitch));} }
 public struct Bounds {public Vector3 center,size;public Vector3 extents=>size/2;public Vector3 max=>center+extents;public Vector3 min=>center-extents;public Bounds(Vector3 c,Vector3 s){center=c;size=s;}}
 public class Rigidbody:Component {public bool detectCollisions=true;public LayerMask includeLayers,excludeLayers;}
 public class Collider:Component {public LayerMask includeLayers,excludeLayers;public int layerOverridePriority;public bool enabled=true,isTrigger;public Rigidbody? attachedRigidbody; public virtual Bounds bounds=>new(transform.position,Vector3.one);public bool RaycastGround; public bool Raycast(Ray r,out RaycastHit h,float d){h=new RaycastHit { normal=Vector3.up, point=new Vector3(0,bounds.max.y,0) };return RaycastGround;}}
 public class BoxCollider:Collider {public Vector3 center,size=Vector3.one;public override Bounds bounds=>new(transform.TransformPoint(center),size);}
 public class Mesh {public Bounds bounds;}
 public class MeshCollider:Collider {public Mesh? sharedMesh;}
 public class SphereCollider:Collider {public Vector3 center;public float radius;}
 public class CapsuleCollider:Collider {public Vector3 center;public int direction;public float radius,height;}
 public struct LayerMask {public int value;public static implicit operator LayerMask(int x)=>new(){value=x};}
 public static class Resources {public static object? Setup;public static T? Load<T>(string x) where T:class=>Setup as T;}
 public struct Ray {public Ray(Vector3 p,Vector3 d){}}
 public struct RaycastHit {public Vector3 normal,point;}
 public enum QueryTriggerInteraction {Ignore}
 public static class Physics {public static Vector3 LastCenter,LastHalf;public static Quaternion LastRotation;public static bool PairIgnore;public static bool GetIgnoreCollision(Collider a,Collider b)=>PairIgnore;public static int Count;public static Collider? Obstacle;public static bool GetIgnoreLayerCollision(int a,int b)=>false;public static int OverlapBoxNonAlloc(Vector3 a,Vector3 b,Collider[] into,Quaternion q,int mask,QueryTriggerInteraction mode){LastCenter=a;LastHalf=b;LastRotation=q;if(Count==128)return 128;if(Obstacle!=null){var bounds=Obstacle.bounds;var d=bounds.center-a;var e=bounds.extents;var c=MathF.Cos(q.angle);var s=MathF.Sin(q.angle);var ac=MathF.Abs(c);var az=MathF.Abs(s);if(MathF.Abs(d.y)<=e.y+b.y&&MathF.Abs(d.x)<=e.x+b.x*ac+b.z*az&&MathF.Abs(d.z)<=e.z+b.x*az+b.z*ac&&MathF.Abs(d.x*c-d.z*s)<=b.x+e.x*ac+e.z*az&&MathF.Abs(d.x*s+d.z*c)<=b.z+e.x*az+e.z*ac){into[0]=Obstacle;return 1;}}return 0;} }
 public static class Mathf {public static float Cos(float x)=>MathF.Cos(x);public static float Sin(float x)=>MathF.Sin(x);public static float Abs(float x)=>MathF.Abs(x);public static float Max(float a,float b)=>MathF.Max(a,b);public static int Max(int a,int b)=>Math.Max(a,b);public static float Min(float a,float b)=>MathF.Min(a,b);public static float Sqrt(float f)=>MathF.Sqrt(f);public static int CeilToInt(float f)=>(int)MathF.Ceiling(f);public static float Lerp(float a,float b,float t)=>LerpUnclamped(a,b,Math.Clamp(t,0,1));public static float LerpUnclamped(float a,float b,float t)=>a+(b-a)*t;}
}
public class CarController:UnityEngine.MonoBehaviour {public UnityEngine.Collider vehicleCollider=null!;}
public class VehicleParkingHelper:UnityEngine.MonoBehaviour { public UnityEngine.Collider? parallelParkingCollider; }
namespace GleyUrbanAssets { public interface IIntersection {void VehicleEnter(int id);void VehicleLeft(int id);} public class CurrentSceneData:UnityEngine.MonoBehaviour {public GleyTrafficSystem.Waypoint[] allWaypoints=null!;public GleyTrafficSystem.TrafficLightsIntersection[] allLightsIntersections=null!;public GleyTrafficSystem.PriorityIntersection[] allPriorityIntersections=Array.Empty<GleyTrafficSystem.PriorityIntersection>();} }
namespace GleyTrafficSystem {
 using UnityEngine;using GleyUrbanAssets;
 public class Waypoint {public int listIndex; public Vector3 position;public bool temporaryDisabled,giveWay,enter,exit,stop; public List<int> neighbors=new(); public string name="Waypoint_";private IIntersection? associatedIntersection;public void Junction(IIntersection x,bool entry){associatedIntersection=x;enter=entry;exit=!entry;name="junction";}}
 public class GenericIntersection:IIntersection {protected List<int> carsInIntersection=new();public List<int> exitWaypoints=new();public void VehicleEnter(int id)=>carsInIntersection.Add(id);public void VehicleLeft(int id)=>carsInIntersection.Remove(id);public List<int> Members=>carsInIntersection;public void Reset()=>carsInIntersection=new();}
 public class TrafficLightsIntersection:GenericIntersection {public List<IntersectionStopWaypointsIndex> stopWaypoints=new();}
 public class PriorityIntersection:GenericIntersection {}
 public class IntersectionStopWaypointsIndex {public List<int> roadWaypoints=new();}
 public class VehicleComponent:MonoBehaviour {public Rigidbody rb=null!;public Transform frontTrigger=null!;private LayerMask obstacleLayers=1<<12,playerLayers=1<<12,buildingLayers=0;public void SetMasks(int obstacles,int players,int buildings){obstacleLayers=obstacles;playerLayers=players;buildingLayers=buildings;}}
 public class TrafficVehicles {public List<VehicleComponent> List=new();public List<VehicleComponent> GetVehicleList()=>List;}
 public class TrafficManager:MonoBehaviour {public static bool HasInstance=true,IsInitialized=true;public static TrafficManager Instance=new();private TrafficVehicles trafficVehicles=new();public TrafficVehicles Pool=>trafficVehicles;public static bool IsNotIntersection(Waypoint w)=>w.name.Contains("Waypoint_");}
 public class LayerSetup:ScriptableObject {public LayerMask trafficLayers=1<<9,obstaclesLayers=1<<12,playerLayers=1<<12,buildingsLayers=0;}
}

namespace FoodTrucks {
 internal sealed class CrewProbeBody {
  internal float? SelectedBandMin=>TrafficCertified?BandMin:null;internal float? SelectedBandMax=>TrafficCertified?BandMax:null;
  internal float? AttemptedBandMin;internal string? FailureStage;
  internal void ClearFailure(){}internal void RecordCoverageFailure(float a,float b){}
  internal bool TrafficCertified; internal float RootY, BandMin, BandMax=.2f,WorldBandMin=.1f,WorldBandMax=.2f;
  internal int BandCandidates,FailingShell,FailingCorner,FailingAuthority;internal long CookedQueries;internal UnityEngine.Vector3 FailingPoint;
  internal CrewProbeBody() {} internal CrewProbeBody(CarController c) {}
  internal string? SelectBand(float a,float b){TrafficCertified=true;return null;} internal void UpdateWorldBand() {} internal bool CoversInterval(float a,float b)=>a<=.1f&&b>=.2f;
  internal UnityEngine.Vector3 Min,Max; internal UnityEngine.Collider[] Own=Array.Empty<UnityEngine.Collider>();
  internal UnityEngine.Collider? ParkingGap; internal UnityEngine.Collider? Wheel;
  internal string? Failure;internal float Ground;internal bool GroundKnown=true;
  internal float VerticalTiltPadding(float rearZ)=>.251f;
  internal bool IsWheel(UnityEngine.Collider c)=>c==Wheel;
  internal bool CoversTrigger(UnityEngine.Collider c)=>c is UnityEngine.BoxCollider && c.bounds.min.y<=.1f && c.bounds.max.y>=.2f;
  internal string? Validate()=>Failure;
  internal bool TryGround(out float y){y=Ground;return GroundKnown;}
 }
}
