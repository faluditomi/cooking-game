using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// Renders cookware from the CC0 "3D Cooking Assets" pack (MilkAndBanana) into transparent
    /// sprites for the prototype: the prep tools, the pot and pan, the cutting boards and plates.
    /// The FBX files import without their colours, so the shared palette is read from one of the
    /// pack's GLB files and put on every model before rendering. The PNGs are ours to commit; the 3D
    /// pack itself doesn't need to be in git.
    /// </summary>
    public static class CookwareSprites
    {
        private const string PackRoot = "Assets/Art Assets/3D Cooking Assets/Cooking Assets";
        public const string OutputFolder = "Assets/Art Assets/Cookware";
        private const int Size = 512;

        private struct Item
        {
            public string Name;
            public string Model;
            /// <summary>Turns the model before rendering (tools hang head down from the rail).</summary>
            public Vector3 ModelRotation;
            /// <summary>The camera's rotation: (90, 0, 0) looks straight down.</summary>
            public Vector3 View;
            public string[] Hide;
        }

        private static readonly Item[] Items =
        {
            new Item { Name = "knife", Model = "FBX/Knives/knife_chef_knife.fbx", ModelRotation = new Vector3(0f, 0f, 180f), View = new Vector3(8f, -20f, 0f) },
            new Item { Name = "peeler", Model = "FBX/All/cutensils_peeler.fbx", ModelRotation = new Vector3(0f, 0f, 180f), View = new Vector3(8f, -20f, 0f) },
            new Item { Name = "spoon", Model = "FBX/All/misc_wooden_spoon.fbx", ModelRotation = new Vector3(-90f, 0f, 0f), View = new Vector3(8f, -20f, 0f) },
            new Item { Name = "grater", Model = "FBX/All/cutensils_box_grater.fbx", View = new Vector3(12f, -30f, 0f) },
            new Item { Name = "pan", Model = "FBX/Pans/pan_wok.fbx", View = new Vector3(50f, -20f, 0f), Hide = new[] { "pan wok lid" } },
            // Open, so the ingredients and the soup show inside.
            new Item { Name = "pot", Model = "FBX/Pans/pan_sauce_pan_large_001.fbx", View = new Vector3(55f, -20f, 0f), Hide = new[] { "pan sauce pan large lid" } },
            new Item { Name = "cutting_board", Model = "FBX/All/board_cutting_board_001.fbx", View = new Vector3(90f, 0f, 0f) },
            new Item { Name = "plate", Model = "FBX/All/plate.fbx", View = new Vector3(90f, 0f, 0f) },
        };

        [MenuItem("Custom Tools/Cooking Prototype/Render Cookware Sprites")]
        public static void RenderAll()
        {
            if(!AssetDatabase.IsValidFolder(PackRoot))
            {
                Debug.LogWarning("[Cooking] The 3D Cooking Assets pack isn't in the project (" + PackRoot + ").");
                return;
            }

            Texture2D palette = LoadPalette();

            if(palette == null)
            {
                Debug.LogWarning("[Cooking] Couldn't read the colour palette from the pack's GLB files.");
                return;
            }

            Directory.CreateDirectory(OutputFolder);
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetTexture("_BaseMap", palette);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0.3f);

            GameObject cameraObject = new GameObject("Cookware Camera") { hideFlags = HideFlags.HideAndDontSave };
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.enabled = false;

            GameObject keyObject = new GameObject("Cookware Key Light") { hideFlags = HideFlags.HideAndDontSave };
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.3f;
            GameObject fillObject = new GameObject("Cookware Fill Light") { hideFlags = HideFlags.HideAndDontSave };
            Light fill = fillObject.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.45f;

            RenderTexture target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            camera.targetTexture = target;
            List<string> written = new List<string>();

            try
            {
                foreach(Item item in Items)
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackRoot + "/" + item.Model);
                    if(prefab == null)
                    {
                        Debug.LogWarning("[Cooking] Missing model " + item.Model);
                        continue;
                    }

                    // Lights follow the view so every sprite is lit the same way.
                    camera.transform.rotation = Quaternion.Euler(item.View);
                    keyObject.transform.rotation = camera.transform.rotation * Quaternion.Euler(35f, -40f, 0f);
                    fillObject.transform.rotation = camera.transform.rotation * Quaternion.Euler(-20f, 50f, 0f);

                    string path = OutputFolder + "/" + item.Name + ".png";
                    File.WriteAllBytes(path, Render(prefab, item, material, camera, target).EncodeToPNG());
                    written.Add(path);
                }
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(keyObject);
                Object.DestroyImmediate(fillObject);
                Object.DestroyImmediate(material);
            }

            AssetDatabase.Refresh();

            foreach(string path in written)
            {
                // Rewritten files keep their old import otherwise.
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                // One sprite per image: Multiple would keep slices cut from an earlier render.
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            Debug.Log("[Cooking] Rendered " + written.Count + " cookware sprites into " + OutputFolder + ".");
            AssignToScene();
        }

        /// <summary>Puts the rendered sprites on the prototype in the open scene.</summary>
        [MenuItem("Custom Tools/Cooking Prototype/Assign Cookware Sprites")]
        public static void AssignToScene()
        {
            CookingPrototype prototype = Object.FindAnyObjectByType<CookingPrototype>();

            if(prototype == null)
            {
                Debug.LogWarning("[Cooking] Open the prototype scene to assign the cookware sprites.");
                return;
            }

            prototype.SetCookwareArt(Load("knife"), Load("peeler"), Load("grater"), Load("pan"), Load("pot"), Load("cutting_board"), Load("plate"), Load("spoon"));
            EditorUtility.SetDirty(prototype);
            EditorSceneManager.MarkSceneDirty(prototype.gameObject.scene);
            EditorSceneManager.SaveScene(prototype.gameObject.scene);
        }

        private static Sprite Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(OutputFolder + "/" + name + ".png");
        }

        private static Texture2D Render(GameObject prefab, Item item, Material material, Camera camera, RenderTexture target)
        {
            GameObject model = Object.Instantiate(prefab);
            model.hideFlags = HideFlags.HideAndDontSave;

            try
            {
                model.transform.position = new Vector3(5000f, 0f, 0f);
                // On top of the import's own rotation (the FBX files are Z-up).
                model.transform.rotation = Quaternion.Euler(item.ModelRotation) * prefab.transform.rotation;

                List<Renderer> renderers = new List<Renderer>();

                foreach(Renderer renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if(item.Hide != null && System.Array.IndexOf(item.Hide, renderer.gameObject.name) >= 0)
                    {
                        renderer.enabled = false;
                        continue;
                    }

                    renderer.sharedMaterial = material;
                    renderers.Add(renderer);
                }

                // Fit the model's corners, as the camera sees them, into the square.
                Bounds bounds = renderers[0].bounds;
                foreach(Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);

                Quaternion inverse = Quaternion.Inverse(camera.transform.rotation);
                Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
                Vector2 max = new Vector2(float.MinValue, float.MinValue);

                for(int i = 0; i < 8; i++)
                {
                    Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 local = inverse * corner;
                    min = Vector2.Min(min, local);
                    max = Vector2.Max(max, local);
                }

                Vector2 centre = (min + max) * 0.5f;
                float depth = bounds.extents.magnitude * 4f;
                camera.transform.position = camera.transform.rotation * new Vector3(centre.x, centre.y, (inverse * bounds.center).z - depth);
                camera.orthographicSize = Mathf.Max(max.x - min.x, max.y - min.y) * 0.5f * 1.04f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = depth * 2f;
                camera.Render();

                RenderTexture.active = target;
                Texture2D full = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                full.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                full.Apply();
                RenderTexture.active = null;
                return Trim(full);
            }
            finally
            {
                Object.DestroyImmediate(model);
            }
        }

        /// <summary>Crops to what was drawn (plus a small margin), so the sprite's shape is the object's.</summary>
        private static Texture2D Trim(Texture2D full)
        {
            Color32[] pixels = full.GetPixels32();
            int minX = Size, minY = Size, maxX = -1, maxY = -1;

            for(int y = 0; y < Size; y++)
            {
                for(int x = 0; x < Size; x++)
                {
                    if(pixels[y * Size + x].a < 8) continue;
                    minX = Mathf.Min(minX, x);
                    maxX = Mathf.Max(maxX, x);
                    minY = Mathf.Min(minY, y);
                    maxY = Mathf.Max(maxY, y);
                }
            }

            if(maxX < 0) return full;

            const int margin = 4;
            minX = Mathf.Max(0, minX - margin);
            minY = Mathf.Max(0, minY - margin);
            maxX = Mathf.Min(Size - 1, maxX + margin);
            maxY = Mathf.Min(Size - 1, maxY + margin);

            Texture2D trimmed = new Texture2D(maxX - minX + 1, maxY - minY + 1, TextureFormat.RGBA32, false);
            trimmed.SetPixels(full.GetPixels(minX, minY, trimmed.width, trimmed.height));
            trimmed.Apply();
            Object.DestroyImmediate(full);
            return trimmed;
        }

        /// <summary>The pack's shared colour strip, embedded in its GLB files.</summary>
        private static Texture2D LoadPalette()
        {
            string glb = PackRoot + "/GLB/Pans/pan_wok.glb";
            if(!File.Exists(glb)) return null;

            byte[] data = File.ReadAllBytes(glb);
            int start = Find(data, new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 0);
            int end = start >= 0 ? Find(data, new byte[] { 0x49, 0x45, 0x4E, 0x44 }, start) : -1;
            if(start < 0 || end < 0) return null;

            byte[] png = new byte[end + 8 - start];
            System.Array.Copy(data, start, png, 0, png.Length);
            Texture2D palette = new Texture2D(2, 2);
            if(!palette.LoadImage(png)) return null;
            palette.filterMode = FilterMode.Point;
            palette.wrapMode = TextureWrapMode.Clamp;
            return palette;
        }

        private static int Find(byte[] data, byte[] pattern, int from)
        {
            for(int i = from; i <= data.Length - pattern.Length; i++)
            {
                int j = 0;
                while(j < pattern.Length && data[i + j] == pattern[j]) j++;
                if(j == pattern.Length) return i;
            }

            return -1;
        }
    }
}
