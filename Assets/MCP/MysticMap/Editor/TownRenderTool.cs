#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MysticMap.EditorTools
{
    /// <summary>
    /// Shared helper: renders the current open scene to PNG(s) from given camera poses so we
    /// can inspect the look of generated towns during batch-mode iteration.
    /// </summary>
    public static class TownRenderTool
    {
        public static string BaseDir = "Logs";

        public static void EnsureLight()
        {
            var existing = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var l in existing)
                if (l.type == LightType.Directional) return;
            var go = new GameObject("Sun");
            var sun = go.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.color = new Color(1f, 0.95f, 0.88f);
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        public static void AddGround()
        {
            // Simple soft ground for look-dev (flat). No terrain needed here.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(600f, 1f, 600f);
            var mr = ground.GetComponent<MeshRenderer>();
            if (mr != null) mr.sharedMaterial = MakeMat(new Color(0.40f, 0.55f, 0.33f), new Color(0.28f,0.38f,0.24f));
        }

        public static Material MakeMat(Color a, Color b)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", a);
            else if (m.HasProperty("_Color")) m.SetColor("_Color", a);
            m.SetFloat("_Smoothness", 0.1f);
            return m;
        }

        /// <summary>Render current scene to PNG. Returns full path.</summary>
        public static string Shot(string tag, Vector3 pos, Vector3 look, float fov, int w = 1500, int h = 850)
        {
            EnsureLight();
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
            var camGo = new GameObject("ShotCam");
            camGo.transform.position = pos;
            camGo.transform.LookAt(look);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.60f, 0.75f, 0.85f, 1f);
            cam.targetTexture = rt;
            cam.renderingPath = RenderingPath.UsePlayerSettings;
            cam.allowHDR = false;
            cam.allowMSAA = true;

            cam.Render();

            var old = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = old;

            Directory.CreateDirectory(BaseDir);
            string path = Path.Combine(BaseDir, tag + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());

            Object.DestroyImmediate(camGo);
            RenderTexture.ReleaseTemporary(rt);
            Object.DestroyImmediate(tex);
            Debug.Log("SHOT " + path);
            return path;
        }
    }
}
#endif
