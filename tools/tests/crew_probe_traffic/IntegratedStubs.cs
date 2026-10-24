#nullable enable
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public partial class Object
    {
        public static object? Scene;
        public static T? FindObjectOfType<T>() where T:class => Scene as T;
        public static void Destroy(Object o) {} public static void DontDestroyOnLoad(Object o) {}
    }
    public partial class GameObject { public T AddComponent<T>() where T:Component,new() => Add<T>(); }
    public partial class Component
    {
        public void GetComponents<T>(List<T> list) where T:Component { list.Clear(); foreach(var c in gameObject.Components) if(c is T t) list.Add(t); }
        public T[] GetComponentsInChildren<T>(bool all) where T:Component { var list=new List<T>(); GetComponentsInChildren(all,list);return list.ToArray(); }
        public T? GetComponentInChildren<T>(bool all) where T:Component {var a=GetComponentsInChildren<T>(all);return a.Length==0?null:a[0];}
    }
    public class MonoBehaviour:Component {} public class ScriptableObject:Object {}
    public partial class Transform { public int childCount=>Children.Count; public Transform GetChild(int i)=>Children[i]; public Vector3 forward=>rotation*new Vector3(0,0,1); }
    public partial struct Vector3
    {
        public static Vector3 zero=>default; public static Vector3 up=>new(0,1,0);public static Vector3 down=>new(0,-1,0);public static Vector3 forward=>new(0,0,1);
        public static Vector3 operator -(Vector3 a)=>default(Vector3)-a;
        public static Vector3 operator /(Vector3 a,float b)=>a*(1/b);
        public static bool operator ==(Vector3 a,Vector3 b)=>a.x==b.x&&a.y==b.y&&a.z==b.z;
        public static bool operator !=(Vector3 a,Vector3 b)=>!(a==b);
        public override bool Equals(object? o)=>o is Vector3 v&&this==v;public override int GetHashCode()=>0;
    }
    public partial struct Quaternion { public static Quaternion LookRotation(Vector3 f)=>Euler(0,MathF.Atan2(f.x,f.z)*180/MathF.PI,0); }
    public partial class Collider {public bool Raycast(Ray r,out RaycastHit h,float d){h=default;return false;}}
    public struct Ray {public Ray(Vector3 p,Vector3 d){}} public struct RaycastHit {public Vector3 normal,point;}
    public enum QueryTriggerInteraction {Ignore}
    public static class Physics
    {
        public static bool PairIgnore;
        public static bool GetIgnoreCollision(Collider a,Collider b)=>PairIgnore;
        public static bool GetIgnoreLayerCollision(int a,int b)=>false;
        public static int OverlapBoxNonAlloc(Vector3 a,Vector3 b,Collider[] into,Quaternion q,int mask,QueryTriggerInteraction mode)=>0;
    }
    public static class Resources {public static object? Setup; public static T? Load<T>(string s) where T:class=>Setup as T;}
    public static partial class Mathf
    {
        public static float Cos(float f)=>MathF.Cos(f);public static float Sin(float f)=>MathF.Sin(f);public static float Abs(float f)=>MathF.Abs(f);
        public static float Min(float a,float b)=>MathF.Min(a,b);public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Sqrt(float f)=>MathF.Sqrt(f);public static int CeilToInt(float f)=>(int)MathF.Ceiling(f);
        public static float LerpUnclamped(float a,float b,float t)=>a+(b-a)*t;public static float Lerp(float a,float b,float t)=>LerpUnclamped(a,b,Math.Clamp(t,0,1));
    }
}
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
