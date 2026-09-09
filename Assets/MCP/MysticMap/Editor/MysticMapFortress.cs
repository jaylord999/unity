#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    public static partial class MysticMapBuilder
    {
        // ---------------------------------------------------------------------
        //  Fortress town (replaces the old loose "village" that the map used to
        //  scatter around the plaza).  Built from the Town pack's stone walls,
        //  towers and gatehouses.  Houses and landmarks are laid on streets that
        //  surround the paved market square, using measured prefab footprints so
        //  nothing overlaps the walls, roads or other buildings.
        // ---------------------------------------------------------------------
        static void CreateFortressTown()
        {
            var root = new GameObject("FortressTown");
            root.transform.position = Vector3.zero;

            var wallRoot = Child(root.transform, "Walls");
            var towerRoot = Child(root.transform, "Towers");
            var gateRoot = Child(root.transform, "Gates");
            var bldRoot = Child(root.transform, "Buildings");

            PlaceCornerTowers(towerRoot);
            PlaceWallRuns(wallRoot);
            PlaceGates(gateRoot);
            PlaceBuildings(bldRoot);

            Log("Fortress town placed.");
        }

        static Transform Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            return go.transform;
        }

        // Four big towers cap the corners of the rectangle; the east and west walls
        // also get a mid-point tower so the perimeter reads as a fortified line.
        static void PlaceCornerTowers(Transform parent)
        {
            var tower = LoadPrefab(TownDir + "/Wall_tower2.prefab");
            if (tower == null) return;

            var sx = new[] { 1f, 1f, -1f, -1f };
            var sz = new[] { 1f, -1f, -1f, 1f };
            for (int i = 0; i < 4; i++)
            {
                float wx = Village.x + sx[i] * FortHX;
                float wz = Village.y + sz[i] * FortHZ;
                float h = WorldSampleHeight(wx, wz);
                SpawnAt(parent, tower, new Vector3(wx, h, wz), 0f, WallScale);
            }

            var mid = LoadPrefab(TownDir + "/Wall_tower1.prefab");
            if (mid == null) return;
            foreach (var dir in new[] { 1f, -1f })
            {
                float wx = Village.x + dir * FortHX;
                float h = WorldSampleHeight(wx, Village.y);
                SpawnAt(parent, mid, new Vector3(wx, h, Village.y), 90f, WallScale);
            }
        }

        // Straight wall runs between the corner towers. North and south sides leave a
        // central opening for the gates; east and west sides are solid.
        static void PlaceWallRuns(Transform parent)
        {
            var seg = LoadPrefab(TownDir + "/Wall_part2.prefab");
            if (seg == null) return;
            float len = 7.815f * WallScale;          // Wall_part2 run length (x) * scale
            float gateLen = 9.445f * WallScale;      // Wall_entrance width along the wall
            float gateHalf = gateLen * 0.5f + 1.2f;  // opening each side of the centre

            foreach (var sz in new[] { 1f, -1f })
            {
                float wz = Village.y + sz * FortHZ;
                FillRunX(parent, seg, len, Village.x - FortHX, Village.x - gateHalf, wz);
                FillRunX(parent, seg, len, Village.x + gateHalf, Village.x + FortHX, wz);
            }
            foreach (var sgn in new[] { 1f, -1f })
            {
                float wx = Village.x + sgn * FortHX;
                FillRunZ(parent, seg, len, Village.y - FortHZ, Village.y + FortHZ, wx);
            }
        }

        static void FillRunX(Transform parent, GameObject seg, float len, float x0, float x1, float zW)
        {
            float range = x1 - x0;
            if (range < 0.5f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(range / len));
            float step = range / n;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + step * (i + 0.5f);
                SpawnAt(parent, seg, new Vector3(x, WorldSampleHeight(x, zW), zW), 0f, WallScale);
            }
        }

        static void FillRunZ(Transform parent, GameObject seg, float len, float z0, float z1, float xW)
        {
            float range = z1 - z0;
            if (range < 0.5f) return;
            int n = Mathf.Max(1, Mathf.RoundToInt(range / len));
            float step = range / n;
            for (int i = 0; i < n; i++)
            {
                float z = z0 + step * (i + 0.5f);
                SpawnAt(parent, seg, new Vector3(xW, WorldSampleHeight(xW, z), z), 90f, WallScale);
            }
        }
        // The two main gates sit in the middle of the north and south walls, each a tall
        // gatehouse flanked by a pair of smaller towers beside the road.
        static void PlaceGates(Transform parent)
        {
            var gate = LoadPrefab(TownDir + "/Wall_entrance.prefab");
            if (gate == null) return;
            var ft = LoadPrefab(TownDir + "/Wall_tower2.prefab");
            float gateLen = 9.445f * WallScale;
            float off = gateLen * 0.5f + 3.2f;

            foreach (var sz in new[] { 1f, -1f })
            {
                float wz = Village.y + sz * FortHZ;
                SpawnAt(parent, gate, new Vector3(Village.x, WorldSampleHeight(Village.x, wz), wz), 0f, WallScale);
                if (ft == null) continue;
                foreach (var sd in new[] { 1f, -1f })
                {
                    float wx = Village.x + sd * off;
                    SpawnAt(parent, ft, new Vector3(wx, WorldSampleHeight(wx, wz), wz), 0f, WallScale);
                }
            }
        }

        // Houses + landmarks.  Candidates are tried in order; anything that would touch the
        // paved square, a road, the walls, or an already-placed building is skipped, so a
        // tidy town always results no matter what was on offer.
        static void PlaceBuildings(Transform parent)
        {
            var placed = new List<Bounds>();

            // Landmarks first (market inn, a windmill) so houses tuck around them.
            TryBld(parent, "Tavern", Village.x + 27f, Village.y + 22f, 0f, 1.0f, placed);
            TryBld(parent, "Windmill", Village.x + 40f, Village.y - 24f, 0f, 0.9f, placed);

            string[] pool =
            {
                "House1", "House2", "House3",
                "WoodenHouse_1", "House1", "WoodenHouse_2"
            };

            // Two rings of houses around the square. They deliberately leave the two main
            // avenues open where they cross each ring (those candidates get rejected), which
            // makes the town read as streets lined with houses.
            AddRing(parent, pool, placed, 24f, 8, 20f);
            AddRing(parent, pool, placed, 38f, 14, 4f);
        }

        static void AddRing(Transform parent, string[] pool, List<Bounds> placed,
                            float radius, int count, float angStart)
        {
            for (int i = 0; i < count; i++)
            {
                float deg = angStart + i * (360f / count);
                float rad = deg * Mathf.Deg2Rad;
                float x = Village.x + Mathf.Sin(rad) * radius;
                float z = Village.y + Mathf.Cos(rad) * radius;
                float yaw = (i % 2 == 0) ? 0f : 90f;
                string nm = pool[(i * 3 + i / 2) % pool.Length];
                TryBld(parent, nm, x, z, yaw, 1.0f, placed);
            }
        }
        // Place one building after rejecting it if it would overlap the plaza, either road,
        // the walls or a building that is already down.
        static void TryBld(Transform parent, string prefabName, float x, float z, float yaw,
                           float scale, List<Bounds> placed)
        {
            var prefab = LoadPrefab(TownDir + "/" + prefabName + ".prefab");
            if (prefab == null) return;
            if (x < 6f || x > MapSize - 6f || z < 6f || z > MapSize - 6f) return;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (go == null) go = (GameObject)Object.Instantiate(prefab);
            if (go == null) return;

            go.transform.position = new Vector3(x, 0f, z);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;

            Bounds b = CombinedBounds(go);

            // 1) fully inside the walls, clear of the perimeter.
            if (b.min.x < Village.x - FortHX + 2.5f || b.max.x > Village.x + FortHX - 2.5f ||
                b.min.z < Village.y - FortHZ + 2.5f || b.max.z > Village.y + FortHZ - 2.5f)
            { DestroyBld(go); return; }

            // 2) clear of the paved market square (small apron allowed).
            float px = Mathf.Clamp(Village.x, b.min.x, b.max.x);
            float pz = Mathf.Clamp(Village.y, b.min.z, b.max.z);
            if (Vector2.Distance(new Vector2(px, pz), Village) < PlazaRadius + 0.5f)
            { DestroyBld(go); return; }

            // 3) clear of the two main roads that run through the town.
            if (b.min.z < Village.y + RoadHalfWidth + 0.4f && b.max.z > Village.y - RoadHalfWidth - 0.4f)
            { DestroyBld(go); return; }
            if (b.min.x < Village.x + RoadHalfWidth + 0.4f && b.max.x > Village.x - RoadHalfWidth - 0.4f)
            { DestroyBld(go); return; }

            // 4) clear of everything already placed.
            foreach (var p in placed)
                if (OverlapXZ(b, p, 0.6f)) { DestroyBld(go); return; }

            float h = WorldSampleHeight(x, z);
            go.transform.position = new Vector3(x, h, z);
            go.transform.SetParent(parent, true);
            placed.Add(b);
        }

        static Bounds CombinedBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(true);
            if (rends == null || rends.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);
            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
            return b;
        }

        static bool OverlapXZ(Bounds a, Bounds b, float pad)
        {
            return a.min.x - pad < b.max.x && a.max.x + pad > b.min.x &&
                   a.min.z - pad < b.max.z && a.max.z + pad > b.min.z;
        }

        static void DestroyBld(GameObject go) => Object.DestroyImmediate(go);
    }
}
#endif
