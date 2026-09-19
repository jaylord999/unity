using System.Collections.Generic;
using UnityEngine;

namespace MysticMap.World
{
    /// <summary>Spawns and tears down the content of a chunk.</summary>
    public static class WorldSpawn
    {
        /// <summary>Instantiate a prefab under a parent with a yaw and a uniform scale.</summary>
        public static GameObject Spawn(GameObject prefab, Transform parent, Vector3 position,
                                       float yaw, float scale)
        {
            if (prefab == null || parent == null) return null;

            GameObject go = Object.Instantiate(prefab, position, Quaternion.Euler(0f, yaw, 0f), parent);
            if (go == null) return null;

            if (!Mathf.Approximately(scale, 1f))
                go.transform.localScale = go.transform.localScale * scale;

            return go;
        }

        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }

    /// <summary>
    /// One streamed piece of the endless world: its terrain mesh, its collider and every
    /// tree/rock/ruin that belongs to it. Destroying the object releases all of it.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldChunk : MonoBehaviour
    {
        public Vector2Int coord;
        public bool highLod;

        MeshFilter _filter;
        MeshRenderer _renderer;
        MeshCollider _collider;
        Transform _propsRoot;

        public TerrainGenerator.ChunkMesh Data { get; private set; }

        public int PropCount { get; private set; }

        public void Setup(Vector2Int chunkCoord, TerrainGenerator.ChunkMesh data, Material[] materials,
                          bool highLodMesh, bool withCollider)
        {
            coord = chunkCoord;
            Data = data;
            highLod = highLodMesh;

            if (_filter == null) _filter = gameObject.AddComponent<MeshFilter>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

            _filter.sharedMesh = data.mesh;
            _renderer.sharedMaterials = materials;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            _renderer.receiveShadows = true;

            if (withCollider)
            {
                if (_collider == null) _collider = gameObject.AddComponent<MeshCollider>();
                _collider.sharedMesh = null;              // force a fresh bake
                _collider.sharedMesh = data.mesh;
                _collider.enabled = true;
            }
            else if (_collider != null)
            {
                _collider.enabled = false;
            }

            if (_propsRoot == null)
            {
                var go = new GameObject("Props");
                go.transform.SetParent(transform, false);
                _propsRoot = go.transform;
            }

            // Let the slime's magic find this chunk's grass, trees and rocks (see MagicEnvironment).
            MagicEnvironment.Register(_propsRoot, MagicEnvMode.Destroy, 160f);
        }

        public Transform PropsRoot
        {
            get
            {
                if (_propsRoot == null)
                {
                    var go = new GameObject("Props");
                    go.transform.SetParent(transform, false);
                    _propsRoot = go.transform;
                }
                return _propsRoot;
            }
        }

        public void AddProp() => PropCount++;

        /// <summary>World height of this chunk's mesh at a local position.</summary>
        public float HeightAtLocal(float localX, float localZ) =>
            Data != null ? Data.SampleHeight(localX, localZ) : 0f;

        /// <summary>Removes the props only (keeps the terrain, used when the detail LOD changes).</summary>
        public void ClearProps()
        {
            if (_propsRoot == null) return;
            for (int i = _propsRoot.childCount - 1; i >= 0; i--)
                WorldSpawn.Destroy(_propsRoot.GetChild(i).gameObject);
            PropCount = 0;
        }

        public void Release()
        {
            MagicEnvironment.Unregister(_propsRoot);

            if (_filter != null) _filter.sharedMesh = null;
            if (_renderer != null) _renderer.sharedMaterials = new Material[0];
            if (_collider != null) _collider.sharedMesh = null;
            if (Data != null && Data.mesh != null) WorldSpawn.Destroy(Data.mesh);
            Data = null;
        }

        void OnDestroy() => Release();
    }
}
