using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// Turns the noise fields into ground.
    ///
    /// Height at a point is a pure function of (seed, x, z):
    ///   1. a biome mix picks the rolling shape (see <see cref="BiomeGenerator"/>),
    ///   2. the road network grades its corridor through it (cut on hills, fill in dips,
    ///      with an apron whose width follows the size of the cut so nothing turns into a
    ///      vertical wall),
    ///   3. near the hand-built map the result is blended up to the map's edge height, so
    ///      the two meshes meet without a step.
    ///
    /// Because a chunk mesh samples this function exactly on its border lines, neighbouring
    /// chunks always agree along the seam, and the frozen town keeps its own geometry.
    /// </summary>
    public class TerrainGenerator
    {
        readonly WorldSettings _s;
        readonly ILegacyMap _town;

        public BiomeGenerator Biomes { get; }
        public RoadNetworkGenerator Roads { get; }

        public TerrainGenerator(WorldSettings settings, ILegacyMap town)
        {
            _s = settings;
            _town = town;
            Biomes = new BiomeGenerator(settings);
            Roads = new RoadNetworkGenerator(settings, town);
        }

        public float ChunkSize => _s.chunkSize;

        // =====================================================================
        //  Height field
        // =====================================================================
        /// <summary>Terrain height ignoring roads and the town seam.</summary>
        public float NaturalHeight(float x, float z)
        {
            BiomeWeights w = Biomes.Weights(x, z);

            uint salt = (uint)_s.seed;
            float hills = ProcNoise.Fbm(x * 0.0038f, z * 0.0038f, salt + 1301u, 4);
            float detail = ProcNoise.Fbm(x * 0.021f, z * 0.021f, salt + 1601u, 3) - 0.5f;
            float ridge = ProcNoise.Ridged(x * 0.0095f, z * 0.0095f, salt + 1901u, 4);

            float h = _s.baseHeight;
            h += w.meadow * (6f + hills * 11f);
            h += w.forest * (12f + hills * 17f);
            h += w.highlands * (26f + hills * 34f);
            h += w.rocky * (40f + ridge * 52f + hills * 12f);
            h += w.ruins * (5f + hills * 5f);
            h += w.labyrinth * (9f + hills * 7f);

            // A little roughness everywhere so no biome reads as a flat plane.
            h += detail * (2.2f + w.rocky * 4f);

            return Mathf.Clamp(h, 4f, _s.maxHeight);
        }

        /// <summary>1 far from the hand-built map, 0 right at its edge.</summary>
        public float SeamWeight(float x, float z)
        {
            if (_town == null || !_town.HasLegacy) return 1f;
            return ProcNoise.Smoothstep(0f, _s.seamBand, _town.OutsideDistance(x, z));
        }

        /// <summary>Full terrain height at a world position (without a prepared road field).</summary>
        public float Height(float x, float z)
        {
            var field = Roads.BuildField(x, z, x, z, _s.roadMaxApron);
            return Height(x, z, field);
        }

        /// <summary>Full terrain height, reusing an already built <see cref="RoadField"/>.</summary>
        public float Height(float x, float z, RoadField field)
        {
            float h = NaturalHeight(x, z);

            // ---- roads: grade a corridor through the ground -------------------
            if (field != null && !field.IsEmpty)
            {
                RoadHit hit = field.Query(x, z);
                if (hit.valid)
                {
                    float roadH = Roads.RoadElevation(x, z);
                    float hw = hit.halfWidth;

                    // The apron widens with the size of the cut/fill, so a road through a
                    // hill becomes a graded valley instead of a canyon with vertical walls.
                    float diff = Mathf.Abs(h - roadH);
                    float apron = Mathf.Min(_s.roadMaxApron, hw + 9f + diff * 1.35f);
                    float t = 1f - ProcNoise.Smoothstep(hw + 1.5f, apron, hit.distance);
                    if (t > 0.001f) h = Mathf.Lerp(h, roadH, t);
                }
            }

            // ---- seam with the hand-built map ---------------------------------
            if (_town != null && _town.HasLegacy)
            {
                float sw = SeamWeight(x, z);
                if (sw < 0.999f)
                {
                    float edge = _town.EdgeHeight(x, z);
                    h = Mathf.Lerp(edge, h, sw);
                }
            }

            return Mathf.Clamp(h, 2f, _s.maxHeight);
        }

        /// <summary>Terrain slope in degrees (finite differences of the full height).</summary>
        public float SlopeDegrees(float x, float z, RoadField field, float delta = 2f)
        {
            float hx = Height(x + delta, z, field) - Height(x - delta, z, field);
            float hz = Height(x, z + delta, field) - Height(x, z - delta, field);
            float rise = Mathf.Sqrt(hx * hx + hz * hz) / (2f * delta);
            return Mathf.Atan(rise) * Mathf.Rad2Deg;
        }

        /// <summary>World height of the nearest road surface (for road-side props).</summary>
        public float HeightAt(Vector3 worldPoint, RoadField field) => Height(worldPoint.x, worldPoint.z, field);

        // =====================================================================
        //  Chunk mesh
        // =====================================================================
        /// <summary>A generated chunk mesh plus the data the other passes need.</summary>
        public sealed class ChunkMesh
        {
            public Mesh mesh;
            public float[] heights;      // res * res, chunk-local grid
            public float[] roadDistance; // res * res, distance to the nearest road centre line
            public int res;
            public float step;
            public float minHeight, maxHeight;
            public BiomeType dominant;

            /// <summary>Bilinear height of the built mesh (identical to what the player stands on).</summary>
            public float SampleHeight(float localX, float localZ)
            {
                if (heights == null || res < 2) return 0f;
                float fx = Mathf.Clamp(localX / step, 0f, res - 1.001f);
                float fz = Mathf.Clamp(localZ / step, 0f, res - 1.001f);
                int i0 = (int)fx, j0 = (int)fz;
                float tx = fx - i0, tz = fz - j0;
                int i1 = Mathf.Min(i0 + 1, res - 1), j1 = Mathf.Min(j0 + 1, res - 1);

                float h00 = heights[j0 * res + i0], h10 = heights[j0 * res + i1];
                float h01 = heights[j1 * res + i0], h11 = heights[j1 * res + i1];
                return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
            }

            public float SampleRoadDistance(float localX, float localZ)
            {
                if (roadDistance == null || res < 2) return 1e9f;
                float fx = Mathf.Clamp(localX / step, 0f, res - 1.001f);
                float fz = Mathf.Clamp(localZ / step, 0f, res - 1.001f);
                int i0 = (int)fx, j0 = (int)fz;
                float tx = fx - i0, tz = fz - j0;
                int i1 = Mathf.Min(i0 + 1, res - 1), j1 = Mathf.Min(j0 + 1, res - 1);

                float r00 = roadDistance[j0 * res + i0], r10 = roadDistance[j0 * res + i1];
                float r01 = roadDistance[j1 * res + i0], r11 = roadDistance[j1 * res + i1];
                return Mathf.Lerp(Mathf.Lerp(r00, r10, tx), Mathf.Lerp(r01, r11, tx), tz);
            }
        }

        /// <summary>Builds the terrain mesh for one chunk (local space, chunk origin at 0,0).</summary>
        public ChunkMesh BuildMesh(int cx, int cz, bool highLod, RoadField field)
        {
            int res = Mathf.Clamp(highLod ? _s.meshResolution : _s.farMeshResolution, 3, 257);
            float size = _s.chunkSize;
            float step = size / (res - 1);
            float ox = cx * size, oz = cz * size;

            var heights = new float[res * res];
            var roadDist = new float[res * res];
            var halfWidth = new float[res * res];

            float minH = float.MaxValue, maxH = float.MinValue;
            bool hasRoads = field != null && !field.IsEmpty;

            float wMeadow = 0f, wForest = 0f, wHigh = 0f, wRocky = 0f, wRuins = 0f, wLab = 0f;

            for (int j = 0; j < res; j++)
            {
                float lz = j * step;
                for (int i = 0; i < res; i++)
                {
                    float lx = i * step;
                    int idx = j * res + i;

                    float h = Height(ox + lx, oz + lz, field);
                    heights[idx] = h;
                    if (h < minH) minH = h;
                    if (h > maxH) maxH = h;

                    if (hasRoads)
                    {
                        RoadHit hit = field.Query(ox + lx, oz + lz);
                        roadDist[idx] = hit.valid ? hit.distance : 1e9f;
                        halfWidth[idx] = hit.halfWidth;
                    }
                    else
                    {
                        roadDist[idx] = 1e9f;
                        halfWidth[idx] = 0f;
                    }
                }
            }

            // Biome of the chunk (4 corners + centre - it only drives the material tint).
            float[] px = { 0f, size, 0f, size, size * 0.5f };
            float[] pz = { 0f, 0f, size, size, size * 0.5f };
            for (int s = 0; s < px.Length; s++)
            {
                BiomeWeights w = Biomes.Weights(ox + px[s], oz + pz[s]);
                wMeadow += w.meadow; wForest += w.forest; wHigh += w.highlands;
                wRocky += w.rocky; wRuins += w.ruins; wLab += w.labyrinth;
            }
            var mix = new BiomeWeights
            {
                meadow = wMeadow, forest = wForest, highlands = wHigh,
                rocky = wRocky, ruins = wRuins, labyrinth = wLab,
            }.Normalized();
            float rockBias = Mathf.Clamp01(mix.rocky + mix.highlands * 0.5f);

            var cmesh = new ChunkMesh
            {
                heights = heights,
                roadDistance = roadDist,
                res = res,
                step = step,
                minHeight = minH,
                maxHeight = maxH,
                dominant = mix.Dominant,
            };

            BuildGeometry(cmesh, res, size, step, highLod, roadDist, halfWidth, rockBias);
            return cmesh;
        }

        /// <summary>Turns the sampled grid into a 4-sub-mesh mesh (grass / soil / rock / road).</summary>
        void BuildGeometry(ChunkMesh outMesh, int res, float size, float step, bool highLod,
                           float[] roadDist, float[] halfWidth, float rockBias)
        {
            float[] h = outMesh.heights;
            int vertCount = res * res;

            var verts = new List<Vector3>(vertCount + res * 8);
            var uvs = new List<Vector2>(vertCount + res * 8);
            var tris = new List<int>[ChunkMaterials.Count];
            for (int i = 0; i < tris.Length; i++) tris[i] = new List<int>(res * res * 3);

            for (int j = 0; j < res; j++)
            {
                float lz = j * step;
                for (int i = 0; i < res; i++)
                {
                    float lx = i * step;
                    verts.Add(new Vector3(lx, h[j * res + i], lz));
                    uvs.Add(new Vector2(lx * 0.25f, lz * 0.25f));
                }
            }

            for (int j = 0; j < res - 1; j++)
            {
                for (int i = 0; i < res - 1; i++)
                {
                    int i0 = j * res + i;
                    int i1 = i0 + 1;
                    int i3 = i0 + res;
                    int i2 = i3 + 1;

                    float rd = (roadDist[i0] + roadDist[i1] + roadDist[i2] + roadDist[i3]) * 0.25f;
                    float hw = Mathf.Max(Mathf.Max(halfWidth[i0], halfWidth[i1]),
                                         Mathf.Max(halfWidth[i2], halfWidth[i3]));

                    float riseX = Mathf.Max(Mathf.Abs(h[i1] - h[i0]), Mathf.Abs(h[i2] - h[i3]));
                    float riseZ = Mathf.Max(Mathf.Abs(h[i3] - h[i0]), Mathf.Abs(h[i2] - h[i1]));
                    float slope = Mathf.Atan2(Mathf.Max(riseX, riseZ), step) * Mathf.Rad2Deg;

                    int type;
                    if (rd <= hw + 0.7f) type = ChunkMaterials.Road;
                    else if (slope > 34f || (rockBias > 0.55f && slope > 20f)) type = ChunkMaterials.Rock;
                    else if (rd <= hw + 7f) type = ChunkMaterials.Soil;
                    else type = ChunkMaterials.Grass;

                    var list = tris[type];
                    list.Add(i0); list.Add(i2); list.Add(i1);
                    list.Add(i0); list.Add(i3); list.Add(i2);
                }
            }

            // A short, double sided skirt around far chunks hides the tiny gaps that appear
            // where a coarse mesh meets a detailed one.
            if (!highLod) AddSkirt(verts, uvs, tris[ChunkMaterials.Grass], res, step, 2.5f);

            var mesh = new Mesh { name = "Chunk_" + res };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = ChunkMaterials.Count;
            for (int i = 0; i < tris.Length; i++) mesh.SetTriangles(tris[i], i, false);
            mesh.RecalculateNormals();

            float midY = (outMesh.minHeight + outMesh.maxHeight) * 0.5f;
            float height = Mathf.Max(4f, outMesh.maxHeight - outMesh.minHeight);
            mesh.bounds = new Bounds(new Vector3(size * 0.5f, midY, size * 0.5f),
                                     new Vector3(size, height + 6f, size));

            outMesh.mesh = mesh;
        }

        static void AddSkirt(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                             int res, float step, float depth)
        {
            int first = verts.Count;
            int last = res - 1;

            // Boundary loop, walked so the skirt faces outward (both windings are emitted,
            // which is cheap and removes any doubt about the facing direction).
            var loop = new List<int>(res * 4);
            for (int i = 0; i < res; i++) loop.Add(i);                       // z = 0   edge
            for (int j = 1; j < res; j++) loop.Add(j * res + last);          // x = max edge
            for (int i = last - 1; i >= 0; i--) loop.Add(last * res + i);    // z = max edge
            for (int j = last - 1; j >= 1; j--) loop.Add(j * res);           // x = 0   edge

            int n = loop.Count;
            for (int k = 0; k < n; k++)
            {
                int top = loop[k];
                Vector3 v = verts[top];
                verts.Add(new Vector3(v.x, v.y - depth, v.z));
                uvs.Add(uvs[top]);
            }

            for (int k = 0; k < n; k++)
            {
                int topA = loop[k];
                int topB = loop[(k + 1) % n];
                int botA = first + k;
                int botB = first + (k + 1) % n;

                tris.Add(topA); tris.Add(botB); tris.Add(botA);
                tris.Add(topA); tris.Add(topB); tris.Add(botB);

                // reverse winding (visible from the inside too)
                tris.Add(topA); tris.Add(botA); tris.Add(botB);
                tris.Add(topA); tris.Add(botB); tris.Add(topB);
            }
        }
    }
}
