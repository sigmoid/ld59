# Trading Card Minigame 

I would like to add a trading card minigame similar to hearthstone or yugioh

## Technical details

This game functions the same as other minigames such as solitaire and minefield. It will exist
as an application in the game's start menu. Use a placeholder icon for it for now. Also use just use
TCG as its name for now.

This game should all take place within a ui window, and any elements that are part of this game should be 
parented to that window. If the window moves, they move. If they go outside of the window, they are clipped.

## UI Library

The engine used by this game has a well featured UI library which you will use exclusively and extend if needed. 

## Background

All of the minigames I am creating are based on an invented alien alphabet which you can find more info
in the alphabet folder of the code base. The alphabet is a pyramid shape. The rules around this alphabet
currently are 

1. Tiers
    Each symbol belongs to a specific row in the pyramid, which corresponds to its 'tier' value. In existing
    minigames this rule is used in several ways. In Minefield, the tier of the symbol shown on the grid corresponds to the number of adjacent mines. In solitaire, the only valid ordering of cards is in descending tier order. Powergrid is a graph coloring game in which connected nodes need to be one tier different.
1. Sidedness
    There is a concept of being on the right, left, or center of the grid. These rules apply to the minigames in the following ways. Minefield: the sidedness of a revealed symbold tells you if the mines adjacent to the cell are on the right left or split. Solitaire, there is an additional rule in solitaire that you can temporarily stack cards of the same tier and any color as long as they share the same sidedness or are ambiguous/center.


## Visual Style

This game is presented in 1-bit through a shader that applies dithering to approximate different colors. The design needs to have high contrast to make things readable.

## Gameplay details

The game board has a number of places where players can place their cards. It also has a second row of places where 'life' cards can be played.

Life cards are combinations of symbols (words). The player will randomly be assigned some number of these at the start of the game. They lose the game when their opponent destroys all of their life cards. 

Cards should use the background template from the solitaire game and for now we can use the same art that solitaire uses for the backing of the cards. Cards should also have room for a description if we decide to add that. They should also display the tier as a number and the sidedness of each card.

In the game players draw from a deck of cards. Each card that they draw is a symbol from the alphabet

Eventually I might add effect cards, but for now lets make it simple.

There is a shared 'dictionary' that functions like the extra deck in yugioh. Players can fuse symbols they have drawn to summon from the dictionary. As part of the game the player should be able to browse this deck/dictionary in a scroll view.

Engagement rules: combat is by tier. You can only attack a card whose tier is at most your own — a higher-tier card knocks the target's top symbol off for free, equal-tier cards grind each other down (peeling the weaker symbol first) and only trade a mutual kill once both are reduced to a single matching symbol, and a lower-tier card cannot touch a higher one. Sidedness plays no part in battle; it governs fusion instead. (See "Ruleset v3" below.)

Other rules: player can only summon a limited number of cards on their turn, and are limited in the number of times they can combine cards to summon from the dictionary.

A big part of the challenge of this game is fusing upward: fusion climbs the pyramid one tier-step at a time within a sidedness school, and taller cards soak more hits and can stride past shorter defenders to reach life cards — so the player is always weighing one great card against keeping bodies on the field.

## Ruleset v3 — attrition combat, one stat one job (design brief)

Supersedes v2. History: v1's lock-and-key engagement legality froze boards (95%+ of sims stalled); v2's stacking damage bonuses made games a ~6-turn race and overloaded sidedness (combat bonus *and* fusion gate). v3 gives each stat exactly one job:

- **Sidedness** governs what you can **fuse** — it has no effect in battle.
- **Length** (symbol count) is the card's **health**.
- **Tier** governs **combat** — you can only strike down at cards you outrank.

### The card, restated

A card is a stack of symbols, and the symbols ARE the card:

- **Length** is its hit points, purely — the card dies when its last symbol is knocked off. (It no longer doubles as "reach"; the life row is gated only by clearing the board.)
- **Tier** = highest tier among its current symbols. It degrades as peak symbols are knocked off.
- **Sidedness** = sign of the summed `HorizontalSide` values. Left and right are the two fusion schools; center is the ambiguous wildcard.

All three stats recompute from whatever symbols remain — combat only ever knocks the peak off, so a card's tier drops as it takes hits (and its sidedness/length shift with it).

### Combat

- You can only attack a target you outrank or tie: **the attacker's tier must be greater than or equal to the target's**. You can never knock a symbol off a card unless that symbol sits below your own top tier — so a lower-tier card cannot touch a higher-tier one at all. (This is the rule that makes tier feel like real power: nothing you outrank can hurt you, nothing that outranks you can be answered head-on.)
    - **Attacker tier > target tier** → knock the target's **highest**-tier symbol off (one symbol, the peak blunted; its tier drops). The attacker takes no damage. Ties within a card: most recently added falls.
    - **Attacker tier = target tier** → knock the target's **lowest**-tier symbol off first, as long as it still has one sitting below the shared peak tier. The attacker takes no damage on this exchange, so a tall card can grind a shorter one down for free while it has sub-peak symbols to shed. Only once the target has **no sub-peak symbol left** — its remaining symbols are all at the shared tier, which in practice means it's usually down to (or started as) a single symbol — does the strike turn destructive: an attacker whose length is **at least** the target's destroys the target outright, and if the lengths are **exactly equal** (the clean case: two single-symbol cards of the same tier) the attacker is destroyed too, a true mutual trade. A same-tier attacker that's shorter than an all-peak target bounces off and does nothing that turn.
- A card with no symbols left is destroyed.
- Sidedness plays no role in combat.
- *Note:* the tier gate reintroduces the possibility of standoffs (a lone top-tier card no one can outrank), but mutual trades, fusion climbing, and length-based life reach keep games moving — AI-vs-AI sims stay 100% decisive. Watch for stalls if the numbers change.

### Sidedness — fusion schools only

- Fused cards must be from the **same sidedness**; a **center** card is ambiguous and can fuse into either school (kept from the alphabet's established wildcard semantics — solitaire's parking rule works the same way).
- This splits your board into left and right lineages with the three center symbols as universal glue — which school you can build tall is shaped by what you draw.

### Fusion

- Once per turn, combines two of your **summoned** (on-field) cards. Two gates:
    - **Same sidedness** (or either card center), per above.
    - **Tiers exactly one apart** — the two cards' tiers must differ by 1, so fusion climbs the pyramid step by step, the same adjacency that drives solitaire's runs and Powergrid's coloring rule.
- The result is the union of their symbols, lands in the first-selected slot, and its stats derive as usual. Independent of the summon limit.
- No length cap for now (tuning lever). If a fused card's symbols match a dictionary word, it is tagged as that word.

### Special abilities (the dictionary's new job)

- **Specific symbol combinations grant a card special abilities.** The dictionary is the catalog of these: fuse a card whose symbols match a word and the card gains that word's ability. The word-tag hook already exists in the engine; designing the actual abilities is the next milestone after v3 combat proves out. The dictionary browser stays as the reference for these combinations and as the life-card pool.

### Life cards and winning

- 3 life cards per player, dealt face up from the dictionary's word pool, inert (they never attack).
- A life card may be attacked **only when the defender's front row is completely empty** — you must clear their board before you can strike at their life.
- Life cards obey the same tier rule: a **higher-tier** attacker chips them one symbol at a time (free), a **same-tier** attacker grinds them down the same way front-row combat works — peeling sub-peak symbols for free, then trading with the attacker only once the life card is down to a single matching-tier symbol — and a lower-tier attacker cannot touch them. Destroy all 3 to win.

### Turn structure, zones, decks (carried over)

- Draw 1 → main phase (up to 2 summons + 1 fusion, any order) → attacks (each card once) → end turn. First player skips attacks on turn 1. 3 front slots + 3 life slots per player; 3 copies of each symbol per deck (45 cards); empty deck just stops drawing; no hand limit.

### The opponent (AI sketch)

- Greedy one-ply, re-aimed for v3: fuse when legal and useful (opening a life-row lane or climbing toward one), summon to keep bodies and fusion material — now caring about tier adjacency when choosing what to field — and attack the most valuable target, prioritizing life cards whenever reachable and preferring hits that land on the defender's peak. Verified by the AI-vs-AI harness — the bar is ~100/100 decisive games before it ships.

### Tuning levers (deliberately not decided yet)

- **First-player advantage**: measured at 65.8% for the first seat under v2 (1000-game `winrate` harness). Re-measure under v3; if it persists, compensate the second player (extra starting card is the standard fix).
- **Capture variant**: knocked-off symbols go to the attacker's hand instead of leaving the game — flavorful comeback mechanic; try after the base game proves out.
- The no-cap fusion rule, the stride-over margin, and summoning sickness (currently: none) remain open numbers to tune against the sim and real play.