using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cooking.Prototype
{
    /// <summary>
    /// The screen from the Miro mock-up. The conversation runs down the left at full height. On the
    /// right, the market and the customer are on top (with the prep tools hanging from the stall roof)
    /// and the workbench is below. The tablet is built into the workbench and everything on it fits
    /// there: the Current Order, Menu, History and Flavour Profiles tabs, and the Dish Served and End
    /// of Cycle pages (which hide the tabs). Shopping happens at the market stall in the morning.
    /// </summary>
    public partial class CookingPrototype
    {
        /// <summary>Share of the screen width the conversation takes on the left.</summary>
        private const float ConversationWidth = 0.22f;
        /// <summary>Share of the screen height the workbench takes at the bottom right.</summary>
        private const float DeskShare = 0.56f;
        private const float TabletBarHeight = 30f;
        private static readonly Color Orange = new Color(0.75f, 0.42f, 0.15f, 1f);

        private enum TabletPage
        {
            Order,
            Recipes,
            History,
            Flavours,
        }

        [Header("Market art")]
        [Tooltip("Ads on the market billboards behind the customer, picked at random each cycle.")]
        [SerializeField] private List<Sprite> billboardAds = new List<Sprite>();
        [Tooltip("The billboard frame the ads sit in (134 × 70 pixels, the ad 4 pixels in).")]
        [SerializeField] private Sprite billboardFrame;

        [Header("Cookware art")]
        [Tooltip("Rendered from the CC0 3D Cooking Assets pack by Custom Tools → Cooking Prototype → Render Cookware Sprites.")]
        [SerializeField] private Sprite knifeSprite;
        [SerializeField] private Sprite peelerSprite;
        [SerializeField] private Sprite graterSprite;
        [SerializeField] private Sprite panSprite;
        [SerializeField] private Sprite potSprite;
        [SerializeField] private Sprite boardSprite;
        [SerializeField] private Sprite plateSprite;
        [SerializeField] private Sprite spoonSprite;

        [Header("Labels")]
        [Tooltip("Pantry and desk ingredients show \"x2 Pak Choi\" under them. Off: the name shows only above what's held. Also switchable on the title screen.")]
        [SerializeField] private bool ingredientLabels;

        /// <summary>When a warning toast over the desk hides again (unscaled time).</summary>
        private float toastUntil;

        [Header("UI skin")]
        [Tooltip("Kenney's UI Pack: Sci-fi (CC0), from Assets/Art Assets/UI Skin. Set by Custom Tools → Cooking Prototype → Assign UI Skin. Empty keeps the flat look.")]
        [SerializeField] private Sprite skinPanel;
        [SerializeField] private Sprite skinButton;
        [SerializeField] private Sprite skinBar;
        [SerializeField] private Sprite skinGlass;
        [SerializeField] private Texture2D skinCursor;

        private RectTransform canvasRect;
        private RectTransform deskRect;

        // Tablet
        private RectTransform tablet;
        private RectTransform tabletPages;
        private TabletPage tabletPage = TabletPage.Order;
        private bool? tabletShownSpecial;
        private GameObject orderPage;
        private RectTransform tabletBar;
        private readonly Dictionary<TabletPage, Button> tabletTabs = new Dictionary<TabletPage, Button>();

        // Market
        private GameObject npcFigure;
        private Image npcBody;
        private Image npcHead;
        private Text npcName;
        private Image npcPortrait;
        private Text marketHint;
        private readonly List<Image> billboardSlots = new List<Image>();

        /// <summary>Sets the market art. Used by the editor setup menu.</summary>
        public void SetMarketArt(IEnumerable<Sprite> ads, Sprite frame)
        {
            billboardAds = new List<Sprite>(ads);
            billboardFrame = frame;
        }

        /// <summary>Sets the UI skin. Used by the editor's Assign UI Skin menu.</summary>
        public void SetUiSkin(Sprite panel, Sprite button, Sprite bar, Sprite glass, Texture2D cursor)
        {
            skinPanel = panel;
            skinButton = button;
            skinBar = bar;
            skinGlass = glass;
            skinCursor = cursor;
        }

        /// <summary>Hands the skin to the UI factory (before the UI is built) and sets the pointer.</summary>
        private void ApplySkin()
        {
            UIFactory.PanelSprite = skinPanel;
            UIFactory.ButtonSprite = skinButton;
            UIFactory.BarSprite = skinBar;
            UIFactory.GlassSprite = skinGlass;
            if(skinCursor != null) Cursor.SetCursor(skinCursor, new Vector2(3f, 3f), CursorMode.Auto);
        }

        /// <summary>Sets the cookware sprites. Used by the editor's Render Cookware Sprites menu.</summary>
        public void SetCookwareArt(Sprite knife, Sprite peeler, Sprite grater, Sprite pan, Sprite pot, Sprite board, Sprite plateArt, Sprite spoon)
        {
            spoonSprite = spoon;
            knifeSprite = knife;
            peelerSprite = peeler;
            graterSprite = grater;
            panSprite = pan;
            potSprite = pot;
            boardSprite = board;
            plateSprite = plateArt;
        }

        // ------------------------------------------------------------------ Tablet

        private bool RecipeBookOpen => recipeBookOverlay != null && (tabletPage == TabletPage.Recipes || tabletPage == TabletPage.History)
                                       && !TabletSpecial;

        /// <summary>The result or end-of-cycle page is up; the tabs wait until it's dismissed.</summary>
        private bool TabletSpecial => (resultOverlay != null && resultOverlay.activeSelf) || (summaryOverlay != null && summaryOverlay.activeSelf);

        private void ShowTabletPage(TabletPage page)
        {
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
            RefreshAll();
        }

        private void RefreshTablet()
        {
            if(tablet == null) return;

            bool special = TabletSpecial;
            orderPage.SetActive(!special && tabletPage == TabletPage.Order);
            recipeBookOverlay.SetActive(!special && (tabletPage == TabletPage.Recipes || tabletPage == TabletPage.History));
            flavoursPage.SetActive(!special && tabletPage == TabletPage.Flavours);

            foreach(KeyValuePair<TabletPage, Button> pair in tabletTabs)
            {
                pair.Value.GetComponent<Image>().color = pair.Key == tabletPage && !special ? Orange : UIFactory.ButtonColor;
            }

            SyncTablet();
        }

        /// <summary>The Dish Served and End of Cycle pages take the whole screen, with no tabs. Cheap when nothing changed.</summary>
        private void SyncTablet()
        {
            if(tablet == null) return;

            bool special = TabletSpecial;
            if(tabletShownSpecial == special) return;

            tabletShownSpecial = special;
            tabletBar.gameObject.SetActive(!special);
            tabletPages.offsetMax = new Vector2(0f, special ? 0f : -TabletBarHeight);
            if(!special) RefreshTablet();
        }

        private void LateUpdate()
        {
            if(messageText != null && messageText.transform.parent.gameObject.activeSelf && Time.unscaledTime > toastUntil)
            {
                messageText.transform.parent.gameObject.SetActive(false);
            }

            SyncTablet();
            UpdateTutorialHighlight();
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
            BuildStoreScene(root);
            BuildToast(root);
            BuildTutorial(root);
            BuildTitle(root);
            BuildPause(root);
        }

        /// <summary>Left, full height: who's at the counter (just their name) and what they say, each new line below the last.</summary>
        private void BuildConversation(Transform root)
        {
            RectTransform panel = UIFactory.Panel(root, "Conversation", UIFactory.PanelDark).rectTransform;
            UIFactory.Place(panel, 0f, 0f, ConversationWidth, 1f);
            VerticalLayoutGroup layout = UIFactory.Vertical(panel.gameObject, 12f, 22);
            layout.childForceExpandHeight = false;

            sidebarCustomer = UIFactory.Label(panel, "", 30, TextAnchor.UpperLeft, FontStyle.Bold, UIFactory.Accent);
            UIFactory.Size(sidebarCustomer, 40f, 0f);

            // Each line goes below the last; once the box is full, older ones scroll off the top.
            RectTransform speechBox = UIFactory.Rect(panel, "Speech");
            UIFactory.Size(speechBox, 80f, 1f);
            speechBox.gameObject.AddComponent<RectMask2D>();
            sidebarSpeech = UIFactory.Label(speechBox, "", 21, TextAnchor.UpperLeft, FontStyle.Italic);
            RectTransform speechRect = sidebarSpeech.rectTransform;
            speechRect.anchorMin = new Vector2(0f, 1f);
            speechRect.anchorMax = new Vector2(1f, 1f);
            speechRect.pivot = new Vector2(0.5f, 1f);
            speechRect.sizeDelta = Vector2.zero;
        }

        /// <summary>A short warning over the desk when something can't be done (it fades on its own).</summary>
        private void BuildToast(Transform root)
        {
            Image back = UIFactory.Panel(root, "Toast", new Color(0.12f, 0.06f, 0.06f, 0.92f));
            back.raycastTarget = false;
            RectTransform rt = back.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(ConversationWidth + (1f - ConversationWidth) * 0.42f, DeskShare);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(760f, 46f);
            rt.anchoredPosition = new Vector2(0f, -14f);
            messageText = UIFactory.Label(rt, "", 17, TextAnchor.MiddleCenter);
            UIFactory.Stretch(messageText.rectTransform, 8f);
            UIFactory.FitText(messageText);
            back.gameObject.SetActive(false);
        }

        /// <summary>Shows a warning in the toast for a few seconds.</summary>
        private void ShowToast(string text, Color color)
        {
            if(messageText == null) return;
            messageText.text = text;
            messageText.color = color;
            messageText.transform.parent.gameObject.SetActive(true);
            messageText.transform.parent.SetAsLastSibling();
            toastUntil = Time.unscaledTime + 3f;
        }

        /// <summary>Top right: the night market, billboards, the customer, and the prep tools on the stall roof.</summary>
        private void BuildMarket(Transform root)
        {
            RectTransform market = UIFactory.Panel(root, "Market", new Color(0.1f, 0.09f, 0.17f, 1f)).rectTransform;
            UIFactory.Place(market, ConversationWidth, DeskShare, 1f, 1f);
            market.gameObject.AddComponent<RectMask2D>();

            // The market in the distance, blurred, behind everything.
            Image backdrop = UIFactory.Panel(market, "Market Backdrop", Color.white);
            backdrop.sprite = MarketBackdrop.Create();
            backdrop.raycastTarget = false;
            UIFactory.Stretch(backdrop.rectTransform);

            // No ads (demo feedback): just the market.

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

            // A regular with art stands here instead of the placeholder: close to the counter, seen
            // from the hips up as in the Miro mock-up (the picture runs on below the workbench).
            npcPortrait = UIFactory.Panel(market, "Portrait", Color.white);
            npcPortrait.preserveAspect = true;
            npcPortrait.raycastTarget = false;
            UIFactory.Place(npcPortrait.rectTransform, 0f, -0.82f, 0.32f, 0.97f);
            npcPortrait.rectTransform.pivot = new Vector2(0.5f, 0f);
            npcPortrait.gameObject.SetActive(false);

            npcName = UIFactory.Label(market, "", 22, TextAnchor.LowerLeft, FontStyle.Bold, UIFactory.TextColor);
            UIFactory.Place(npcName.rectTransform, 0.2f, 0.04f, 0.5f, 0.3f);

            marketHint = UIFactory.Label(market, "", 20, TextAnchor.MiddleCenter, FontStyle.Italic, UIFactory.MutedText);
            UIFactory.Place(marketHint.rectTransform, 0.3f, 0.02f, 0.98f, 0.18f);

            BuildToolRail(market);
        }

        private void AddBillboard(RectTransform market, Vector2 anchor, float scale)
        {
            Image frame = UIFactory.Panel(market, "Billboard", billboardFrame != null ? new Color(0.75f, 0.72f, 0.8f, 0.85f) : new Color(0.2f, 0.2f, 0.25f, 1f));
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
                // Dimmed: they're across the street.
                slot.color = ad != null ? new Color(0.78f, 0.74f, 0.82f, 0.9f) : new Color(0.3f, 0.25f, 0.4f, 1f);
            }
        }

        private void RefreshMarket()
        {
            if(npcFigure == null) return;

            bool here = phase == Phase.Service && customer != null;
            Sprite portrait = here ? customer.portrait : null;
            npcFigure.SetActive(here && portrait == null);
            npcPortrait.gameObject.SetActive(portrait != null);
            npcPortrait.sprite = portrait;

            if(here)
            {
                float hue = (customer.name.GetHashCode() & 0xFFFF) / 65535f;
                Color color = Color.HSVToRGB(hue, 0.4f, 0.65f);
                npcBody.color = color;
                npcHead.color = Color.Lerp(color, Color.white, 0.35f);
                npcName.text = "";
            }
            else
            {
                npcName.text = "";
            }

            marketHint.text = phase == Phase.DayOver ? "The market is closing for the night." : "";
        }

        /// <summary>The tablet, in the workbench: a bezel, a tab bar, and the pages (order, menu, history, flavour profiles, result, summary).</summary>
        private void BuildTablet(Transform root)
        {
            Image bezel = UIFactory.Panel(root, "Tablet", new Color(0.04f, 0.04f, 0.05f, 1f));
            UIFactory.SkinGlass(bezel, new Color(0.16f, 0.17f, 0.21f, 1f));
            tablet = bezel.rectTransform;
            // Where the mock-up has it: bottom left of the workbench.
            float width = 1f - ConversationWidth;
            UIFactory.Place(tablet, ConversationWidth + width * 0.012f, DeskShare * 0.03f, ConversationWidth + width * 0.385f, DeskShare * 0.62f);

            RectTransform screen = UIFactory.Panel(tablet, "Screen", UIFactory.Background).rectTransform;
            UIFactory.Stretch(screen, 8f);

            tabletBar = UIFactory.Panel(screen, "Tabs", UIFactory.PanelDark).rectTransform;
            tabletBar.anchorMin = new Vector2(0f, 1f);
            tabletBar.anchorMax = Vector2.one;
            tabletBar.offsetMin = new Vector2(0f, -TabletBarHeight);
            tabletBar.offsetMax = Vector2.zero;
            HorizontalLayoutGroup barLayout = UIFactory.Horizontal(tabletBar.gameObject, 3f, 3);
            barLayout.childForceExpandWidth = false;

            tabletTabs[TabletPage.Order] = TabletTab(tabletBar, "Current Order", TabletPage.Order, 104f);
            tabletTabs[TabletPage.Recipes] = TabletTab(tabletBar, "Menu", TabletPage.Recipes, 60f);
            tabletTabs[TabletPage.History] = TabletTab(tabletBar, "History", TabletPage.History, 68f);
            tabletTabs[TabletPage.Flavours] = TabletTab(tabletBar, "Flavour Profiles", TabletPage.Flavours, 116f);

            tabletPages = UIFactory.Rect(screen, "Pages");
            UIFactory.Stretch(tabletPages);
            tabletPages.offsetMax = new Vector2(0f, -TabletBarHeight);

            RectTransform orderRoot = UIFactory.Rect(tabletPages, "Order");
            UIFactory.Stretch(orderRoot);
            orderPage = orderRoot.gameObject;
            BuildOrderPage(orderRoot);

            BuildRecipeBook(tabletPages);
            BuildFlavoursPage(tabletPages);
            BuildResultOverlay(tabletPages);
            BuildSummaryOverlay(tabletPages);
            SyncTablet();
        }

        private Button TabletTab(RectTransform bar, string label, TabletPage page, float width)
        {
            Button tab = UIFactory.Button(bar, label, () => ShowTabletPage(page), null, 12);
            UIFactory.Size(tab, -1f, -1f, width, 0f);
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
            UIFactory.SkinBar(timerBack, new Color(0.2f, 0.2f, 0.23f, 1f));
            UIFactory.Size(timerBack, 12f, 0f);
            ticket.TimerBar = timerBack.gameObject;
            ticket.TimerFillImage = UIFactory.Panel(timerBack.transform, "Fill", UIFactory.Good);
            UIFactory.SkinBar(ticket.TimerFillImage, UIFactory.Good);
            ticket.TimerFill = ticket.TimerFillImage.rectTransform;
            UIFactory.Place(ticket.TimerFill, 0f, 0f, 1f, 1f);
            RectTransform timerRow = UIFactory.Row(left, 22f, 6f);
            ticket.Timer = UIFactory.Label(timerRow, "", 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            dayText = UIFactory.Label(timerRow, "", 15, TextAnchor.MiddleCenter, FontStyle.Normal, UIFactory.MutedText);
            moneyText = UIFactory.Label(timerRow, "", 18, TextAnchor.MiddleRight, FontStyle.Bold, UIFactory.Good);

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
            // Your dish and, when you know it, the order; plus any zones ticked on the Flavour Profiles tab.
            plateRadar.SetLabelMargin(30f);
            plateRadar.CreateLabels(UIFactory.Font, 11, UIFactory.MutedText);

        }
    }
}
