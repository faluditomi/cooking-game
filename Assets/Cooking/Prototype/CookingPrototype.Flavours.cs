using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The tablet's Flavour Profiles tab. It shows the tasty zones one at a time, each in its own
    /// colour, with arrows to cycle through them and a box to show that zone on the Current Order's
    /// radar too. When a regular gets their dream dish, their own taste joins the list
    /// ("Hera's Taste").
    /// </summary>
    public partial class CookingPrototype
    {
        private class TasteProfile
        {
            public FlavourZone Zone;
            public string Name;
            public Color Color;
            public bool OnOrder;
        }

        /// <summary>The colour the tasty zones are drawn in where they aren't told apart.</summary>
        private static readonly Color TastyZoneBlue = FlavourRadarGraphic.DefaultZoneColor;

        private static readonly Color[] TastePalette =
        {
            new Color(0.3f, 0.55f, 0.95f, 1f),
            new Color(0.4f, 0.8f, 0.45f, 1f),
            new Color(0.72f, 0.45f, 0.95f, 1f),
            new Color(0.95f, 0.8f, 0.3f, 1f),
            new Color(0.3f, 0.82f, 0.82f, 1f),
            new Color(0.95f, 0.45f, 0.6f, 1f),
        };

        private readonly List<TasteProfile> tasteProfiles = new List<TasteProfile>();
        private int tasteIndex;
        private GameObject flavoursPage;
        private Text tasteName;
        private Text tasteCount;
        private Text tasteToggleText;
        private FlavourRadarGraphic tasteRadar;

        /// <summary>The default tasty zones, before anyone's own taste is found.</summary>
        private void ResetTasteProfiles()
        {
            tasteProfiles.Clear();
            tasteIndex = 0;

            foreach(FlavourZone zone in zones)
            {
                if(zone == null) continue;
                tasteProfiles.Add(new TasteProfile { Zone = zone, Name = zone.DisplayName, Color = TastePalette[tasteProfiles.Count % TastePalette.Length] });
            }
        }

        /// <summary>Their favourite flavour joins the Flavour Profiles tab, once they've had their dream dish.</summary>
        private void UnlockTaste(Customer regular)
        {
            if(regular == null || regular.secretDesire == null || tasteProfiles.Exists(p => p.Zone == regular.secretDesire)) return;

            tasteProfiles.Add(new TasteProfile
            {
                Zone = regular.secretDesire,
                Name = regular.name + "'s Taste",
                Color = TastePalette[tasteProfiles.Count % TastePalette.Length],
            });
            progressToday.Add(regular.name + "'s Taste is in your Flavour Profiles.");
        }

        /// <summary>Whether a recipe tastes like the current customer's favourite flavour.</summary>
        private bool InFavourite(RecipeDefinition recipe)
        {
            if(recipe == null || customer == null || customer.secretDesire == null) return false;
            return FlavourEvaluator.Score(recipe.CalculateProfile(rules), customer.secretDesire, Falloff) >= DishJudge.Settings.Default.ZoneAt;
        }

        private void StepTaste(int step)
        {
            if(tasteProfiles.Count == 0) return;
            tasteIndex = (tasteIndex + step + tasteProfiles.Count) % tasteProfiles.Count;
            RefreshFlavours();
        }

        private void ToggleTasteOnOrder()
        {
            if(tasteProfiles.Count == 0) return;
            TasteProfile profile = tasteProfiles[tasteIndex];
            profile.OnOrder = !profile.OnOrder;
            RefreshAll();
        }

        private void RefreshFlavours()
        {
            if(flavoursPage == null || !flavoursPage.activeSelf) return;

            tasteRadar.ClearProfiles();
            tasteRadar.SetZones(null);

            if(tasteProfiles.Count == 0)
            {
                tasteName.text = "No flavour profiles.";
                tasteCount.text = "";
                tasteToggleText.text = "";
                return;
            }

            tasteIndex = Mathf.Clamp(tasteIndex, 0, tasteProfiles.Count - 1);
            TasteProfile profile = tasteProfiles[tasteIndex];
            tasteName.text = profile.Name;
            tasteName.color = profile.Color;
            tasteCount.text = (tasteIndex + 1) + " / " + tasteProfiles.Count;
            tasteToggleText.text = (profile.OnOrder ? "[x]" : "[  ]") + "  Display on Current Order tab";
            tasteRadar.AddZone(profile.Zone, profile.Color);
        }

        /// <summary>The Current Order radar: the zones ticked on the Flavour Profiles tab, in their colours.</summary>
        private void ApplyOrderZones()
        {
            if(plateRadar == null) return;
            plateRadar.SetZones(null);

            // The tasty zone the order belongs to, in blue: the target to cook towards.
            if(orderIn && orderKnown && order != null)
            {
                FlavourZoneMatch home = FlavourEvaluator.FindBestZone(orderProfile, zones, Falloff);
                if(home.Zone != null && home.Score >= DishJudge.Settings.Default.ZoneAt) plateRadar.AddZone(home.Zone, TastyZoneBlue);
            }

            foreach(TasteProfile profile in tasteProfiles) if(profile.OnOrder) plateRadar.AddZone(profile.Zone, profile.Color);
        }

        private void BuildFlavoursPage(Transform pages)
        {
            Image page = UIFactory.Panel(pages, "Flavour Profiles", UIFactory.PanelColor);
            UIFactory.Stretch(page.rectTransform, 4f);
            flavoursPage = page.gameObject;
            VerticalLayoutGroup layout = UIFactory.Vertical(page.gameObject, 4f, 8);
            layout.childForceExpandHeight = false;

            RectTransform header = UIFactory.Row(page.transform, 32f, 6f);
            header.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
            Button previous = UIFactory.Button(header, "<", () => StepTaste(-1), null, 18);
            UIFactory.Size(previous, 32f, 0f, 40f, 0f);
            tasteName = UIFactory.Label(header, "", 17, TextAnchor.MiddleCenter, FontStyle.Bold);
            UIFactory.Size(tasteName, 32f, 0f, -1f, 1f);
            UIFactory.FitText(tasteName);
            Button next = UIFactory.Button(header, ">", () => StepTaste(1), null, 18);
            UIFactory.Size(next, 32f, 0f, 40f, 0f);

            tasteCount = UIFactory.Label(page.transform, "", 11, TextAnchor.MiddleCenter, FontStyle.Normal, UIFactory.MutedText);
            UIFactory.Size(tasteCount, 14f, 0f);

            RectTransform radarArea = UIFactory.Rect(page.transform, "Radar");
            UIFactory.Size(radarArea, 100f, 1f);
            tasteRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            tasteRadar.raycastTarget = false;
            tasteRadar.SetLabelMargin(28f);
            tasteRadar.CreateLabels(UIFactory.Font, 11, UIFactory.MutedText);

            RectTransform toggleRow = UIFactory.Row(page.transform, 30f);
            Button toggle = UIFactory.Button(toggleRow, "", ToggleTasteOnOrder, UIFactory.ButtonColor, 13);
            tasteToggleText = toggle.GetComponentInChildren<Text>();

            flavoursPage.SetActive(false);
        }
    }
}
