using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// The UI skin: a few pieces of Kenney's "UI Pack: Sci-fi" (CC0), copied into
    /// Assets/Art Assets/UI Skin. Sets them up as nine-sliced sprites (the corners stay put when a
    /// panel or button stretches) and the pointer as a cursor, then puts them on the prototype.
    /// </summary>
    public static class UiSkinSetup
    {
        public const string Folder = "Assets/Art Assets/UI Skin";

        [MenuItem("Custom Tools/Cooking Prototype/Assign UI Skin")]
        public static void Assign()
        {
            CookingPrototype prototype = Object.FindAnyObjectByType<CookingPrototype>();

            if(prototype == null)
            {
                Debug.LogWarning("[Cooking] Open the prototype scene to assign the UI skin.");
                return;
            }

            // Borders in pixels: left, bottom, right, top. The button's raised lip is at the bottom.
            Sprite panel = Sliced("panel", new Vector4(28f, 28f, 28f, 28f));
            Sprite button = Sliced("button", new Vector4(24f, 32f, 24f, 24f));
            Sprite bar = Sliced("bar", new Vector4(24f, 0f, 24f, 0f));
            Sprite glass = Sliced("glass", new Vector4(24f, 24f, 24f, 24f));
            Texture2D cursor = Cursor("cursor");

            prototype.SetUiSkin(panel, button, bar, glass, cursor);
            EditorUtility.SetDirty(prototype);
            EditorSceneManager.MarkSceneDirty(prototype.gameObject.scene);
            EditorSceneManager.SaveScene(prototype.gameObject.scene);
            Debug.Log("[Cooking] UI skin assigned" + (panel == null ? " (pieces missing from " + Folder + ")." : "."));
        }

        private static Sprite Sliced(string name, Vector4 border)
        {
            string path = Folder + "/" + name + ".png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer == null) return null;

            if(importer.textureType != TextureImporterType.Sprite || importer.spriteBorder != border || importer.mipmapEnabled)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spriteBorder = border;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                TextureImporterSettings settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                // Sliced images need the full rectangle, not a tight outline.
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        private static Texture2D Cursor(string name)
        {
            string path = Folder + "/" + name + ".png";
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if(importer == null) return null;

            if(importer.textureType != TextureImporterType.Cursor)
            {
                importer.textureType = TextureImporterType.Cursor;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
