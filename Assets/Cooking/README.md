# Cooking: Flavour Profile system

Decides what a dish tastes like and whether it is any good. Implements the *Flavour Profile*
section of the design doc (Notion: The Washerwoman Potlatch → Mechanics → Flavour Profile).

## Concepts

| Type | Kind | What it is |
|---|---|---|
| `FlavourAxis` | enum | The 8 radar spokes: Savoury, Salty, Sweet, Bitter, Sour, Pain, Nutrition, Texture (0 mushy → 10 crunchy). |
| `FlavourProfile` | struct | One value per axis on a 0..10 scale. Used for ingredients, deltas, dishes and zone bounds. |
| `IngredientDefinition` | asset | One unit's flavour, its **bulk** (how much of a dish it fills), price, and an optional `rawDelta` for things that are bad uncooked. |
| `IngredientPortion` | data | Ingredient × quantity + prep (`Peeled`/`Sliced`/`Grated`, stackable) + cooking (`Boiled`/`Fried`, none = raw) + doneness. A dish is a list of these. |
| `FlavourRules` | asset | Global tuning: what each prep step, cooking method and doneness does, plus zone falloff. |
| `FlavourZone` | asset | A desirable region: min/max per axis. Drawn as a shaded band on the radar. |
| `RecipeDefinition` | asset | A menu dish: its portions (with prep and cooking), origin, perfect-cook profile, cost, and `Matches(plate)` for "is this exactly what they ordered". |
| `FlavourCalculator` | static | Portions → dish `FlavourProfile`, and ingredient cost. |
| `FlavourEvaluator` | static | Profile vs zones: score, best zone, shared zone. |

## The maths

Per portion, one unit's profile is:

```
unit = ingredient.flavour
     + rulesDelta(prep, cooking, doneness) × (bulk for taste axes, 1 for texture)
     + ingredient.rawDelta            (only when not cooked)
unit = max(unit, 0)
```

Then across the dish, with `w = bulk × quantity`:

```
taste axis   = Σ(unit × quantity) / Σw     // concentration
texture      = Σ(unit × w)        / Σw     // bulk-weighted average
dish         = clamp(result, 0, 10)
```

Why this shape:

- A bulk-1 ingredient on its own tastes exactly like its flavour values, so they're easy to author.
- Seasonings have low bulk (salt 0.1). They add a lot of taste without diluting the dish, and
  more mushrooms *do* dilute the salt.
- A dish isn't crunchier because it has more crunchy things in it, but because more of it is
  crunchy. That's why texture is averaged.
- Rules deltas are written for a full-size unit. Frying two pinches of sugar shouldn't add as
  much savoury as frying a steak, so taste deltas scale with bulk.

**Zone score:** `1` inside the zone, falling linearly to `0` at `zoneFalloff` radar units outside
it (Euclidean distance to the nearest point of the min/max box). With falloff 0, only inside counts.

## How the game should use it

| Design rule | Call |
|---|---|
| Show the dish's radar dot after serving | `FlavourCalculator.Calculate(portions, rules)` |
| Is it tasty at all? | `FlavourEvaluator.FindBestZone(dish, zones, rules.zoneFalloff)` |
| Unordered dish, but liked because it is in the same desirable range as the order | `FlavourEvaluator.FindSharedZone(orderedProfile, servedProfile, zones, falloff)` |
| Hits the NPC's secret desire | `FlavourEvaluator.Score(served, npcDesireZone, falloff)` |
| Menu price from ingredients | `FlavourCalculator.CalculateIngredientCost(portions)` × margin |

An NPC's secret desire is just another `FlavourZone` asset (for example, a tight sweet-and-sour
band for the woman). Thresholds like "how good is good enough to pay or tip" belong to the
customer/judging system, not here.

## Tools

- **Custom Tools → Flavour Lab**: build a dish from portions and watch its radar, values, cost
  and zone scores update live. All zones and the rules asset load automatically. This is the
  place to tune numbers.
- Ingredient and zone inspectors show a radar preview. `FlavourProfile` fields draw as a 4×2 grid.
- Create assets via **Create → Cooking → Ingredient / Flavour Zone / Flavour Rules**.

## Sample data (`Data/`)

All values are **placeholders** tuned so the Notion recipes behave sensibly:

Zones are shapes on the radar, and a dish is judged by where its **dot** lands (see "Dishes as
dots" below). The first four tasty zones are fitted around the menu, one dish family each; six more
fill the gaps in the chart, placed where random proper dishes (3 to 5 pantry ingredients, boiled or
fried) were landing outside every zone, so more of what the player invents can be tasty. With all
ten, about 5 in 6 such dishes land in a zone (half did with four), and almost none in two:

| Zone | Where on the chart | Menu dishes in it |
|---|---|---|
| Greasy Comfort | outer band on the nutrition-crunch side | Calorice, MSBeef noodles |
| Hearty Broth | inner band on the same side | Mushroom soup, Synthetic soup |
| Fire Bowl | towards pain | Hellspawn fried rice |
| Briny Broth | towards salty | Soup on the rock |
| Filling Stew | between Fire Bowl and the soups, close in | (boiled, filling dishes: mushroom, rice, chicken) |
| Crispy Fry | outside Greasy Comfort | (heavy fried dishes: beef, tofu, potatoes) |
| Umami Bomb | the far savoury edge | (MSG2-heavy dishes) |
| Sweet Tooth | towards sweet | (sweet, a little salty) |
| Bitter Greens | towards bitter | (pak choi and friends) |
| Tangy Kick | between sour and pain | (sour with a bite) |

A raw potato or a lone MSG2 cube lands in none of them; each regular's dream dish lands in their
own favourite (Hera's in the middle, Petor's near Hearty Broth, the Sleeper's inside Fire Bowl).

## Cooking prototype (`Prototype/`)

A playable loop to try the flavour system with. **Custom Tools → Cooking Prototype → Build Scene**
creates `Prototype/CookingPrototype.unity` and the design-doc recipes in `Data/Recipes/`. It keeps
recipe assets that already exist, so hand tuning survives a rebuild. Then press Play.

The game opens on a **title screen** (`CookingPrototype.Title.cs`): **Start** begins the game,
**Quit** closes it. A game over goes back to the title screen, and Start there begins a new game.

### Building

The build settings contain only `Prototype/CookingPrototype.unity`. Build from **Custom Tools →
Cooking Prototype**, into `Builds/` (git-ignored):

- **Build Windows** → `Builds/Windows/EmphisStall.exe`. It always runs full screen (it forces a
  full-screen window at startup, so a windowed launch isn't remembered). Zip the folder for itch.
- **Build WebGL** → `Builds/WebGL`. Needs the WebGL Build Support module for this Unity version
  (Unity Hub → Installs → ⚙ → Add modules). It's gzip-compressed with a decompression fallback, so it
  runs on itch.io without server setup; zip the folder's contents and upload as an HTML game
  (1280×720). There's no Quit button in the browser.

### The screen

The layout follows the "Screen layout mock-up" on the Miro board:

- **Conversation** (left, full height): just the customer's name and what they say, typed out,
  each new line below the last (once the box is full, older lines scroll off the top). It clears
  when the customer leaves. Warnings (can't afford the basket, the pot's full) show for a few seconds
  as a toast over the desk.
- **Market** (top right): the Bright Market in the distance, painted at startup and blurred
  (`MarketBackdrop`: towers with lit windows, neon, a row of stalls with lamps, strings of
  lanterns), with no ads, the
  customer close to the counter and seen from the hips up as in the Miro mock-up (no name or state
  under them), and the **tools** (knife, peeler, grater, spoon) hanging from the stall roof,
  unlabelled. The **II** button in the corner (or Esc) pauses.
- **Workbench** (bottom right), all press-and-hold drags:
  - **Pantry** (right): your stock (`Inventory`) in four columns, grouped into **Raw**,
    **Prepped** and **Seasoning** under taped paper labels like the stoves' (always there, even
    when a group is empty), with no panel behind it. Each pile shows its pieces (one unit shows one
    piece). Labels like "x2 Pak Choi" show **only when held**.
    Holding a pile takes **one** unit. Drop it on a cutting board, the pot, the pan, a plate
    (seasonings only, on a cooked dish) or free desk space. Dropped anywhere else, it goes back.
  - **Desk space**: no grid. Plates and loose ingredients go where you drop them, anywhere in the
    L-shaped free area (a band along the top, plus the block above the tablet), nudged to the
    nearest spot where they don't overlap. Hold a loose pile to take all of it.
  - **Clean plates**: an endless stack of outlined, shaded plates. Taking one needs room on the desk.
    An empty plate can go back on the stack; a plate with anything on it stays on the desk.
  - **Food delivery**: a dotted outline about 1.5x a plate; it pulses green while you hold a plate
    with food. Drop the plate on it to serve.
  - **Stoves**: the pot (**Boil**) and the pan (**Fry**) sit on two drawn stoves, labelled on taped
    paper. A small bar rides on each pot. They start cooking with the first thing in.
  - **Prep section**: two cutting boards, with no text on them.
  - **Tablet**, built into the table (bottom left), and everything fits in it (it never goes full
    screen). Tabs: **Current Order** (the timer, cycle and money at the top, the order card, and a
    radar with your plate, the order, and any flavour profiles ticked to show there), **Menu**,
    **History** and **Flavour Profiles**. The Dish Served and End of Cycle pages take the whole
    tablet and hide the tabs until they're dismissed.

The UI is built in code at startup (`CookingPrototype.Layout.cs`), with uGUI and the built-in font,
and needs the Input System UI module (the setup adds one). A held item follows the pointer from
`UpdateHeld` (Input System `Pointer`), so a drag survives the UI under it being rebuilt.
**Custom Tools → Cooking Prototype → Assign Market Art** puts the billboard sprites on the scene's
prototype and sets them to point filtering.

The panels, buttons, progress bars, tablet frame and mouse pointer use a few pieces of Kenney's CC0
**UI Pack: Sci-fi**, copied into `Assets/Art Assets/UI Skin` (with its licence). They're light grey and
tinted in code, so the colours stay as before. **Custom Tools → Cooking Prototype → Assign UI Skin**
(`UiSkinSetup.cs`) sets them up as nine-sliced sprites (corners stay put when a panel stretches) and
puts them on the prototype; **Build Scene** runs it too. `UIFactory` applies the skin: every panel in
`PanelColor` or `PanelDark` and every button gets it automatically. Without the sprites the UI stays flat.

The cookware (knife, peeler, box grater, wooden spoon, wok, pot, cutting board, plate) is rendered from the CC0
[3D Cooking Assets](https://milkandbanana.itch.io/) pack by MilkAndBanana:
**Custom Tools → Cooking Prototype → Render Cookware Sprites** (`CookwareSprites.cs`) renders each
model into a trimmed, transparent PNG in `Assets/Art Assets/Cookware` and assigns them to the
prototype. The pack's FBX files import without colour, so the tool reads the shared palette from
one of its GLB files. Only the rendered PNGs are in git; the 3D pack is needed just to re-render
(say, with a different angle in the tool's item list). Without the sprites the game falls back to
plain shapes. The pot and pan are drawn with what's in them heaped inside, and water over the
pieces once a soup is cooking. The pot and pan can be picked up even when empty, and their bar
goes with them. The knife and peeler hang upright but are held flat by the handle, blade to the left.

### The cycle

The game runs in cycles (days). Everything you buy goes into your stock (`Inventory`), which tracks
units per prep state ("3× Onion raw", "2× Onion peeled, sliced").

1. **Morning** (untimed), at the market's **grocer** (`CookingPrototype.Store.cs`), a scene of its
   own over the stall: ingredients stand on shelves with price tags and how many you have at home.
   Click one, or drag it into the **basket**, where everything picked is heaped; click a piece in
   the basket, or drag it out, to put it back. The price tags name what's on the shelf. **Recipe bundles** under the shelves add everything one menu dish needs.
   Nothing is bought until you pay (`ShoppingCart`). The till also has **Empty basket**, the
   day-timer switch and who's coming today. The workbench waits: prepping is part of the timed day
   (meeting decision).
2. **Buy and start the day** pays for the basket, starts the timer and goes back to the stall,
   with the tablet on the Current Order. The grocer closes until the next morning.
3. **Service.** The day timer runs (`daySeconds`, 6 minutes). Every regular comes **once**, in a
   shuffled order, 1–6 s (`arrivalDelay`) after the last one leaves. They order as they arrive:
   their order dialogue names the dish, and the Order page fills in. It shows the recipe with each
   ingredient marked ready / needs prep / short (counting stock, the boards and the desk). If they
   order something that isn't on your menu (like the Sleeper's boiled synthetic chicken), it only
   shows the name: working out what goes in it is up to you. For a dish you made them before but
   didn't save, it says to look in the cooking history.
   - **Prep**: put units on a cutting board. A board takes several units of **one kind**. Hold a
     tool on the roof to pick it up and do its gesture over the board: that preps everything on it.
     Once it has started on a board, the tool stays locked to that board until the prep is done
     or you let go, so moving off the board mid-stroke doesn't reset it.
     Letting go hangs the tool back up. **Peeling comes first**: something sliced or grated can't
     be peeled. Prepped units stay on the board; hold the board to take all of them.
   - **Stations** (`CookingStation`): the pot or pan starts cooking as soon as one thing is in, and
     takes more while it cooks. The bar has three zones. **Blue** (the first half): undercooked.
     **Orange**: just right. **Red**: ruined, and it can't come back. The food darkens as it goes
     (browner in the orange, nearly black in the red).
   - **Pan**: lift it and shake it side to side (four strokes, so one move doesn't count). A shake
     in the orange sends the bar back to the start of the orange, and the food gets browner; an early
     shake does nothing.
   - **Pot**: stir it with the **spoon** from the roof (four strokes over the pot), which does the
     same as shaking the pan. (The knife stirs too.) Without a stock cube it boils one ingredient at
     a time; with one, it's a soup and takes anything.
   - Holding a station **takes it off the heat**: its cycle pauses and its bar goes with it. Drop it
     on a plate to dish up, on the other station to pour it in (rice has to be **boiled before it's
     fried**: boil it, then pour the pot into the pan). Anywhere else, the pantry included, puts it
     back on its stove with nothing lost.
   - **Seasoning**: drop salt, pepper, sugar, MSG2, citric acid or chilli
     (`IngredientDefinition.isSeasoning`) on a plate with a cooked dish. Nothing else goes on raw.
     Chilli and MSG2 give their full taste grated, half unprepped (`fullFlavourPrep`). For a
     recipe, a seasoning counts the same whether it was cooked in or added after.
   - **Serve**: drop the plate on the food delivery spot. The dish is judged (`DishJudge`). The
     Dish Served page plays their reaction and shows the dish's flavour profile, with the order's
     outline when it's a recipe you know. If the dish isn't on the menu, it offers a name field
     (20 letters): **Save dish** adds it to the menu (origin Emphis); **Continue** leaves it
     "Nameless". Continue sends the customer off (anything they were still saying stops).
   - **Day timer off**: to compare the "time trial gauntlet" and "zen social kitchen" vibes
     (design doc TODO), switch it off at the grocer in the morning, or untick `dayTimer` on the
     `Cooking Prototype` object. The day then ends when everyone has been.
4. **End of the cycle**, when everyone has been or the timer runs out (whoever is still waiting
   leaves without eating): the End of Cycle page lists each dish and who it was for, income,
   expenses, the **stall rent** (`stallRent`, $10) and the profit. If you can't pay the rent, it's
   **game over** and you go back to the title screen. Units on the boards and the desk go back into
   stock; plates and whatever is in the stations are binned. **Prepped stock spoils; raw stock
   carries over.**

### From the design doc (this round)

- **Dish pictures** (`CookingPrototype.Pictures.cs`): a dish is drawn as its ingredient sprites heaped
  on a plate. Fried pieces come out golden with brown char marks (darker, and more of them, if
  something burnt); a soup sits in a layer of broth. Seasonings are a pinch on the rim. Shown on the
  desk's plates, the Dish Served page, the Menu and History pages and lists, and the bundles.
- **Time's up**: when the timer runs out, the customer at the counter still gets their order
  ("Last customer"); nobody else comes. Those who never got there are listed as not making it
  before closing.
- **Staying away**: after food they didn't pay for, a regular may skip the next cycle
  or two (`stayAwayChance`, 50%). They always come back, and someone always turns up. The morning
  list says who's staying away. The Sleeper, paying anyway as a Stranger, doesn't mind.
- **Dream meal**: once found, it's what they always order.
- **Tastes and tips** (demo changes): see the verdict table below. In short, a correct order pays
  50% more than before (`margin` 2.4); a dish that isn't the order but lands in a basic tasty zone
  is paid in full without a tip; one in the customer's own favourite flavour is paid in full and
  tipped. Anything else isn't paid: no part-payment for a near miss.
- **Dishes as dots** (`FlavourDot`): on every radar a dish (and an order) is one dot, the centre
  of what stands out in its shape: each flavour minus the dish's own average (nearly every
  ingredient adds nutrition and crunch, so a plain centre puts every dish in one corner), averaged
  over the chart's corners and magnified (`Gain`) to use the chart. Zones are wedges on the chart
  (`FlavourZone` "On the chart": direction, width, inner and outer edge). **A dish is tasty when
  its dot is inside a tasty zone's wedge, and in their favourite when it's inside the customer's
  wedge**: exactly what the radar shows. A zone without a chart shape falls back to its per-axis
  min/max (the tests use those).
- **Only a proper dish counts** (`DishJudge.IsProperDish`, `MinIngredients` 3, `MinPrepped` 1): to
  be tasty, their favourite or their dream, a dish needs at least 3 different ingredients and
  something cooked, so a lone ingredient can't land in a zone and get paid. Hera and Petor also want
  at least one of them prepped on a board (peeled, sliced, grated...), so a few things tipped into
  the pot as they come won't do (`Customer.wantsPreppedFood`; off for the Sleeper, who doesn't know
  food). It only decides whether they like it: anything can still be cooked and served, and every
  menu dish has a prepped ingredient. The exact order is always paid (the
  Sleeper's raw potato). Until a dish is proper its dot is grey on the radar, and the Dish Served
  page says why it didn't count.
- **Cooking by taste** (`CookingPrototype.Taste.cs`): the Current Order radar is the guide while
  you cook. It shows the order (a grey dot) and the tasty zone it belongs to (blue) as the target,
  and the dish as it builds up (an orange dot): the plate you're working on plus the pot and pan,
  as they'll taste once cooked (burnt once they're in the red). Holding an ingredient over the pot,
  the pan or a plate shows a dashed ring: where the dot would move if you dropped it. The radar is clear
  whenever nobody is waiting on an order: after the dish is served and at the end of the day.
- **Prepped ingredients look prepped** (`PrepSprites`): made from the ingredient's own picture the
  first time it's needed. Peeled is paler, sliced is three outlined slices with pale cut faces,
  grated is a heap of shreds; peeled and sliced (or grated) combine. Used in the pantry, on the
  boards, the desk, in hand, in the pot and pan, and in dish pictures.
- **Dish Served and History radars**: the dish's dot, the order's grey dot when it's a dish you
  know, and the tasty zone only when the dot actually landed in it. The Menu shows each dish's dot
  in its zone.
- **Dish Served headline**: one of three, "Correct order", "Wrong order, but they liked it" or
  "Wrong order and they didn't like it". The tablet doesn't talk about how well a regular knows you.
- **Orders show when they're said**: the order dialogue is read from its first line, but the
  Current Order card only fills in on the line that names the dish (or when the dialogue ends).
  Dishes only NPCs know (the Sleeper's potato) are found through `offMenu` on the `Cooking Prototype`.
- **Faces** in the history's lists (the regular's picture, or a placeholder), and **bundle tiles**
  that list what they add.

### Tutorial (`CookingPrototype.Tutorial.cs`)

**Play with tutorial** on the title screen starts with a **practice day** before cycle 1: one
customer (the Man), one simple dish (*Soup on the rock*), no clock and no rent. A panel over the
conversation says what to do, and a pulsing outline marks what to use. Each step waits for the
player to actually do it:

1. Welcome: buy in the morning, cook for the regulars, pay the rent; today is practice.
2. The grocer: click (or drag) the Soup on the rock bundle, then Buy and start the day.
3. Petor orders Soup on the rock; the Current Order page and the Menu tab.
4. The order chart: dishes are dots; the order's grey dot, its blue tasty zone, your orange dot,
   and the ring that previews an ingredient held over the pot.
5. Drag a potato from the pantry onto a cutting board, and peel it with the peeler from the roof.
6. Drag the peeled potato, the soup cube and the salts into the pot (it starts with the first).
7. Put a clean plate on the desk; when the bar is orange, lift the pot onto it (the spoon holds it there).
8. Drag the plate to the food delivery spot; the Dish Served page; Continue.
9. What real days add (every regular, a timer, rent), the pan, and the pay rules for a dish that
   isn't the order: a proper dish (3 ingredients, something cooked, and for most regulars one prepped) in a tasty zone is paid, in
   their favourite zone it's tipped; the Flavour Profiles tab and its ticks.
   **Start cycle 1** then begins the real game fresh (starting money, empty pantry, the morning
   shop), so the player plans the shopping and prep for the real day themselves.

The tutorial can't be done wrong: a see-through `TutorialBlocker` over the screen lets clicks through
only on what the step uses (anything else says what to do), anything let go in the wrong place goes
back where it came from with a hint, the soup cube has to go in first, the pot can only be dished up
in the orange, and the practice pot never burns (it waits in the orange). **Skip tutorial** goes straight to cycle 1 from any step; **Play** starts without the
practice day. The order card also says when an ingredient is already in the pot or the pan.

### Sound (`CookingPrototype.Audio.cs`, `Audio/`)

Sound effects and a market ambience; the music is coming separately. Every clip is synthesized from
sine waves and filtered noise by `Audio/Source/synth_sounds.py` (plain Python 3, no packages), so
there are no samples or licences involved: edit a sound's function there and re-run it to change it.
**Custom Tools → Cooking Prototype → Assign Sounds** (`SoundSetup.cs`) imports them and puts them on
the prototype; **Build Scene** runs it too.

- **Ambience**: a busy sci-fi food street at night, looping all the time (about 27 s). Synthesized voices
  (vowel formants) in short exchanges now and then, near or far, with quiet stretches between; hover traffic passing, a soft synth drone over
  the city, plates and cutlery from the other stalls, the odd till, a neighbour's fryer, a flickering
  neon sign and a distant PA chime, all through a small reverb. Re-make it alone with
  `python synth_sounds.py ambience_loop`.
- **Cooking**: the pan sizzles (louder as it heats) and the pot bubbles while they're lit and on the
  heat; both go quiet when lifted off. A hiss when something in the pan burns.
- **Hands**: picking up and putting down ingredients, plates, the pot and pan and the tools; a
  stroke sound per knife, peeler or grater stroke and a chime when a board's prep is done; a toss per
  shake of the pan, a tick for a stir and a dull note for shaking too early.
- **Service**: a door chime when someone arrives, a soft blip as their lines type out, a bell when
  you serve, coins when they pay (a chime for a dream meal, a dull note when they don't), the till
  when you buy, and a low gong at the end of the cycle. Every button clicks.

Volumes are on the `Cooking Prototype` object (`sfxVolume`, `ambienceVolume`, `cookingVolume`).

### Minigames (`Prototype/Minigames/`)

From the design doc's cooking section. Each is a held drag that feeds a `Gesture`: a prep tool
moved over a cutting board, or the lifted pan. While a tool is held, the boards draw its guide and
a fading trail of the pointer (`GesturePadGraphic`).

| Tool | Gesture | Counts as a stroke | Strokes (one board) |
|---|---|---|---|
| Knife → sliced | `BackAndForthGesture` (vertical) | each change of direction after 70 px | 6 |
| Peeler → peeled | `HalfCircleDownGesture` | a half circle from the top down to the bottom, either side. Bottom to top, straight lines and reversals don't count | 3 |
| Grater → grated | `SwipeRightGesture` | 110 px to the right. Going back left earns nothing | 5 |
| Pan shake | `BackAndForthGesture` (horizontal) | each change of direction after 45 px. One shake = one stir (see the pan above) | one per unit |

Stroke counts, station speeds, the day timer and the rent are on the
`Cooking Prototype` object. Stock is a flag on the ingredient (`IngredientDefinition.IsStock`, set
on Soup Cube).

The code is split by part: `CookingPrototype.cs` (cycle loop, ticket, result and summary pages),
`.Layout.cs` (the screen, market and tablet), `.Kitchen.cs` (workbench, plates, stations, drags),
`.Prep.cs` (cutting boards and tools), `.Store.cs` (the morning grocer), `.RecipeBook.cs` (Menu tab),
`.History.cs`, `.Flavours.cs` (Flavour Profiles tab), `.Pause.cs` (pause menu and settings),
`.Npcs.cs` (customers and dialogue) and `.Discovery.cs` (naming dishes).

| Verdict | When | Pay | Liked? | Their reaction |
|---|---|---|---|---|
| Dream meal | deep in their favourite flavour (`DreamAt`, 0.85), with everything in `dreamNeeds`. Beats a refusal | price + 100% tip | yes | `DreamOrder` the first time, then `CorrectOrder` |
| Ordered | `RecipeDefinition.Matches`: same portions, prep and cooking, nothing badly cooked | 100%, + 30% tip in their favourite flavour | yes | `CorrectOrder` |
| Their taste | not the order, but a proper dish whose dot is in their favourite zone, well cooked | 100% + 30% tip (`FavouriteTip`) | yes | `WrongOrderTasty` |
| Tasty | not the order, but a proper dish whose dot is in a basic tasty zone, well cooked | 100%, no tip | yes | `WrongOrderTasty` |
| Too different | anything else | nothing | no | `WrongOrderNasty` |

**Similarity** (`DishSimilarity`) blends flavour and ingredients, weighted by `flavourWeight` (50/50):
flavour is 1 at the same point on the radar and 0 at `flavourRange` (6) apart; ingredients counts
units that went in as written (right ingredient with the wrong prep, cooking or doneness counts
half), over the larger of ordered and served units, so missing and extra units both cost. It's
still worked out for the history, but no longer pays: a near miss of the order is paid only if its
dot lands in a tasty zone. A dish in or close to
their favourite flavour (`CustomerRelationship.CloseToFavourite`) is added to what they like.

Price is the **ordered** dish's ingredient cost × `margin`. The menu follows the Notion menu:
MSBeef noodles, Calorice and Hellspawn fried rice (fried; their rice and noodles are boiled first
and poured into the pan), and Mushroom soup, Synthetic soup and Soup on the rock (boiled).

Only three regulars come, left to right on the Miro board (Notion demo changes):

- **Hera**: starts out liking MSBeef noodles (Real Beef™ only) and Calorice. Her favourite flavour is *Their lost love's soup*, Jessa's sweet and sour beef soup: her dream needs Beef.
- **Sleeper** (Miro board): their initial liked list is empty; every Stranger order is scripted in their dialogue (a potato, a peeled potato, an MSG2 cube, then soup on the rock). The raw "meals" aren't on the menu or in the book (any raw ingredient can go on a clean plate). They don't know food yet, so as a Stranger they pay full price for anything they'll eat (`alwaysPaysAsStranger`). The first dish that actually tastes good makes them Familiar on its own (`firstTasteReplacesLikes`). A secret spice fiend: *Mother's fire*, and the dream dish needs chilli, seaweed noodles and MSG2.
- **Petor**: his favourite is *An honest broth* (filling, a little to chew, a touch sweet). His dialogue column on Miro is still empty, so he has placeholder lines.
- **Pictures**: all three stand at the counter with their art from the Miro board (`Customer.portrait`, in `Assets/Art Assets/Characters`, trimmed).
- After their dream dish, their starting liked dishes that don't taste like their favourite are dropped, and their favourite joins the **Flavour Profiles** tab ("Hera's Taste").

Customer lines and other settings (margin, station speed, live preview) are on the
`Cooking Prototype` object in the scene. The live flavour preview is a tuning aid. The design
reveals the flavour only after serving.

`FlavourRadarGraphic` is the runtime radar chart.

### Recipe book (`CookingPrototype.RecipeBook.cs`, `RecipeGuide.cs`)

The tablet's **Menu** tab opens it any time. It lists the menu only (no background behind the
list); dishes you didn't save (or took off) are in the cooking history. The selected dish and its
flavour profile share one page:

- **The recipe**, written exactly like the Current Order card: each ingredient with its prep and
  whether it's ready, and the menu price. (`RecipeGuide` still writes the full steps; the tests use it.)
- **Flavour Profile** at the bottom of the page (scroll down): the dish alone on the radar.

While there's an order, opening it shows that dish. The kitchen keeps running while it's open.

### Flavour Profiles tab (`CookingPrototype.Flavours.cs`)

The basic tasty zones, one at a time with their names and each in its own colour; the arrows cycle
through them. **Display on Current Order tab** shows that zone on the Current Order's radar too.
When a regular gets their dream dish, their favourite flavour joins the list ("Hera's Taste").
Elsewhere (the Dish Served page) the tasty zone is drawn in blue.

### Pause menu and settings (`CookingPrototype.Pause.cs`)

Esc or the **II** button: **Resume**, **Main Menu**, **Settings** and **Quit Game**. Pausing stops
the game's time (`Time.timeScale`): timers, cooking, customers and dialogue. Settings has the
**sound effects** and **music** volumes, saved between sessions (`PlayerPrefs`); the title screen
opens it too. The music clip goes in `music` on the `Cooking Prototype` object when it's ready.

### Regulars and dialogue (`Prototype/Npc/`, `CookingPrototype.Npcs.cs`)

Stages and lines follow the Cooking Game Miro board. Each regular has a `CustomerRelationship` for
the session:

| Stage | How they get there | What changes |
|---|---|---|
| Stranger | where everyone starts | short, rude lines |
| Familiar | `likedMealsToFamiliar` liked meals (3): their exact order or very close to it | they chat while you cook and drop clues about their secret desire |
| Friendly | their dream meal | friends; they often order it |

- **Liked meals:** what a regular orders. It starts with their usual orders; a very close dish
  they didn't order is added (as the menu dish it makes, or as a new "Nameless" dish), so they can
  ask for it back and it's judged as an exact order when you recreate it.
- **A visit**, as DialogueSystem triggers `(NPC, event, stage)`:
  - `Order` when they arrive. The order graph's `payload` names the dish ("MSBeef noodles"), or
    is a token: `[liked meal]` / `[liked dish]` (one of their liked meals) or `[dream dish]`. In the
    text, tokens become the dish's name, or "the thing you made me the other day" / "the regular"
    when it isn't on the menu (never saved, or deleted).
  - `Musing` once per order, `musingAfter` (8–20 s) after they order.
  - `Hurry` every `hurryAfter` seconds (per regular) while they wait.
  - On serving, with priority: `CorrectOrder`, `WrongOrderTasty`, `WrongOrderNasty`, or
    `DreamOrder` (once per game).
  It prefers lines authored for the current stage and falls back to entries with state `None`. If
  neither exists, the customer's fallback line on the `Cooking Prototype` object is used.
- **Director policy:** `FinishThenQueue`. Whatever is being said finishes; the next dialogue
  waits a beat; a dish reaction jumps the queue. Options of a set play in order (O1, O2, O3) the
  first time, then at random; one-shots (like her Friendly O1 musing) play once.
- **Content:** **Custom Tools → Cooking Prototype → Rebuild Dialogue From Scratch** (Build Scene
  runs it) deletes the graphs and builds them again from `CookingDialogueSetup`; **Build Dialogue**
  only adds what's missing. One graph per option
  in `Data/Dialogue/Graphs/` ("Woman - Order - Stranger - O1") and the catalog
  `Data/Dialogue/CookingDialogueCatalog.asset` through the DialogueSystem's editor API, then wires
  the open prototype scene (catalog, each regular's `dialogueId`, a `DialogueDirector`). Hera's
  and the Sleeper's lines are the board's, as written (Hera's Stranger "correct order" O1 is
  "[Nothing]" on the board, so that graph has no lines; her MSBeef orders name the MSBeef noodles
  from the Notion menu). The Sleeper's are complete for every stage, including the dream (his
  mother's noodles) and the Friendly musings after it. Petor has the board's MSG line and
  otherwise placeholders. Hera, Petor and the
  Sleeper use the DialogueSystem ids Woman, Man and Sleeper.
- The cycle summary lists who became familiar or had their dream meal; the morning ticket shows
  each regular's stage and what they order now.

### Discovering a regular's recipe (`DesireClue`, `CookingPrototype.Discovery.cs`, `.History.cs`)

Each regular has an order (what they ask for), dishes they like, and a **secret desire**: their
own flavour zone in `Data/Zones/Desires/` that **no menu recipe reaches**, so the dish has to be
invented. **Custom Tools → Cooking Prototype → Build Desire Zones** creates them (existing ones are
kept, so tune them in the Inspector) and assigns them to the regulars in the open scene; **Reset
Desire Zones** puts them back to the values in code. They're
built around dishes from the design doc that aren't on the menu; one way to hit each:

| Regular | Desire | One dish that hits it |
|---|---|---|
| Hera | Their lost love's soup (needs Beef) | Soup Cube, Beef sliced, 2× Sugar, 2× Citric Acid, boiled. With Real Beef it tastes right but isn't it |
| Petor | An honest broth | Soup Cube, 2× Potato peeled+sliced, Pak Choi sliced, Mushroom sliced, Salt, boiled |
| Sleeper | Mother's fire | Seaweed Noodles, Synthetic Chicken sliced, 4× Chilli grated, 2× MSG2 grated, fried |

- **Clues.** From Familiar, after each dish (until they've had their dream meal) the regular adds a
  clue after their reaction: warmer / colder / about the same than their closest dish so far, plus
  the flavour that's most off ("It should be sweeter", "Hotter. Much hotter", "It needs more
  depth"), or, when the flavour is right but an ingredient it needs is missing, "It tastes right.
  But something in it isn't what it should be." Strangers keep it to themselves. Their dialogue
  lines drop story hints too.
- **Cooking history.** Each visit is recorded from the moment the customer arrives
  (`HistoryEntry`) as a full report on the recipe book's **History** page. On the left you filter
  by regular and pick an order (newest first). The header is the dish, who it was for, the cycle
  and the verdict. The report:
  - **Ordered:** the dish name.
  - **Received:** what was on the plate, written like the order card's lines.
  - **Receipt:** dish price, cash received, ingredients, a line, and the profit.
  - **Transcript:** everything they said.

  On the right is that dish on the radar (with the order's outline when you knew it). For a dish
  that isn't on the menu there's a name field: **Save dish** saves it as your own recipe, so
  regulars can order it by name.
- **Unlocking.** Hitting the desire is their dream meal (the dream order scene, and they become
  Friendly) and the dish becomes a new recipe: the result screen asks you to **name it** and save
  it to the menu. Unnamed, it stays "Nameless" and lives only in the cooking history, where you
  can still save it.

### The recipe book's menu

The book lists only the menu. Dishes regulars order that you never saved, or took off, aren't in it:
find them in the cooking history. Each dish shows whether it's a soup, fried, or neither. **Remove from the menu** soft-deletes a
dish you saved: it leaves the menu and the bundles, regulars can still order it, and their lines
stop naming it. The default recipes can't be removed (meeting decision: regulars would be left with
nothing they like, or nothing to name).

### Radars and secret desires

The radars only show what you made, plus the order's outline when the order is a recipe in your
book. They draw no zones. The regulars' secret desire zones are undiscovered recipes, so the game
never draws, lists or names them; they're only used to judge the dream meal and give clues.
`showDesireDebug` on the `Cooking Prototype` object shows the desire score on the result screen
for tuning.

## Tests

EditMode tests live in `Tests/Editor` (Window → General → Test Runner → EditMode). They cover the
calculator, evaluator, recipe matching, stations, dish judging, the minigame gestures and the pot rule.

## Not here yet

The real stall: art and plating visuals, tool sprites that follow the cursor, oil options for the
wok, plates piling up, saving between sessions, and the Man's and the Sleeper's lines from the board.
