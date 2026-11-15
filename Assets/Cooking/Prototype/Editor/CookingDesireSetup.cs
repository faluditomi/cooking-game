using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Cooking.Prototype.EditorTools
{
    /// <summary>
    /// Creates each regular's secret desire zone, a flavour region no menu recipe reaches, so their
    /// dream meal has to be discovered. Existing zone assets are kept, so tune them in the Inspector.
    /// </summary>
    /// <remarks>
    /// Built around a dish from the design doc that isn't on the menu:
    /// The Woman: the sweet and sour (tofu) soup she made for her love.
    /// The Man: an honest, filling broth with something to chew and nothing synthetic (e.g. pak choi).
    /// The Sleeper: something fiery and savoury, not sweet: a parent's cooking for dull sensors.
    /// </remarks>
    public static class CookingDesireSetup
    {
        private const string Folder = "Assets/Cooking/Data/Zones/Desires";

        private struct Spec
        {
            public string Customer;
            public string Name;
            public string Description;
            public Color Color;
            public FlavourProfile Min;
            public FlavourProfile Max;
        }

        private static List<Spec> Specs()
        {
            return new List<Spec>
            {
                new Spec
                {
                    Customer = "The Woman", Name = "Their lost love's soup", Color = new Color(0.95f, 0.45f, 0.7f),
                    Description = "Sweet and sour, soft, a soup. What she used to make for the one who left.",
                    Min = new FlavourProfile { savoury = 1.5f, salty = 0f, sweet = 3.2f, bitter = 0f, sour = 2.3f, pain = 0f, nutrition = 2.5f, texture = 0f },
                    Max = new FlavourProfile { savoury = 5f, salty = 3f, sweet = 6f, bitter = 1.5f, sour = 5f, pain = 1.5f, nutrition = 6.5f, texture = 3f },
                },
                new Spec
                {
                    Customer = "The Man", Name = "An honest broth", Color = new Color(0.55f, 0.8f, 0.5f),
                    Description = "Filling, simple, a little to chew on, a touch of sweetness. Nothing synthetic.",
                    Min = new FlavourProfile { savoury = 2.8f, salty = 1.6f, sweet = 0.5f, bitter = 0f, sour = 0f, pain = 0f, nutrition = 4.8f, texture = 1.4f },
                    Max = new FlavourProfile { savoury = 5f, salty = 3.5f, sweet = 2f, bitter = 1.6f, sour = 0.8f, pain = 0.8f, nutrition = 7.5f, texture = 3.5f },
                },
                new Spec
                {
                    Customer = "The Sleeper", Name = "Mother's fire", Color = new Color(0.95f, 0.3f, 0.2f),
                    Description = "Hot enough to reach dull sensors, deeply savoury, not sweet.",
                    Min = new FlavourProfile { savoury = 5.5f, salty = 1.5f, sweet = 0f, bitter = 0f, sour = 0f, pain = 8f, nutrition = 3.5f, texture = 3f },
                    Max = new FlavourProfile { savoury = 10f, salty = 6f, sweet = 2.8f, bitter = 2f, sour = 1.5f, pain = 10f, nutrition = 8f, texture = 8f },
                },
            };
        }

        /// <summary>Desire zones by customer name, creating any that are missing.</summary>
        public static Dictionary<string, FlavourZone> EnsureDesireZones()
        {
            if(!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Cooking/Data/Zones", "Desires");

            Dictionary<string, FlavourZone> zones = new Dictionary<string, FlavourZone>();

            foreach(Spec spec in Specs())
            {
                string path = Folder + "/" + spec.Name.Replace("'", "") + ".asset";
                FlavourZone zone = AssetDatabase.LoadAssetAtPath<FlavourZone>(path);

                if(zone == null)
                {
                    zone = ScriptableObject.CreateInstance<FlavourZone>();
                    zone.Configure(spec.Name, spec.Min, spec.Max);
                    AssetDatabase.CreateAsset(zone, path);

                    SerializedObject so = new SerializedObject(zone);
                    so.FindProperty("color").colorValue = spec.Color;
                    so.FindProperty("description").stringValue = spec.Description;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }

                zones[spec.Customer] = zone;
            }

            AssetDatabase.SaveAssets();
            return zones;
        }

        [MenuItem("Custom Tools/Cooking Prototype/Build Desire Zones")]
        public static void BuildDesireZones()
        {
            Dictionary<string, FlavourZone> desires = EnsureDesireZones();
            CookingPrototype prototype = Object.FindAnyObjectByType<CookingPrototype>();

            if(prototype == null)
            {
                Debug.Log("[Cooking] Desire zones are in " + Folder + ". Open the prototype scene and run this again to assign them.");
                return;
            }

            SerializedObject so = new SerializedObject(prototype);
            SerializedProperty customers = so.FindProperty("customers");

            for(int i = 0; i < customers.arraySize; i++)
            {
                SerializedProperty entry = customers.GetArrayElementAtIndex(i);
                string name = entry.FindPropertyRelative("name").stringValue;
                if(desires.TryGetValue(name, out FlavourZone zone)) entry.FindPropertyRelative("secretDesire").objectReferenceValue = zone;
            }

            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(prototype.gameObject.scene);
            EditorSceneManager.SaveScene(prototype.gameObject.scene);
            Debug.Log("[Cooking] Desire zones in " + Folder + " assigned to the regulars in the open scene.");
        }
    }
}
