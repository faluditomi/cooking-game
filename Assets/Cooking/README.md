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

| Recipe | Lands in |
|---|---|
| MSBeef fried rice | Greasy Comfort |
| Calorice | between Greasy Comfort and Hearty Broth (bland, on purpose) |
| Hellchick noodles | Fire Bowl |
| Mushroom soup | Hearty Broth |
| Synthetic soup | near Hearty Broth |
| Soup on the rock | **no zone**: salty 8.2. Lower its salt or accept it as a rough dish. |
| Sweet & sour tofu soup | Sweet & Sour only (the Woman's dream is now a beef version, see below) |
| Burnt or raw dishes | no zone |

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

- **Conversation** (left, full height): the stall, the cycle and time left, your money, who's at
  the counter, and what they say, typed out with the newest line at the bottom. Under it, the latest
  hint. It clears when the customer leaves.
- **Market** (top right): the night market with billboard ads (CraftPix pack, new ads each cycle),
  the customer as a placeholder figure at the bottom left, and the **prep tools** (knife, peeler,
  grater) hanging from the stall roof.
- **Workbench** (bottom right), all press-and-hold drags:
  - **Pantry** (right): your stock (`Inventory`), grouped into **raw**, **prepped** and
    **seasoning**, each pile showing its pieces. Holding a pile takes **one** unit. Drop it on a
    cutting board, the pot, the pan, a plate (seasonings only, on a cooked dish) or free desk
    space. Dropped anywhere else, it goes back on its pile.
  - **Desk space** (top left, six spots): plates and loose ingredients. Hold a loose pile to take
    all of it.
  - **Clean plates**: an endless stack. A plate dropped on the desk goes to the nearest free spot.
    An empty plate can go back on the stack; a plate with anything on it stays on the desk.
  - **Food delivery**: drop a plate here to serve it.
  - **Cooking station (pot)** and **frying station (pan)**: drop ingredients in, then click the
    station to light it.
  - **Prep section**: two cutting boards.
  - **Tablet**, built into the table (bottom left). Small, it shows the **Order** page. **Recipes**,
    **History** and, in the morning, **Shop** open it big over the right side, as do the Dish
    Served and End of Cycle pages. **Shrink** puts it back.

The UI is built in code at startup (`CookingPrototype.Layout.cs`), with uGUI and the built-in font,
and needs the Input System UI module (the setup adds one). A held item follows the pointer from
`UpdateHeld` (Input System `Pointer`), so a drag survives the UI under it being rebuilt.
**Custom Tools → Cooking Prototype → Assign Market Art** puts the billboard sprites on the scene's
prototype and sets them to point filtering.

### The cycle

The game runs in cycles (days). Everything you buy goes into your stock (`Inventory`), which tracks
units per prep state ("3× Onion raw", "2× Onion peeled, sliced").

1. **Morning** (untimed). The tablet opens big on the **Shop**. Fill a cart (`ShoppingCart`):
   ingredients one at a time from the shelf (−, +1, +5), a whole dish from the **Bundles** tiles
   (one serving of a menu dish), or from the **Recipes** tab (**+ One serving**, **+ What's
   missing**). Nothing is bought until you pay. The cart column also has the day-timer switch and
   who's coming today. The workbench is locked: prepping is part of the timed day (meeting
   decision).
2. **Buy and start the day** (Shop tab) pays for the cart, starts the timer and opens the stall.
   The tablet goes back into the table, on the Order page. The Shop closes until the next morning.
3. **Service.** The day timer runs (`daySeconds`, 6 minutes). Every regular comes **once**, in a
   shuffled order, 1–6 s (`arrivalDelay`) after the last one leaves. They order as they arrive:
   their order dialogue names the dish, and the Order page fills in. It shows the recipe with each
   ingredient marked ready / needs prep / short (counting stock, the boards and the desk). If they
   order something that isn't on your menu (like the Sleeper's boiled synthetic chicken), it only
   shows the name: working out what goes in it is up to you. For a dish you made them before but
   didn't save, it says to look in the cooking history.
   - **Prep**: put units on a cutting board. A board takes several units of **one kind**. Hold a
     tool on the roof to pick it up and do its gesture over the board: that preps everything on it.
     Letting go hangs the tool back up. **Peeling comes first**: something sliced or grated can't
     be peeled. Prepped units stay on the board; hold the board to take all of them.
   - **A dish is one batch**: everything for it goes into the pot or the pan before you light it.
     Holding a lit station **takes it off the heat**: its cycle pauses and its bar goes with it.
     Drop it on a plate to dish up; anywhere else puts it back and the cycle carries on. A plate
     with a dish on it takes no more cooked food. **Take it all out** returns an unlit station's
     contents to the pantry.
   - **Pan**: every unit needs one stir (3× tofu + 1× rice: 4 stirs). Lift the pan and shake it
     side to side. The bar has four zones. **Grey**: a shake is too early and sends the bar back to
     the start. **Green**: stirs one unit perfectly. **Red**: stirs one unit, a bit burned
     (overcooked: bitter). A counted shake sends the bar back to the middle of the grey, so it's
     one stir per lift: put it back to heat up again. Reaching the **black** burns one unit, and
     leaving it at the end burns another every 2 s. Units never stirred come off undercooked.
   - **Pot**: dish it up on the narrow green line, or it overcooks. Without a stock cube it boils
     one ingredient at a time; with one, it's a soup and takes anything. Everything in it comes
     off with the same doneness.
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
     (design doc TODO), switch it off in the Shop in the morning, or untick `dayTimer` on the
     `Cooking Prototype` object. The day then ends when everyone has been.
4. **End of the cycle**, when everyone has been or the timer runs out (whoever is still waiting
   leaves without eating): the End of Cycle page lists each dish and who it was for, income,
   expenses, the **stall rent** (`stallRent`, $10) and the profit. If you can't pay the rent, it's
   **game over** and you go back to the title screen. Units on the boards and the desk go back into
   stock; plates and whatever is in the stations are binned. **Prepped stock spoils; raw stock
   carries over.**

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
`.Prep.cs` (cutting boards and tools), `.Store.cs` (Shop tab), `.RecipeBook.cs`, `.History.cs`,
`.Npcs.cs` (customers and dialogue) and `.Discovery.cs` (naming dishes).

| Verdict | When | Pay | Liked? | Their reaction |
|---|---|---|---|---|
| Dream meal | inside their secret desire zone, with everything in `dreamNeeds`. Beats a refusal | price + 100% tip | yes | `DreamOrder` the first time, then `CorrectOrder` |
| Ordered | `RecipeDefinition.Matches`: same portions, prep and cooking, nothing badly cooked | 100% | yes | `CorrectOrder` |
| Very close | similarity ≥ `veryCloseAt` (80%) | `veryClosePay` (80%) | yes | `WrongOrderTasty` |
| Close enough | similarity ≥ `closeEnoughAt` (55%) | `closeEnoughPay` (60%) | no | `WrongOrderTasty` |
| Refused | contains something they won't eat | nothing | no | `WrongOrderNasty` |
| Too different | anything else | nothing | no | `WrongOrderNasty` |

**Similarity** (`DishSimilarity`) blends flavour and ingredients, weighted by `flavourWeight` (50/50):
flavour is 1 at the same point on the radar and 0 at `flavourRange` (6) apart; ingredients counts
units that went in as written (right ingredient with the wrong prep, cooking or doneness counts
half), over the larger of ordered and served units, so missing and extra units both cost. As a
guide with the placeholder data: forgetting the pepper or not grating the MSG2 is very close;
1 mushroom instead of 3, overcooking or leaving out the tofu is close enough; a burnt fried rice,
Hellchick without chilli, or a different dish isn't paid for. A very close dish they didn't order
is added to what they like.

Price is the **ordered** dish's ingredient cost × `margin`. Customers come from the design doc's NPC notes:

- **The Woman**: starts out liking MSBeef fried rice (Real Beef™ only), Synthetic soup and Calorice, and refuses Beef. Secretly craves *Their lost love's soup*, a sweet and sour beef soup: her dream needs Beef, and wins over the refusal.
- **The Man**: refuses synthetic chicken. Craves *An honest broth* (filling, a little to chew, a touch sweet).
- **The Sleeper**: orders boiled synthetic chicken, a recipe only NPCs know (it's not on the menu or in the book). Craves *Mother's fire* (very hot, savoury, not sweet).
- **A generic Eyesider**: orders anything on the menu, craves nothing.

Customer lines and other settings (margin, station speed, live preview) are on the
`Cooking Prototype` object in the scene. The live flavour preview is a tuning aid. The design
reveals the flavour only after serving.

`FlavourRadarGraphic` is the runtime radar chart.

### Recipe book (`CookingPrototype.RecipeBook.cs`, `RecipeGuide.cs`)

The tablet's **Recipes** tab opens it any time. It lists the menu only;
dishes you didn't save (or took off) are in the cooking history. For the selected dish it shows:

- **Steps** (`RecipeGuide`): what to buy (with how many you have), what to prep with which tool
  ("peel (peeler), then slice (knife)"), which station cooks what and how (pot: the soup cube
  makes it a soup; wok: take it off in the green, shake in the red; raw straight to the plate), and serving.
- **Flavour**: the dish alone on the radar. No zones, and no "tastes like", which could give away
  what customers want.
- **Shopping**: have/need per ingredient (stock + cart), with **+ One serving** and **+ What's
  missing** to fill the store cart. These only work in the morning.

While there's an order, opening it shows that dish. The kitchen keeps running while it's open (the
big tablet covers the workbench), so looking something up costs time.

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
  - `Order` when they arrive. The order graph's `payload` names the dish ("MSBeef fried rice"), or
    is a token: `[liked meal]` / `[liked dish]` (one of their liked meals) or `[dream dish]`. In the
    text, tokens become the dish's name, or "the thing you made me the other day" / "the regular"
    when it isn't on the menu (never saved, or deleted). `[name of her lover]` stays as written.
  - `Musing` once per order, `musingAfter` (8–20 s) after they order.
  - `Hurry` every `hurryAfter` seconds (per regular) while they wait.
  - On serving, with priority: `CorrectOrder`, `WrongOrderTasty`, `WrongOrderNasty`, or
    `DreamOrder` (once per game).
  It prefers lines authored for the current stage and falls back to entries with state `None`. If
  neither exists, the customer's fallback line on the `Cooking Prototype` object is used.
- **Director policy:** `FinishThenQueue`. Whatever is being said finishes; the next dialogue
  waits a beat; a dish reaction jumps the queue. Options of a set play in order (O1, O2, O3) the
  first time, then at random; one-shots (like her Friendly O1 musing) play once.
- **Content:** **Custom Tools → Cooking Prototype → Build Dialogue** generates one graph per option
  in `Data/Dialogue/Graphs/` ("Woman - Order - Stranger - O1") and the catalog
  `Data/Dialogue/CookingDialogueCatalog.asset` through the DialogueSystem's editor API, then wires
  the open prototype scene (catalog, each regular's `dialogueId`, a `DialogueDirector`). The Woman's
  lines are the board's, as written (her Stranger "correct order" O1 is "[Nothing]" on the board,
  so that graph has no lines and she says nothing); the Man has the board's MSG line and otherwise placeholders, as
  do the Sleeper and Eyesiders. Existing graphs are kept, so rewrite the text in
  **Custom Tools → Dialogue Graph Editor**; rerunning only adds what's missing. Build Scene runs it too.
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
| The Woman | Their lost love's soup (needs Beef) | Soup Cube, Beef sliced, 2× Sugar, 2× Citric Acid, boiled. With Real Beef it tastes right but isn't it |
| The Man | An honest broth | Soup Cube, 2× Potato peeled+sliced, Pak Choi sliced, Mushroom sliced, Salt, boiled |
| The Sleeper | Mother's fire | Seaweed Noodles, Synthetic Chicken sliced, 4× Chilli grated, 2× MSG2 grated, fried |

- **Clues.** From Familiar, after each dish (until they've had their dream meal) the regular adds a
  clue after their reaction: warmer / colder / about the same than their closest dish so far, plus
  the flavour that's most off ("It should be sweeter", "Hotter. Much hotter", "It needs more
  depth"), or, when the flavour is right but an ingredient it needs is missing, "It tastes right.
  But something in it isn't what it should be." Strangers keep it to themselves. Their dialogue
  lines drop story hints too.
- **Cooking history.** Each visit is recorded from the moment the customer arrives
  (`HistoryEntry`) as a full report on the recipe book's **History** page. On the left you filter
  by regular and pick an order (newest first). In the middle is the report:
  - **Order:** the dish, their order lines, and how long after ordering it was served.
  - **What was said:** every line with a timestamp, labelled Order, Musing, Hurrying, Reaction or Clue.
  - **What was cooked:** a timestamped log of units put into the wok/pot/plate, stations started
    and taken off (at what %, doneness, wok shakes), anything binned, and prep done during the order.
  - **Served**, **to make it again** (the dish's name or "Nameless", and its recipe steps), the
    dish's **flavour profile** per axis next to the order's when you knew the order (orange = more,
    blue = less), and the **result** (verdict, similarity breakdown, pay, ingredient cost, profit).

  On the right is that dish on the radar (with the order's outline when you knew it), plus what you
  know about that regular's secret desire (their clues so far, or the recipe you found). For a dish
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
