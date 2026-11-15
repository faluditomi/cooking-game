using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Cooking.EditorTools
{
    /// <summary>Default inspector plus a radar preview of the zone's band.</summary>
    [CustomEditor(typeof(FlavourZone))]
    public class FlavourZoneEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            FlavourRadarGUI.Layout(260f, new[] { (FlavourZone)target }, null);
        }
    }

    /// <summary>Default inspector plus a radar preview of one unit, cooked and raw.</summary>
    [CustomEditor(typeof(IngredientDefinition))]
    public class IngredientDefinitionEditor : Editor
    {
        private static readonly Color CookedColor = new Color(0.35f, 0.8f, 1f, 1f);
        private static readonly Color RawColor = new Color(1f, 0.45f, 0.45f, 0.9f);

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            IngredientDefinition ingredient = (IngredientDefinition)target;
            FlavourProfile baseProfile = ingredient.Flavour.Clamped();
            FlavourProfile rawProfile = (ingredient.Flavour + ingredient.RawDelta).Clamped();

            List<(FlavourProfile, Color)> profiles = new List<(FlavourProfile, Color)> { (baseProfile, CookedColor) };
            bool hasRawDelta = !ingredient.RawDelta.Equals(FlavourProfile.Zero);
            if(hasRawDelta) profiles.Add((rawProfile, RawColor));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(hasRawDelta ? "Blue: base (cooked)   Red: raw" : "Base flavour (one unit, unprocessed)", EditorStyles.miniLabel);
            FlavourRadarGUI.Layout(240f, null, profiles);
        }
    }
}
