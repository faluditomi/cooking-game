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

## Tests

EditMode tests live in `Tests/Editor` (Window → General → Test Runner → EditMode).

## Not here yet

Recipe assets and the menu, cooking stations and their minigames, customer judging, and a
runtime (in-game) radar chart. The editor radar in `Editor/FlavourRadarGUI.cs` is a reference for that.
