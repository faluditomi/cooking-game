using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cooking.EditorTools
{
    /// <summary>
    /// Sandbox for tuning the flavour system: build a dish from ingredient portions and see its
    /// profile, cost and zone scores update live on the radar.
    /// </summary>
    public class FlavourLabWindow : EditorWindow
    {
        private static readonly Color DishColor = new Color(0.35f, 0.8f, 1f, 1f);

        [SerializeField] private FlavourRules rules;
        [SerializeField] private List<IngredientPortion> portions = new List<IngredientPortion>();
        [SerializeField] private List<FlavourZone> zones = new List<FlavourZone>();
        [SerializeField] private bool showZonesOnChart = true;

        private SerializedObject serializedWindow;
        private Vector2 scroll;

        [MenuItem("Custom Tools/Flavour Lab")]
        public static void Open()
        {
            FlavourLabWindow window = GetWindow<FlavourLabWindow>();
            window.titleContent = new GUIContent("Flavour Lab");
            window.minSize = new Vector2(420f, 520f);
        }

        private void OnEnable()
        {
            serializedWindow = new SerializedObject(this);
            if(rules == null) rules = FindFirstAsset<FlavourRules>();
            if(zones.Count == 0) zones = FindAllAssets<FlavourZone>();
        }

        private void OnGUI()
        {
            if(serializedWindow == null || serializedWindow.targetObject == null) serializedWindow = new SerializedObject(this);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            serializedWindow.Update();

            EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(rules)));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dish", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Ingredient · qty · prep · cooking (Nothing = raw) · doneness", EditorStyles.miniLabel);
            EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(portions)), GUIContent.none, true);

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Add portion"))
                {
                    serializedWindow.ApplyModifiedProperties();
                    Undo.RecordObject(this, "Add portion");
                    portions.Add(new IngredientPortion());
                    serializedWindow.Update();
                }

                if(GUILayout.Button("Clear"))
                {
                    serializedWindow.ApplyModifiedProperties();
                    Undo.RecordObject(this, "Clear dish");
                    portions.Clear();
                    serializedWindow.Update();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Zones", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(zones)), true);

            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button("Load all zones in project"))
                {
                    serializedWindow.ApplyModifiedProperties();
                    Undo.RecordObject(this, "Load zones");
                    zones = FindAllAssets<FlavourZone>();
                    serializedWindow.Update();
                }

                EditorGUILayout.PropertyField(serializedWindow.FindProperty(nameof(showZonesOnChart)), new GUIContent("Show on chart"));
            }

            serializedWindow.ApplyModifiedProperties();

            DrawResults();

            EditorGUILayout.EndScrollView();
        }

        private void DrawResults()
        {
            FlavourProfile profile = FlavourCalculator.Calculate(portions, rules);
            float falloff = rules != null ? rules.zoneFalloff : FlavourRules.DefaultZoneFalloff;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Result", EditorStyles.boldLabel);

            FlavourRadarGUI.Layout(300f, showZonesOnChart ? zones : null, new List<(FlavourProfile, Color)> { (profile, DishColor) });

            using(new EditorGUI.DisabledScope(true))
            {
                foreach(FlavourAxis axis in FlavourAxes.All)
                {
                    EditorGUILayout.Slider(FlavourAxes.Label(axis), profile[axis], 0f, FlavourProfile.MaxValue);
                }
            }

            EditorGUILayout.LabelField("Ingredient cost", FlavourCalculator.CalculateIngredientCost(portions).ToString("0.##"));

            EditorGUILayout.Space();

            FlavourZoneMatch best = FlavourEvaluator.FindBestZone(profile, zones, falloff);
            string verdict = best.IsInside ? $"Inside {best.Zone.DisplayName}"
                : best.IsMatch ? $"Near {best.Zone.DisplayName} ({best.Score:P0})"
                : "Outside every zone";
            EditorGUILayout.HelpBox(verdict, best.IsInside ? MessageType.Info : best.IsMatch ? MessageType.None : MessageType.Warning);

            foreach(FlavourZone zone in zones)
            {
                if(zone == null) continue;

                float score = FlavourEvaluator.Score(profile, zone, falloff);
                Rect rect = EditorGUILayout.GetControlRect();
                EditorGUI.ProgressBar(rect, score, $"{zone.DisplayName}: {score:P0}");
            }
        }

        private static T FindFirstAsset<T>() where T : Object
        {
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            return guids.Length == 0 ? null : AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static List<T> FindAllAssets<T>() where T : Object
        {
            List<T> result = new List<T>();

            foreach(string guid in AssetDatabase.FindAssets("t:" + typeof(T).Name))
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));
                if(asset != null) result.Add(asset);
            }

            return result;
        }
    }
}
