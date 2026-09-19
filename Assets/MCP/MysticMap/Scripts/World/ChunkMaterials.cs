using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>
    /// The four ground materials every chunk mesh uses (grass / soil / rock / road), tinted
    /// per biome. They are created once and shared by every chunk, so the streamed world
    /// costs the same as one mesh as far as materials go.
    ///
    /// Note: the ground look is built from sub-meshes with solid URP/Lit materials, so it
    /// works in the URP project without a custom terrain shader.
    /// </summary>
    public static class ChunkMaterials
    {
        public const int Grass = 0;
        public const int Soil = 1;
        public const int Rock = 2;
        public const int Road = 3;
        public const int Count = 4;

        static readonly Dictionary<int, Material[]> _cache = new Dictionary<int, Material[]>();

        /// <summary>Ground materials for a biome (cached).</summary>
        public static Material[] For(BiomeType biome)
        {
            int key = (int)biome;
            if (_cache.TryGetValue(key, out Material[] cached) && cached != null && cached.Length == Count)
                return cached;

            Color grass, soil, rock, road;
            Tint(biome, out grass, out soil, out rock, out road);

            var set = new Material[Count];
            set[Grass] = Make("ProcGrass_" + biome, grass, 0.08f);
            set[Soil] = Make("ProcSoil_" + biome, soil, 0.05f);
            set[Rock] = Make("ProcRock_" + biome, rock, 0.18f);
            set[Road] = Make("ProcRoad_" + biome, road, 0.04f);

            _cache[key] = set;
            return set;
        }

        static void Tint(BiomeType biome, out Color grass, out Color soil, out Color rock, out Color road)
        {
            // Base palette (close to the hand-built map's terrain layers).
            grass = new Color(0.40f, 0.56f, 0.32f);
            soil = new Color(0.36f, 0.30f, 0.22f);
            rock = new Color(0.44f, 0.44f, 0.46f);
            road = new Color(0.37f, 0.32f, 0.25f);

            switch (biome)
            {
                case BiomeType.Forest:
                    grass = new Color(0.26f, 0.42f, 0.22f);
                    soil = new Color(0.29f, 0.25f, 0.18f);
                    break;
                case BiomeType.Highlands:
                    grass = new Color(0.45f, 0.50f, 0.31f);
                    soil = new Color(0.40f, 0.34f, 0.24f);
                    rock = new Color(0.47f, 0.45f, 0.43f);
                    break;
                case BiomeType.Rocky:
                    grass = new Color(0.36f, 0.42f, 0.34f);
                    rock = new Color(0.40f, 0.39f, 0.41f);
                    soil = new Color(0.33f, 0.30f, 0.26f);
                    break;
                case BiomeType.Ruins:
                    grass = new Color(0.44f, 0.46f, 0.34f);
                    soil = new Color(0.42f, 0.38f, 0.29f);
                    rock = new Color(0.50f, 0.48f, 0.44f);
                    break;
                case BiomeType.Labyrinth:
                    grass = new Color(0.34f, 0.42f, 0.27f);
                    soil = new Color(0.35f, 0.32f, 0.24f);
                    rock = new Color(0.46f, 0.45f, 0.45f);
                    break;
            }
        }

        static Material Make(string name, Color color, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var m = new Material(shader) { name = name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            return m;
        }

        /// <summary>Clears the cache (used when the project's URP assets are reloaded).</summary>
        public static void Clear() => _cache.Clear();
    }
}
