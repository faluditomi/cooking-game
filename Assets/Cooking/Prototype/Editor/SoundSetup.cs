using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// Puts the synthesized sounds (Assets/Cooking/Audio, made by Audio/Source/synth_sounds.py) on the
    /// prototype in the open scene. Short effects load into memory; the loops stream.
    /// </summary>
    public static class SoundSetup
    {
        public const string Folder = "Assets/Cooking/Audio";

        [MenuItem("Custom Tools/Cooking Prototype/Assign Sounds")]
        public static void Assign()
        {
            CookingPrototype prototype = Object.FindAnyObjectByType<CookingPrototype>();

            if(prototype == null)
            {
                Debug.LogWarning("[Cooking] Open the prototype scene to assign the sounds.");
                return;
            }

            CookingPrototype.SoundSet set = new CookingPrototype.SoundSet
            {
                uiClick = Clip("ui_click"),
                pickup = Clip("pickup"),
                place = Clip("place"),
                plate = Clip("plate"),
                chop = Clip("chop"),
                peel = Clip("peel"),
                grate = Clip("grate"),
                prepDone = Clip("prep_done"),
                shake = Clip("shake"),
                stir = Clip("stir"),
                tooEarly = Clip("too_early"),
                burn = Clip("burn"),
                sizzle = Clip("sizzle_loop"),
                boil = Clip("boil_loop"),
                serve = Clip("serve"),
                coins = Clip("coins"),
                noPay = Clip("no_pay"),
                dream = Clip("dream"),
                arrive = Clip("arrive"),
                blip = Clip("blip"),
                register = Clip("register"),
                dayEnd = Clip("day_end"),
                ambience = Clip("ambience_loop"),
            };

            prototype.SetSounds(set);
            EditorUtility.SetDirty(prototype);
            EditorSceneManager.MarkSceneDirty(prototype.gameObject.scene);
            EditorSceneManager.SaveScene(prototype.gameObject.scene);
            Debug.Log("[Cooking] Sounds assigned" + (set.uiClick == null ? " (clips missing from " + Folder + "; run Audio/Source/synth_sounds.py)." : "."));
        }

        private static AudioClip Clip(string name)
        {
            string path = Folder + "/" + name + ".wav";
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if(importer == null) return null;

            bool loop = name.EndsWith("_loop");
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            AudioClipLoadType loadType = loop ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;

            if(settings.loadType != loadType || importer.forceToMono != true)
            {
                settings.loadType = loadType;
                settings.compressionFormat = AudioCompressionFormat.Vorbis;
                settings.quality = 0.7f;
                importer.defaultSampleSettings = settings;
                importer.forceToMono = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
