# NoClue — Game Design

A family-themed murder-mystery deduction game for 2–8 players on a local network. One player hosts and the others join. There is no board: on each turn, a player either asks another player about a guess or makes an accusation.

---

## 1. Assets (`MyResources/`)

| Group      | Count | Cards |
|------------|-------|-------|
| Characters | 8 | AntieLana, Chane, Dad, GrandMa, GrandPa, Miane, Mom, UnclePeter |
| Rooms      | 8 | Bedroom, Braai, Garage, GardenShed, Kitchen, Study, SwimmingPool, TVRoom |
| Weapons    | 6 | BraaiTongs, CellCharger, CoffeeMug, ExtensionCord, GardenFork, Spanner |
| Audio      | 1 | `audio/introbackground.wav`: plays on the start screen and in the lobby |

That makes 22 cards in total. Each card's ID is its file name without the extension (for example `Dad`, `Kitchen`, `Spanner`).

The **character images have two uses**: they are cards, and they are the player's **profile picture** (the character the player picks in the lobby).

> Note: the PNGs are 1.4–2.8 MB each. In Unity, set Max Size to about 512 and use compression so they don't bloat the build.

---

## 2. Game flow

```
Start Screen ──► HOST ──► Pick character ──► Lobby (waiting) ──► Start Game
             └─► JOIN ──► Game list ──► Pick game ──► Pick character ──► Lobby
```

### 2.1 Start screen
- Intro sound plays.
- Two buttons: **HOST** and **JOIN**.
- Optional: a player name field (shown under the profile image).

### 2.2 Host
1. The player taps **HOST**.
2. They choose a character, which becomes their profile image.
3. The game is now advertised on the LAN and the host waits in the lobby.
4. The lobby shows every connected player (profile image and name) as they join.
5. The **Start Game** button is enabled only when **2 or more players** have joined.
6. The lobby is full at **8 players** (one per character). A full game shows as "Full" in the game list and can't be joined.

### 2.3 Join
1. The player taps **JOIN**.
2. They see the list of hosted games on the LAN (host name, player count, Join button).
3. They pick a game and connect.
4. They choose a character. **Characters already taken by other players are greyed out and cannot be selected.** The host has the final say: if two players pick the same character at the same moment, the first request wins and the second player is asked to choose again.
5. They wait in the lobby until the host starts the game.

---

## 3. Dealing the cards

When the host starts the game:

1. **Solution envelope:** the host randomly takes **1 character, 1 room and 1 weapon** and keeps them secret. This is the answer players are trying to deduce. *(This step is needed: without a hidden answer, an accusation can never be right or wrong.)*
2. The remaining **19 cards** are dealt round-robin in player order, in this sequence:
   1. all remaining **characters**,
   2. then all remaining **rooms**,
   3. then all remaining **weapons**.
3. Dealing round-robin keeps the hands as even as possible. Some players get one card more than others when 19 doesn't divide evenly (for example, with 3 players the hands are 7 / 6 / 6).
4. Each player receives **only their own hand**. Players never receive the envelope or other players' hands.

Player order is the order players joined, with the host as Player 1.

---

## 4. The card grid (each player's notebook)

Every player has a grid showing **all 22 cards** with their images, split into three sections:

```
┌─ CHARACTERS ───────────────────────────────────────────┐
│ [AntieLana] [Chane] [Dad] [GrandMa] [GrandPa] ...      │
├─ ROOMS ────────────────────────────────────────────────┤
│ [Bedroom] [Braai] [Garage] [GardenShed] ...            │
├─ WEAPONS ──────────────────────────────────────────────┤
│ [BraaiTongs] [CellCharger] [CoffeeMug] ...             │
└────────────────────────────────────────────────────────┘
```

Each card can carry one label:

| Label | Meaning |
|-------|---------|
| **Owned** | The card is in my own hand |
| **[profile image of player X]** | Player X showed me this card in answer to my question |
| **Out: [player X]** | Player X was eliminated and all their cards were revealed to everyone |
| *(no label)* | Unknown, so it could still be in the envelope |

Cards without a label are the remaining suspects. Players can work out the answer by elimination.

---

## 5. Taking a turn

Turns go in player order, starting with the host. The active player chooses one of two actions:

### 5.1 ASK (a suggestion)
1. The active player picks **any 1 character + any 1 room + any 1 weapon**. All cards can be picked, including ones they own.
2. The question goes to the **next player** in turn order.
3. **The asked player's screen** shows the 3 cards, with the ones they hold highlighted:
   - If they hold **one or more**, they **must choose exactly 1** to show.
   - If they hold **none**, they tap **"I have none"** and the question moves on to the next player. This repeats until someone shows a card or the question gets **back to the asking player**, which means nobody else holds any of the three cards.
   - Eliminated players are skipped (their cards are already public).
4. **The asking player** sees which card was shown and who showed it. That card is labelled with the shower's profile image in their grid.
5. **Every other player** sees a public banner, without the card:
   > *[Mom's profile image] Mom asked [Dad's profile image] Dad: Chane, in the Kitchen, with the Spanner*
   >
   > *Dad showed Mom a card.* / *Nobody could show a card.*
6. The turn passes to the next player.

### 5.2 ACCUSE
1. The active player picks 1 character, 1 room and 1 weapon and confirms **Accuse**.
2. The host checks the accusation against the envelope:
   - **Correct:** that player **wins**. The envelope is revealed to everyone and the game ends.
   - **Wrong:** that player is **out**. **All of their cards are revealed to every player** and labelled "Out: [player]" in everyone's grid. From then on they are a **spectator**: they stay connected and see everything other players can see (turns, ask banners, eliminations, the end of the game), but they don't take turns and aren't asked questions.
3. If only one player is left who hasn't been eliminated, that player wins.

---

## 6. Networking: plain TCP, host-authoritative

No middleware is needed. Everything is built on `System.Net.Sockets`:

| Purpose | Transport | Detail |
|---------|-----------|--------|
| **Finding games** ("view hosted games") | **UDP broadcast** | The host broadcasts `{gameName, hostName, ip, tcpPort, playerCount, status}` once a second on a fixed port (for example 47777). Joining players listen and build the game list. TCP alone can't do this because it needs a known IP before it can connect. |
| **Gameplay** | **TCP** | Joining players connect to `hostIp:tcpPort` (for example 47778). One connection per player stays open for the whole game. |

### 6.1 Rules
- **The host is the server and the referee.** It owns the deck, the envelope, the hands, the turn order and all validation. Other players only send requests and display what they receive.
- **Messages:** JSON, with each message prefixed by its length (a 4-byte int followed by UTF-8 bytes). TCP is a stream, not a series of messages, so the prefix is how the receiver knows where one message ends.
- **Threading:** socket reads run on a background thread and push messages into a queue. A `MonoBehaviour` empties the queue in `Update()`, because Unity's API may only be called from the main thread.
- **Privacy:** the host sends each player only what they are allowed to see. Each player's hand goes to that player alone, and a shown card goes only to the player who asked.
- **Host's own player:** the host's UI goes through the same message handling locally, so there's only one code path.
- **Disconnects:** see 6.3.
- **Firewall:** on first launch, Windows asks to allow the game on private networks. The players must accept.

### 6.2 Messages

**Client → host**

| Type | Payload |
|------|---------|
| `JoinRequest` | `name` |
| `Rejoin` | `sessionToken` |
| `PickCharacter` | `characterId` |
| `Ask` | `characterId, roomId, weaponId` |
| `ShowCard` | `cardId` (or `null` for "I have none") |
| `Accuse` | `characterId, roomId, weaponId` |

**Host → clients**

| Type | Sent to | Payload |
|------|---------|---------|
| `LobbyState` | all | players `[ {id, name, characterId} ]`, takenCharacters, canStart |
| `CharacterRejected` | one player | reason |
| `GameStarted` | all | playerOrder |
| `DealHand` | one player | `cardIds[]` |
| `TurnStarted` | all | activePlayerId |
| `AskRequest` | the asked player | askerId, the 3 cards |
| `AskBroadcast` | all | askerId, askedId, the 3 cards |
| `CardShown` | the asker only | shownById, cardId |
| `AskResult` | all | shownById or "nobody" (no card) |
| `PlayerEliminated` | all | playerId, `cardIds[]` |
| `GameOver` | all | winnerId, envelope |
| `Welcome` | the joining player | playerId, `sessionToken` |
| `PlayerDisconnected` | all | playerId |
| `PlayerRejoined` | all | playerId |
| `PlayerEjected` | all | playerId, `cardIds[]` |
| `StateSync` | the rejoining player | full view: lobby, hand, grid labels, turn, pending ask, eliminations |

### 6.3 Disconnects and rejoining
- **In the lobby:** the player is removed and their character is freed.
- **Mid-game:**
  1. The host detects the dropped connection and tells everyone (`PlayerDisconnected`). The player's profile image is greyed out with a "Disconnected" badge.
  2. The **host** sees two buttons: **Wait** or **Eject**.
     - **Wait:** the game pauses whenever it needs that player (their turn, or a question reaches them). Everything else stays as it is.
     - **Eject:** the player is removed for good. Like a wrong accusation, **all their cards are revealed to everyone**, so the game can still be solved. They are skipped from then on.
  3. **Rejoining:** when a player first joins, the host gives them a `sessionToken`, which their device keeps for the rest of the game. If they reconnect (JOIN, pick the same game), the client sends `Rejoin` with that token. If the player hasn't been ejected, the host restores their seat and sends a `StateSync` with everything that player is allowed to see (their hand, labels on their grid, whose turn it is, and any question waiting on them). Play then continues.
  4. A player who was ejected can still rejoin as a spectator.
- **If the host drops, the game ends** (the host holds all the game state). Supporting a host rejoin would mean saving the game state to disk, which is out of scope for now.

### 6.4 Middleware fallback
Plain TCP is enough for a turn-based LAN game. Switch only if you need play over the **internet**: Unity Netcode for GameObjects with Unity Relay and Lobby handles NAT and matchmaking. The message set above stays the same, so the switch wouldn't change the game design.

---

## 7. Unity project structure (suggested)

```
Assets/
  Art/Cards/{Characters,Rooms,Weapons}/   ← copied from MyResources
  Audio/introbackground.wav
  Data/CardDatabase.asset                 ← ScriptableObject: id, type, sprite
  Scenes/  StartMenu, Lobby, Game
  Scripts/
    Net/   LanDiscovery.cs, TcpHost.cs, TcpClientConn.cs, MessageCodec.cs, Messages.cs
    Game/  GameHost.cs (rules + state, host only), GameClient.cs (local view state)
    UI/    StartMenuUI, GameListUI, CharacterPickerUI, LobbyUI,
           CardGridUI, AskPanelUI, ShowCardPanelUI, BannerUI, AccusePanelUI
```

---

## 8. Decisions
1. **Profile image:** the character the player picks.
2. **Who gets asked:** the question passes from player to player until someone shows a card or it gets back to the asker.
3. **Eliminated players:** don't answer questions, but stay in the game as spectators.
4. **Player cap:** 8 players (one per character).
5. **Mid-game disconnect:** the player can rejoin. Meanwhile the host chooses **Wait** or **Eject**, and ejecting reveals the player's cards.
