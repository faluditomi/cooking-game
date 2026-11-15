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
| Sweet & sour tofu soup (the woman's dream meal) | Sweet & Sour only |
| Burnt or raw dishes | no zone |

## Cooking prototype (`Prototype/`)

A playable loop to try the flavour system with. **Custom Tools → Cooking Prototype → Build Scene**
creates `Prototype/CookingPrototype.unity` and the design-doc recipes in `Data/Recipes/`. It keeps
recipe assets that already exist, so hand tuning survives a rebuild. Then press Play.

1. A customer orders a dish. The order card shows the recipe, its menu price and which zone it tastes like.
2. **Pantry**: each click buys one unit onto the prep board. Click again for more of the same thing.
3. **Prep board**: Peel / Slice / Grate (they stack), then send the stack to the wok, the pot, or
   straight to the plate raw.
4. **Wok**: Start, then take it off in the green. In the red, **Shake!** knocks the bar back. If it
   reaches the end, it's burnt. **Pot**: take it off on the narrow green line, or it overcooks.
   Everything in a station comes off at once with the same doneness.
5. **Serve**: the dish is judged (`DishJudge`) and its flavour is revealed on the radar next to the order.

| Verdict | When | Pay |
|---|---|---|
| Dream meal | inside the customer's secret desire zone | price + 100% tip |
| Ordered | `RecipeDefinition.Matches`: same portions, prep and cooking, nothing badly cooked | price |
| Liked substitute | shares a zone with the order, score ≥ `likeThreshold` | price, +25% tip if fully inside |
| Refused | contains something they won't eat | nothing |
| Disliked | anything else | nothing |

Price is the **ordered** dish's ingredient cost × `margin`. Customers come from the design doc's NPC notes:

- **The Woman**: refuses beef, secretly craves Sweet & Sour.
- **The Man**: refuses synthetic chicken, craves Hearty Broth.
- **The Sleeper**: orders boiled synthetic chicken, craves Fire Bowl.
- **A generic Eyesider**: orders anything on the menu.

Customer lines and other settings (margin, station speed, live preview) are on the
`Cooking Prototype` object in the scene. The live flavour preview is a tuning aid. The design
reveals the flavour only after serving.

The UI is built in code at startup (`CookingPrototype.BuildUI`), uses uGUI with the built-in font,
and needs the Input System UI module (the setup adds one). `FlavourRadarGraphic` is the runtime radar chart.

## Tests

EditMode tests live in `Tests/Editor` (Window → General → Test Runner → EditMode). They cover the
calculator, evaluator, recipe matching, stations and dish judging.

## Not here yet

The real stall: art and plating visuals, the prep and wok minigames as mouse gestures, saving new
recipes to the menu, cooking history, day cycles and rent, and the conversation system hookup.
