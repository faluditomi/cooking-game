using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The screen from the Miro mock-up. The conversation runs down the left at full height. On the
    /// right, the market and the customer are on top (with the prep tools hanging from the stall roof)
    /// and the workbench is below. The tablet is built into the workbench: small, it shows the order;
    /// it opens big over the right side for the shop, recipes, history, and the result and end-of-cycle
    /// pages.
    /// </summary>
    public partial class CookingPrototype
    {
        /// <summary>Share of the screen width the conversation takes on the left.</summary>
        private const float ConversationWidth = 0.26f;
        /// <summary>Share of the screen height the workbench takes at the bottom right.</summary>
        private const float DeskShare = 0.53f;
        private const float TabletBarHeight = 42f;
        private static readonly Color Orange = new Color(0.75f, 0.42f, 0.15f, 1f);

        private enum TabletPage
        {
            Order,
            Recipes,
            History,
            Shop,
        }

        [Header("Market art")]
        [Tooltip("Ads on the market billboards behind the customer, picked at random each cycle.")]
        [SerializeField] private List<Sprite> billboardAds = new List<Sprite>();
        [Tooltip("The billboard frame the ads sit in (134 × 70 pixels, the ad 4 pixels in).")]
        [SerializeField] private Sprite billboardFrame;

        private RectTransform canvasRect;
        private RectTransform deskRect;

        // Tablet
        private RectTransform tablet;
        private RectTransform tabletPages;
        private TabletPage tabletPage = TabletPage.Order;
        private bool tabletBig;
        private bool? tabletShownBig;
        private bool? tabletShownSpecial;
        private GameObject orderPage;
        private GameObject shopPage;
        private readonly Dictionary<TabletPage, Button> tabletTabs = new Dictionary<TabletPage, Button>();
        private Button tabletSizeButton;

        // Market
        private GameObject npcFigure;
        private Image npcBody;
        private Image npcHead;
        private Text npcName;
        private Text marketHint;
        private readonly List<Image> billboardSlots = new List<Image>();

        /// <summary>Sets the market art. Used by the editor setup menu.</summary>
        public void SetMarketArt(IEnumerable<Sprite> ads, Sprite frame)
        {
            billboardAds = new List<Sprite>(ads);
            billboardFrame = frame;
        }

        // ------------------------------------------------------------------ Tablet

        private bool RecipeBookOpen => recipeBookOverlay != null && (tabletPage == TabletPage.Recipes || tabletPage == TabletPage.History)
                                       && !TabletSpecial;

        /// <summary>The result or end-of-cycle page is up; the tabs wait until it's dismissed.</summary>
        private bool TabletSpecial => (resultOverlay != null && resultOverlay.activeSelf) || (summaryOverlay != null && summaryOverlay.activeSelf);

        private void ShowTabletPage(TabletPage page)
        {
            if(page == TabletPage.Shop && phase != Phase.Morning)
            {
                Say("The market only sells in the morning.", UIFactory.Bad);
                return;
            }

            tabletPage = page;

            if(page == TabletPage.Recipes)
            {
                // Open on the dish being cooked, if it's on the menu.
                List<RecipeDefinition> known = VisibleMenu();
                if(orderIn && order != null && known.Contains(order)) bookRecipe = order;
                else if(bookRecipe == null || !known.Contains(bookRecipe)) bookRecipe = known.Count > 0 ? known[0] : null;
            }

            if(page == TabletPage.Recipes) bookPage = BookPage.Recipes;
            if(page == TabletPage.History) bookPage = BookPage.History;

            // The order fits the small screen; everything else opens it big.
            tabletBig = page != TabletPage.Order;
            RefreshAll();
        }

        private void ToggleTabletSize()
        {
            tabletBig = !tabletBig;
            // Small, the tablet shows the order.
            if(!tabletBig) tabletPage = TabletPage.Order;
            RefreshAll();
        }

        private void RefreshTablet()
        {
            if(tablet == null) return;

            bool special = TabletSpecial;
            orderPage.SetActive(!special && tabletPage == TabletPage.Order);
            shopPage.SetActive(!special && tabletPage == TabletPage.Shop);
            recipeBookOverlay.SetActive(!special && (tabletPage == TabletPage.Recipes || tabletPage == TabletPage.History));

            foreach(KeyValuePair<TabletPage, Button> pair in tabletTabs)
            {
                pair.Value.GetComponent<Image>().color = pair.Key == tabletPage && !special ? Orange : UIFactory.ButtonColor;
            }

            tabletTabs[TabletPage.Shop].gameObject.SetActive(phase == Phase.Morning);
            UIFactory.SetButtonText(tabletSizeButton, tabletBig ? "Shrink" : "Expand");
            SyncTablet();
        }

        /// <summary>Puts the tablet in the workbench, or big over the right side, to match the state. Cheap when nothing changed.</summary>
        private void SyncTablet()
        {
            if(tablet == null) return;

            bool special = TabletSpecial;
            bool big = tabletBig || special;

            if(tabletShownBig != big)
            {
                tabletShownBig = big;
                float width = 1f - ConversationWidth;

                if(big)
                {
                    UIFactory.Place(tablet, ConversationWidth, 0f, 1f, 1f, 10f);
                }
                else
                {
                    // Where the mock-up has it: bottom left of the workbench.
                    UIFactory.Place(tablet, ConversationWidth + width * 0.015f, DeskShare * 0.04f, ConversationWidth + width * 0.4f, DeskShare * 0.59f);
                }
            }

            if(tabletShownSpecial != special)
            {
                tabletShownSpecial = special;
                foreach(Button tab in tabletTabs.Values) tab.interactable = !special;
                tabletSizeButton.interactable = !special;
                // The result and summary pages are only built for the big screen.
                if(!special) RefreshTablet();
            }
        }

        private void LateUpdate()
        {
            SyncTablet();
        }

        // ------------------------------------------------------------------ UI construction

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject("Prototype UI", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
            canvasRect = (RectTransform)canvasObject.transform;
            Transform root = canvasObject.transform;

            UIFactory.Stretch(UIFactory.Panel(root, "Background", UIFactory.Background).rectTransform);
            BuildConversation(root);
            BuildMarket(root);
            BuildDesk(root);
            BuildTablet(root);
            BuildTitle(root);
        }

        /// <summary>Left, full height: the stall, who's at the counter and what they say, and the latest hint.</summary>
        private void BuildConversation(Transform root)
        {
            RectTransform panel = UIFactory.Panel(root, "Conversation", UIFactory.PanelDark).rectTransform;
            UIFactory.Place(panel, 0f, 0f, ConversationWidth, 1f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 8f, 18);
            layout.childForceExpandHeight = false;

            Text stall = UIFactory.Label(panel, "Emphis's Stall", 26, TextAnchor.MiddleLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(stall, 34f, 0f);

            RectTransform stats = UIFactory.Row(panel, 34f, 8f);
            dayText = UIFactory.Label(stats, "", 19, TextAnchor.MiddleLeft);
            moneyText = UIFactory.Label(stats, "", 26, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.Good);

            Image divider = UIFactory.Panel(panel, "Divider", new Color(1f, 1f, 1f, 0.08f));
            UIFactory.Size(divider, 2f, 0f);

            Header(panel, "AT THE COUNTER");
            sidebarCustomer = UIFactory.Label(panel, "", 26, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);

            // The newest line sits at the bottom; older ones scroll off the top.
            RectTransform speechBox = UIFactory.Rect(panel, "Speech");
            UIFactory.Size(speechBox, 80f, 1f);
            speechBox.gameObject.AddComponent<RectMask2D>();
            sidebarSpeech = UIFactory.Label(speechBox, "", 20, TextAnchor.LowerLeft, FontStyle.Italic);
            UIFactory.Stretch(sidebarSpeech.rectTransform);

            Image messageBack = UIFactory.Panel(panel, "Hint", UIFactory.PanelColor);
            UIFactory.Size(messageBack, 92f, 0f);
            messageText = UIFactory.Label(messageBack.transform, "", 17, TextAnchor.MiddleLeft);
            UIFactory.Stretch(messageText.rectTransform, 10f);
            UIFactory.FitText(messageText);
        }

        /// <summary>Top right: the night market, billboards, the customer, and the prep tools on the stall roof.</summary>
        private void BuildMarket(Transform root)
        {
            RectTransform market = UIFactory.Panel(root, "Market", new Color(0.1f, 0.09f, 0.17f, 1f)).rectTransform;
            UIFactory.Place(market, ConversationWidth, DeskShare, 1f, 1f);
            market.gameObject.AddComponent<RectMask2D>();

            Image street = UIFactory.Panel(market, "Street", new Color(0.07f, 0.065f, 0.09f, 1f));
            UIFactory.Place(street.rectTransform, 0f, 0f, 1f, 0.24f);
            street.raycastTarget = false;

            AddBillboard(market, new Vector2(0.3f, 0.66f), 1.4f);
            AddBillboard(market, new Vector2(0.48f, 0.4f), 2.2f);
            AddBillboard(market, new Vector2(0.78f, 0.36f), 2.2f);
            ShuffleBillboards();

            // The customer: a placeholder figure, bottom left as in the mock-up.
            RectTransform figure = UIFactory.Rect(market, "Customer");
            UIFactory.Place(figure, 0.02f, 0f, 0.15f, 0.66f);
            npcFigure = figure.gameObject;
            npcBody = UIFactory.Panel(figure, "Body", Color.white);
            npcBody.sprite = UIFactory.Circle;
            npcBody.raycastTarget = false;
            UIFactory.Place(npcBody.rectTransform, 0.08f, -0.25f, 0.92f, 0.62f);
            npcHead = UIFactory.Panel(figure, "Head", Color.white);
            npcHead.sprite = UIFactory.Circle;
            npcHead.preserveAspect = true;
            npcHead.raycastTarget = false;
            UIFactory.Place(npcHead.rectTransform, 0.2f, 0.56f, 0.8f, 0.95f);

            npcName = UIFactory.Label(market, "", 22, TextAnchor.LowerLeft, FontStyle.Bold, UIFactory.TextColor);
            UIFactory.Place(npcName.rectTransform, 0.16f, 0.04f, 0.5f, 0.3f);

            marketHint = UIFactory.Label(market, "", 20, TextAnchor.MiddleCenter, FontStyle.Italic, UIFactory.MutedText);
            UIFactory.Place(marketHint.rectTransform, 0.3f, 0.02f, 0.98f, 0.18f);

            BuildToolRail(market);
        }

        private void AddBillboard(RectTransform market, Vector2 anchor, float scale)
        {
            Image frame = UIFactory.Panel(market, "Billboard", billboardFrame != null ? Color.white : new Color(0.2f, 0.2f, 0.25f, 1f));
            frame.sprite = billboardFrame;
            frame.raycastTarget = false;
            RectTransform rt = frame.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(134f, 70f) * scale;

            Image ad = UIFactory.Panel(rt, "Ad", Color.clear);
            ad.raycastTarget = false;
            UIFactory.Place(ad.rectTransform, 4f / 134f, 4f / 70f, 130f / 134f, 66f / 70f);
            billboardSlots.Add(ad);
        }

        /// <summary>New ads on the billboards, different ones where there are enough.</summary>
        private void ShuffleBillboards()
        {
            List<Sprite> ads = billboardAds.FindAll(sprite => sprite != null);

            foreach(Image slot in billboardSlots)
            {
                Sprite ad = null;

                if(ads.Count > 0)
                {
                    int index = Random.Range(0, ads.Count);
                    ad = ads[index];
                    if(ads.Count > 1) ads.RemoveAt(index);
                }

                slot.sprite = ad;
                slot.color = ad != null ? Color.white : new Color(0.3f, 0.25f, 0.4f, 1f);
            }
        }

        private void RefreshMarket()
        {
            if(npcFigure == null) return;

            bool here = phase == Phase.Service && customer != null;
            npcFigure.SetActive(here);

            if(here)
            {
                float hue = (customer.name.GetHashCode() & 0xFFFF) / 65535f;
                Color color = Color.HSVToRGB(hue, 0.4f, 0.65f);
                npcBody.color = color;
                npcHead.color = Color.Lerp(color, Color.white, 0.35f);
                npcName.text = customer.name + "\n<size=16><color=#a6a199>" + StageLabel(Relationship(customer).Stage) + "</color></size>";
            }
            else
            {
                npcName.text = "";
            }

            marketHint.text = phase == Phase.Morning ? "The market is waking up. Shop on the tablet, then start the day."
                : phase == Phase.DayOver ? "The market is closing for the night."
                : customer == null && arrivals.Count > 0 ? "Someone's coming..."
                : "";
        }

        /// <summary>The tablet: a bezel, a tab bar, and the pages (order, shop, recipe book, result, summary).</summary>
        private void BuildTablet(Transform root)
        {
            Image bezel = UIFactory.Panel(root, "Tablet", new Color(0.04f, 0.04f, 0.05f, 1f));
            tablet = bezel.rectTransform;

            RectTransform screen = UIFactory.Panel(tablet, "Screen", UIFactory.Background).rectTransform;
            UIFactory.Stretch(screen, 8f);

            RectTransform bar = UIFactory.Panel(screen, "Tabs", UIFactory.PanelDark).rectTransform;
            bar.anchorMin = new Vector2(0f, 1f);
            bar.anchorMax = Vector2.one;
            bar.offsetMin = new Vector2(0f, -TabletBarHeight);
            bar.offsetMax = Vector2.zero;
            HorizontalLayoutGroup barLayout = UIFactory.Horizontal(bar.gameObject, 4f, 4);
            barLayout.childForceExpandWidth = false;

            tabletTabs[TabletPage.Order] = TabletTab(bar, "Order", TabletPage.Order);
            tabletTabs[TabletPage.Recipes] = TabletTab(bar, "Recipes", TabletPage.Recipes);
            tabletTabs[TabletPage.History] = TabletTab(bar, "History", TabletPage.History);
            tabletTabs[TabletPage.Shop] = TabletTab(bar, "Shop", TabletPage.Shop);
            RectTransform spacer = UIFactory.Rect(bar, "Spacer");
            UIFactory.Size(spacer, -1f, -1f, 0f, 1f);
            tabletSizeButton = UIFactory.Button(bar, "Expand", ToggleTabletSize, null, 15);
            UIFactory.Size(tabletSizeButton, -1f, -1f, 84f, 0f);

            tabletPages = UIFactory.Rect(screen, "Pages");
            UIFactory.Stretch(tabletPages);
            tabletPages.offsetMax = new Vector2(0f, -TabletBarHeight);

            RectTransform orderRoot = UIFactory.Rect(tabletPages, "Order");
            UIFactory.Stretch(orderRoot);
            orderPage = orderRoot.gameObject;
            BuildOrderPage(orderRoot);

            RectTransform shop = UIFactory.Rect(tabletPages, "Shop");
            UIFactory.Stretch(shop, 8f);
            shopPage = shop.gameObject;
            BuildStoreArea(shop);

            BuildRecipeBook(tabletPages);
            BuildResultOverlay(tabletPages);
            BuildSummaryOverlay(tabletPages);
            SyncTablet();
        }

        private Button TabletTab(RectTransform bar, string label, TabletPage page)
        {
            Button tab = UIFactory.Button(bar, label, () => ShowTabletPage(page), null, 15);
            UIFactory.Size(tab, -1f, -1f, 92f, 0f);
            return tab;
        }

        /// <summary>The order: who it's for, the day timer, the recipe if you know it, and the flavour so far.</summary>
        private void BuildOrderPage(RectTransform page)
        {
            RectTransform left = UIFactory.Rect(page, "Ticket");
            UIFactory.Place(left, 0f, 0f, 0.56f, 1f);
            VerticalLayoutGroup layout = UIFactory.Vertical(left.gameObject, 4f, 10);
            layout.childForceExpandHeight = false;

            TicketView ticket = new TicketView();
            // The conversation shows who it is; the ticket keeps the label for its morning text.
            ticket.Customer = UIFactory.Label(left, "", 18, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            ticket.Customer.gameObject.SetActive(false);

            Image timerBack = UIFactory.Panel(left, "Timer", new Color(0.08f, 0.08f, 0.09f, 1f));
            UIFactory.Size(timerBack, 8f, 0f);
            ticket.TimerBar = timerBack.gameObject;
            ticket.TimerFillImage = UIFactory.Panel(timerBack.transform, "Fill", UIFactory.Good);
            ticket.TimerFill = ticket.TimerFillImage.rectTransform;
            UIFactory.Place(ticket.TimerFill, 0f, 0f, 1f, 1f);
            ticket.Timer = UIFactory.Label(left, "", 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            UIFactory.Size(ticket.Timer, 20f, 0f);

            ticket.Line = UIFactory.Label(left, "", 14, TextAnchor.UpperLeft, FontStyle.Italic);
            RectTransform scroll = UIFactory.ScrollList(left, 0f);
            ticket.Order = UIFactory.Label(scroll, "", 15);
            tickets.Add(ticket);

            RectTransform right = UIFactory.Rect(page, "Flavour");
            UIFactory.Place(right, 0.56f, 0f, 1f, 1f);
            VerticalLayoutGroup rightLayout = UIFactory.Vertical(right.gameObject, 4f, 8);
            rightLayout.childForceExpandHeight = false;

            RectTransform radarArea = UIFactory.Rect(right, "Radar");
            UIFactory.Size(radarArea, 120f, 1f);
            plateRadar = radarArea.gameObject.AddComponent<FlavourRadarGraphic>();
            plateRadar.raycastTarget = false;
            // No zones: just your dish and, when you know it, the order.
            plateRadar.CreateLabels(UIFactory.Font, 12, UIFactory.MutedText);

            Text legend = UIFactory.Label(right, "<color=#d9d9d9>Grey</color>: the order  <color=#f28c33>Orange</color>: your plate",
                12, TextAnchor.UpperLeft, FontStyle.Normal, UIFactory.MutedText);
            UIFactory.Size(legend, 16f, 0f);

            RectTransform previewRow = UIFactory.Row(right, 24f);
            previewButton = UIFactory.Button(previewRow, "", TogglePreview, null, 13);
        }
    }
}
