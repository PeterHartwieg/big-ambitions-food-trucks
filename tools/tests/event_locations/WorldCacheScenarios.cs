using FoodTrucks;
using UnityEngine;
class CityBuildingController { internal Building building = new(); }
class Building { internal Address Address = new(); }
class Address { internal string streetName = "ba:street_test"; internal int streetNumber = 1; }
namespace UnityEngine { struct Vector3 { } }
namespace FoodTrucks
{
    static class ParkAreas { internal static void Reset() { } }
    static partial class World
    {
        internal static List<(CityBuildingController Controller, Vector3[] Doors)> Native = new();
        internal static int Reads;
        static List<(CityBuildingController Controller, Vector3[] Doors)> Buildings() { Reads++; return Native; }
        internal static void Check()
        {
            if (BuildingAt("ba:street_test#1") != null) throw new Exception("Empty world cached a result");
            var a = new CityBuildingController(); Native.Add((a, Array.Empty<Vector3>()));
            if (BuildingAt("ba:street_test#1") != a) throw new Exception("Empty world must retry");
            var reads = Reads;
            for (var i = 0; i < 1000; i++)
            {
                if (BuildingAt("ba:street_test#1") != a || BuildingAt("missing") != null) throw new Exception("Bad cached result");
            }
            if (Reads != reads) throw new Exception("Building lookup enumerated world after cache creation");
            var b = new CityBuildingController(); Native.Clear(); Native.Add((b, Array.Empty<Vector3>()));
            Reset();
            if (BuildingAt("ba:street_test#1") != b) throw new Exception("Reset kept old city's controller");
            Console.WriteLine("PASS: controller cache, absent world retry and reset");
        }
    }
}
static class Program { static void Main() => World.Check(); }
