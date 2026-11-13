using System;
using System.Collections.Generic;
using System.Linq;
using NoClue.Core;
using NoClue.Net;

namespace NoClue.Game
{
    /// <summary>How the host reaches players. Connection 0 is always the host's own local player.</summary>
    public interface IHostTransport
    {
        void Send(int connectionId, string type, object payload);
        void Kick(int connectionId);
    }

    /// <summary>
    /// The referee. Owns the deck, the hidden solution, every hand and the turn order,
    /// validates every request, and after each change sends each player their own <see cref="GameView"/>.
    /// </summary>
    public sealed class GameHost
    {
        public const int LocalConnection = 0;
        private const int MaxLogLines = 40;

        private sealed class Player
        {
            public int Id;
            public string Name;
            public string CharacterId;
            public string Token;
            public int ConnectionId = -1;          // -1 = disconnected
            public bool IsHost;
            public bool Eliminated;
            public bool Ejected;
            public readonly List<string> Hand = new();
            public readonly Dictionary<string, CardNote> Notes = new();
            public int LastShownSeq;
            public string LastShownCardId;
            public int LastShownById;

            public bool Connected => ConnectionId >= 0;
            public bool InPlay => !Eliminated && !Ejected;
        }

        private sealed class PendingAsk
        {
            public int AskerId;
            public int ResponderId;
            public string CharacterId, RoomId, WeaponId;
            public IEnumerable<string> Cards => new[] { CharacterId, RoomId, WeaponId };
        }

        private readonly IHostTransport _transport;
        private readonly Random _rng;
        private readonly List<Player> _players = new();   // seat order = join order, host first
        private readonly List<string> _log = new();
        private int _nextPlayerId = 1;
        private int _activeSeat = -1;
        private PendingAsk _ask;
        private PendingAsk _revealChoice;    // rolled "Show all": waiting for the roller to pick 1 of their 3 cards
        private PendingAsk _unanswered;      // the last question nobody could answer (shown to the asker until OK)
        private int _awaitingAckFrom = -1;   // asker still looking at the card they were shown
        private AckKind _ackKind;
        private int _direction = 1;          // +1 seat order, -1 reversed
        private int _rollSeq, _rollSide = -1, _rollerId = -1;
        private int _publicRevealSeq, _publicRevealHolderId = -1, _publicRevealAskerId = -1;
        private string _publicRevealCardId;
        private string[] _solution;
        private int _winnerId = -1;

        /// <summary>Returns a die side 0..5. Replaceable so tests can force a face.</summary>
        public Func<int> RollDie;

        public string GameId { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public Phase Phase { get; private set; } = Phase.Lobby;

        public GameHost(IHostTransport transport, int? seed = null)
        {
            _transport = transport;
            _rng = seed.HasValue ? new Random(seed.Value) : new Random();
            RollDie = () => _rng.Next(Die.Sides);
        }

        // ------------------------------------------------------------------ queries

        public int PlayerCount => _players.Count;
        public bool CanStart => Phase == Phase.Lobby && _players.Count >= 2 && _players.All(p => p.CharacterId != null);
        public IReadOnlyList<string> SolutionForTests => _solution;
        public IReadOnlyList<string> HandOf(int playerId) => Find(playerId)?.Hand;
        public int ActivePlayerId => _activeSeat >= 0 && _activeSeat < _players.Count ? _players[_activeSeat].Id : -1;

        public GameAdvert BuildAdvert(int tcpPort)
        {
            var host = _players.FirstOrDefault(p => p.IsHost);
            return new GameAdvert
            {
                gameId = GameId,
                hostName = host?.Name ?? "Host",
                hostCharacterId = host?.CharacterId,
                tcpPort = tcpPort,
                playerCount = _players.Count,
                maxPlayers = CardCatalog.MaxPlayers,
                inProgress = Phase != Phase.Lobby,
            };
        }

        // ------------------------------------------------------------------ connection events

        public void HandleDisconnected(int connectionId)
        {
            var p = _players.FirstOrDefault(x => x.ConnectionId == connectionId);
            if (p == null) return;

            if (Phase == Phase.Lobby)
            {
                _players.Remove(p);   // frees the character
                Log($"{p.Name} left");
            }
            else
            {
                p.ConnectionId = -1;
                Log(p.Ejected || p.Eliminated ? $"{p.Name} stopped watching" : $"{p.Name} disconnected");
            }
            Broadcast();
        }

        public void HandleMessage(int connectionId, Envelope env)
        {
            switch (env.type)
            {
                case MsgType.JoinRequest: OnJoin(connectionId, FramedConnection.Read<JoinRequestMsg>(env)); break;
                case MsgType.Rejoin: OnRejoin(connectionId, FramedConnection.Read<RejoinMsg>(env)); break;
                case MsgType.PickCharacter: OnPickCharacter(connectionId, FramedConnection.Read<PickCharacterMsg>(env)); break;
                case MsgType.StartGame: OnStartGame(connectionId); break;
                case MsgType.Roll: OnRoll(connectionId, FramedConnection.Read<SuggestionMsg>(env)); break;
                case MsgType.RevealChoice: OnRevealChoice(connectionId, FramedConnection.Read<RevealChoiceMsg>(env)); break;
                case MsgType.ShowCard: OnShowCard(connectionId, FramedConnection.Read<ShowCardMsg>(env)); break;
                case MsgType.AckShown: OnAckShown(connectionId); break;
                case MsgType.Accuse: OnAccuse(connectionId, FramedConnection.Read<SuggestionMsg>(env)); break;
                case MsgType.EjectPlayer: OnEject(connectionId, FramedConnection.Read<EjectPlayerMsg>(env)); break;
            }
        }

        // ------------------------------------------------------------------ lobby

        private void OnJoin(int conn, JoinRequestMsg msg)
        {
            if (BySender(conn) != null) return;
            if (Phase != Phase.Lobby) { RejectFatal(conn, "That game has already started."); return; }
            if (_players.Count >= CardCatalog.MaxPlayers) { RejectFatal(conn, "That game is full."); return; }

            string name = string.IsNullOrWhiteSpace(msg.name) ? $"Player {_nextPlayerId}" : msg.name.Trim();
            if (name.Length > 20) name = name.Substring(0, 20);

            var p = new Player
            {
                Id = _nextPlayerId++,
                Name = name,
                Token = Guid.NewGuid().ToString("N"),
                ConnectionId = conn,
                IsHost = conn == LocalConnection,
            };
            _players.Add(p);
            _transport.Send(conn, MsgType.Welcome, new WelcomeMsg { playerId = p.Id, token = p.Token, gameId = GameId });
            Log($"{p.Name} joined");
            Broadcast();
        }

        private void OnRejoin(int conn, RejoinMsg msg)
        {
            var p = _players.FirstOrDefault(x => x.Token == msg.token);
            if (p == null)
            {
                // Unknown seat (e.g. it was a lobby seat that got freed). Client falls back to a normal join.
                _transport.Send(conn, MsgType.Rejected, new RejectedMsg { reason = "rejoin-unknown" });
                return;
            }
            if (p.Connected && p.ConnectionId != conn) _transport.Kick(p.ConnectionId);

            p.ConnectionId = conn;
            _transport.Send(conn, MsgType.Welcome, new WelcomeMsg { playerId = p.Id, token = p.Token, gameId = GameId });
            Log(p.Ejected ? $"{p.Name} is back (watching)" : $"{p.Name} reconnected");
            Broadcast();
        }

        private void OnPickCharacter(int conn, PickCharacterMsg msg)
        {
            var p = BySender(conn);
            if (p == null || Phase != Phase.Lobby) return;
            if (!CardCatalog.Exists(msg.characterId) || CardCatalog.TypeOf(msg.characterId) != CardType.Character)
            {
                Reject(conn, "That is not a character.");
                return;
            }
            var owner = _players.FirstOrDefault(x => x.CharacterId == msg.characterId);
            if (owner != null && owner != p)
            {
                Reject(conn, $"{CardCatalog.DisplayName(msg.characterId)} is already taken by {owner.Name}.");
                Broadcast();   // refresh their picker with the latest taken list
                return;
            }
            p.CharacterId = msg.characterId;
            Broadcast();
        }

        private void OnStartGame(int conn)
        {
            if (conn != LocalConnection || Phase != Phase.Lobby) return;
            if (!CanStart)
            {
                Reject(conn, "Need at least 2 players, and everyone must pick a character.");
                return;
            }

            Deal();
            Phase = Phase.Playing;
            _activeSeat = 0;
            Log($"Game started with {_players.Count} players. {_players[0].Name} goes first.");
            Broadcast();
        }

        private void Deal()
        {
            var characters = Shuffled(CardCatalog.Characters);
            var rooms = Shuffled(CardCatalog.Rooms);
            var weapons = Shuffled(CardCatalog.Weapons);

            _solution = new[] { characters[0], rooms[0], weapons[0] };

            // Characters first, then rooms, then weapons, round-robin across seats so hands stay even.
            var deck = characters.Skip(1).Concat(rooms.Skip(1)).Concat(weapons.Skip(1));
            int seat = 0;
            foreach (var card in deck)
            {
                _players[seat].Hand.Add(card);
                seat = (seat + 1) % _players.Count;
            }

            foreach (var p in _players)
            foreach (var card in p.Hand)
                p.Notes[card] = new CardNote { cardId = card, kind = NoteKind.Owned, byPlayerId = p.Id };
        }

        // ------------------------------------------------------------------ turns

        /// <summary>The active player picked 3 cards and spun the die; the host throws it.</summary>
        private void OnRoll(int conn, SuggestionMsg msg)
        {
            var p = BySender(conn);
            if (!IsActiveAndFree(p, conn) || !ValidSuggestion(conn, msg)) return;

            int side = ((RollDie() % Die.Sides) + Die.Sides) % Die.Sides;
            var face = Die.FaceOf(side);
            _rollSeq++;
            _rollSide = side;
            _rollerId = p.Id;
            var picked = new PendingAsk
            {
                AskerId = p.Id,
                CharacterId = msg.characterId,
                RoomId = msg.roomId,
                WeaponId = msg.weaponId,
            };
            Log($"{p.Name} rolled {Die.Label(face)}!");

            switch (face)
            {
                case DieFace.ShowAll:
                    _revealChoice = picked;   // wait for them to pick which card everyone sees
                    break;

                case DieFace.Reverse:
                    _direction = -_direction;
                    Log("Play direction is reversed!");
                    StartAsk(p, picked);
                    break;

                default:
                    StartAsk(p, picked);
                    break;
            }
            Broadcast();
        }

        private void StartAsk(Player asker, PendingAsk ask)
        {
            _ask = ask;
            Log($"{asker.Name} asks: {Describe(ask.CharacterId, ask.RoomId, ask.WeaponId)}");
            PassAskOnFrom(asker.Id);
        }

        /// <summary>"Show all": the roller picked one of their 3 cards; whoever else holds it shows it to everyone.</summary>
        private void OnRevealChoice(int conn, RevealChoiceMsg msg)
        {
            var p = BySender(conn);
            if (p == null || Phase != Phase.Playing || _revealChoice == null || _revealChoice.AskerId != p.Id) return;
            if (!_revealChoice.Cards.Contains(msg.cardId)) { Reject(conn, "Pick one of the 3 cards you chose."); return; }

            _revealChoice = null;
            var holder = _players.FirstOrDefault(x => x != p && x.Hand.Contains(msg.cardId));
            string card = CardCatalog.DisplayName(msg.cardId);

            if (holder != null)
            {
                foreach (var other in _players)
                {
                    if (other == holder) continue;   // stays "Owned" for them
                    other.Notes[msg.cardId] = new CardNote { cardId = msg.cardId, kind = NoteKind.ShownBy, byPlayerId = holder.Id };
                }
                Log($"Revealed to everyone: {holder.Name} has {card}!");
            }
            else
            {
                Log($"Nobody else holds {card}!");
            }

            _publicRevealSeq++;
            _publicRevealCardId = msg.cardId;
            _publicRevealHolderId = holder?.Id ?? -1;
            _publicRevealAskerId = p.Id;
            _awaitingAckFrom = p.Id;
            _ackKind = AckKind.PublicReveal;
            Broadcast();
        }

        /// <summary>Moves the pending question to the next player in play after <paramref name="afterPlayerId"/>,
        /// following the current play direction.</summary>
        private void PassAskOnFrom(int afterPlayerId)
        {
            int seat = SeatOf(afterPlayerId);
            for (int i = 1; i < _players.Count; i++)
            {
                var candidate = _players[SeatStep(seat, i)];
                if (candidate.Id == _ask.AskerId) break;     // went all the way round
                if (!candidate.InPlay) continue;             // out players' cards are already public
                _ask.ResponderId = candidate.Id;
                return;
            }

            Log("Nobody could show a card.");
            // A big clue: let the asker take it in. The turn waits for their OK.
            var asker = Find(_ask.AskerId);
            _unanswered = _ask;
            _ask = null;
            if (asker != null && asker.InPlay)
            {
                _awaitingAckFrom = asker.Id;
                _ackKind = AckKind.NobodyShowed;
            }
            else AdvanceTurn();
        }

        private void OnShowCard(int conn, ShowCardMsg msg)
        {
            var responder = BySender(conn);
            if (responder == null || Phase != Phase.Playing || _ask == null || _ask.ResponderId != responder.Id) return;

            var holding = _ask.Cards.Where(responder.Hand.Contains).ToList();

            if (string.IsNullOrEmpty(msg.cardId))
            {
                if (holding.Count > 0) { Reject(conn, "You hold one of these cards, so you must show one."); return; }
                Log($"{responder.Name} has none of those.");
                PassAskOnFrom(responder.Id);
                Broadcast();
                return;
            }

            if (!holding.Contains(msg.cardId)) { Reject(conn, "You can only show a card you hold from the question."); return; }

            var asker = Find(_ask.AskerId);
            if (asker != null)
            {
                asker.Notes[msg.cardId] = new CardNote { cardId = msg.cardId, kind = NoteKind.ShownBy, byPlayerId = responder.Id };
                asker.LastShownSeq++;
                asker.LastShownCardId = msg.cardId;
                asker.LastShownById = responder.Id;
            }
            Log($"{responder.Name} showed {asker?.Name ?? "?"} a card.");
            _ask = null;
            // The turn only moves on once the asker has looked at the card and clicked OK.
            if (asker != null) { _awaitingAckFrom = asker.Id; _ackKind = AckKind.ShownToMe; }
            else AdvanceTurn();
            Broadcast();
        }

        private void OnAckShown(int conn)
        {
            var p = BySender(conn);
            if (p == null || Phase != Phase.Playing || p.Id != _awaitingAckFrom) return;
            _awaitingAckFrom = -1;
            _ackKind = AckKind.None;
            AdvanceTurn();
            Broadcast();
        }

        private void OnAccuse(int conn, SuggestionMsg msg)
        {
            var p = BySender(conn);
            if (!IsActiveAndFree(p, conn) || !ValidSuggestion(conn, msg)) return;

            string guess = Describe(msg.characterId, msg.roomId, msg.weaponId);
            bool correct = msg.characterId == _solution[0] && msg.roomId == _solution[1] && msg.weaponId == _solution[2];

            if (correct)
            {
                Log($"{p.Name} accuses: {guess}. CORRECT!");
                EndGame(p.Id);
            }
            else
            {
                Log($"{p.Name} accuses: {guess}. Wrong, {p.Name} is out!");
                RemoveFromPlay(p, eject: false);
                if (!CheckLastPlayerStanding()) AdvanceTurn();
            }
            Broadcast();
        }

        private void OnEject(int conn, EjectPlayerMsg msg)
        {
            if (conn != LocalConnection || Phase != Phase.Playing) return;
            var p = Find(msg.playerId);
            if (p == null || p.IsHost || p.Connected || p.Ejected) return;

            Log($"{p.Name} was removed by the host. Their cards are now shown to everyone.");
            int seat = SeatOf(p.Id);
            bool wasActive = seat == _activeSeat;
            RemoveFromPlay(p, eject: true);

            if (!CheckLastPlayerStanding())
            {
                if (_awaitingAckFrom == p.Id) { _awaitingAckFrom = -1; _ackKind = AckKind.None; AdvanceTurn(); }
                else if (_revealChoice != null && _revealChoice.AskerId == p.Id) { _revealChoice = null; AdvanceTurn(); }
                else if (_ask != null && _ask.AskerId == p.Id) { _ask = null; AdvanceTurn(); }
                else if (_ask != null && _ask.ResponderId == p.Id) PassAskOnFrom(p.Id);
                else if (wasActive && _ask == null && _revealChoice == null) AdvanceTurn();
            }
            Broadcast();
        }

        private void RemoveFromPlay(Player p, bool eject)
        {
            bool alreadyRevealed = p.Eliminated;
            if (eject) p.Ejected = true; else p.Eliminated = true;
            if (alreadyRevealed) return;

            foreach (var other in _players)
            foreach (var card in p.Hand)
            {
                if (other == p) continue;
                other.Notes[card] = new CardNote { cardId = card, kind = NoteKind.Out, byPlayerId = p.Id };
            }
        }

        private bool CheckLastPlayerStanding()
        {
            var remaining = _players.Where(x => x.InPlay).ToList();
            if (remaining.Count > 1) return false;
            if (remaining.Count == 1) Log($"{remaining[0].Name} is the last detective standing!");
            EndGame(remaining.Count == 1 ? remaining[0].Id : -1);
            return true;
        }

        private void EndGame(int winnerId)
        {
            Phase = Phase.GameOver;
            _winnerId = winnerId;
            _ask = null;
            _revealChoice = null;
            _awaitingAckFrom = -1;
            _ackKind = AckKind.None;
            Log($"It was {Describe(_solution[0], _solution[1], _solution[2])}.");
        }

        private void AdvanceTurn()
        {
            if (Phase != Phase.Playing) return;
            for (int i = 1; i <= _players.Count; i++)
            {
                int seat = SeatStep(_activeSeat, i);
                if (!_players[seat].InPlay) continue;
                _activeSeat = seat;
                Log($"{_players[seat].Name}'s turn.");
                return;
            }
        }

        // ------------------------------------------------------------------ views

        private void Broadcast()
        {
            foreach (var p in _players.Where(x => x.Connected))
                _transport.Send(p.ConnectionId, MsgType.View, BuildView(p));
        }

        private GameView BuildView(Player me)
        {
            return new GameView
            {
                phase = Phase,
                gameId = GameId,
                myPlayerId = me.Id,
                activePlayerId = Phase == Phase.Playing ? ActivePlayerId : -1,
                players = _players.Select(p => new PlayerView
                {
                    id = p.Id,
                    name = p.Name,
                    characterId = p.CharacterId,
                    isHost = p.IsHost,
                    connected = p.Connected,
                    eliminated = p.Eliminated,
                    ejected = p.Ejected,
                    handCount = p.Hand.Count,
                }).ToArray(),
                hand = me.Hand.ToArray(),
                notes = me.Notes.Values.ToArray(),
                ask = _ask == null
                    ? new AskView()
                    : new AskView
                    {
                        active = true,
                        askerId = _ask.AskerId,
                        responderId = _ask.ResponderId,
                        characterId = _ask.CharacterId,
                        roomId = _ask.RoomId,
                        weaponId = _ask.WeaponId,
                    },
                log = _log.ToArray(),
                direction = _direction,
                rollSeq = _rollSeq,
                rollSide = _rollSide,
                rollerId = _rollerId,
                revealChoice = _revealChoice == null
                    ? new AskView()
                    : new AskView
                    {
                        active = true,
                        askerId = _revealChoice.AskerId,
                        characterId = _revealChoice.CharacterId,
                        roomId = _revealChoice.RoomId,
                        weaponId = _revealChoice.WeaponId,
                    },
                publicRevealSeq = _publicRevealSeq,
                publicRevealCardId = _publicRevealCardId,
                publicRevealHolderId = _publicRevealHolderId,
                publicRevealAskerId = _publicRevealAskerId,
                awaitingAckFrom = Phase == Phase.Playing ? _awaitingAckFrom : -1,
                ackKind = Phase == Phase.Playing ? _ackKind : AckKind.None,
                unanswered = _unanswered == null || _ackKind != AckKind.NobodyShowed
                    ? new AskView()
                    : new AskView
                    {
                        active = true,
                        askerId = _unanswered.AskerId,
                        characterId = _unanswered.CharacterId,
                        roomId = _unanswered.RoomId,
                        weaponId = _unanswered.WeaponId,
                    },
                lastShownSeq = me.LastShownSeq,
                lastShownCardId = me.LastShownCardId,
                lastShownById = me.LastShownById,
                winnerId = _winnerId,
                solution = Phase == Phase.GameOver ? _solution : Array.Empty<string>(),
            };
        }

        // ------------------------------------------------------------------ helpers

        private bool IsActiveAndFree(Player p, int conn)
        {
            if (p == null || Phase != Phase.Playing) return false;
            if (p.Id != ActivePlayerId) { Reject(conn, "It's not your turn."); return false; }
            if (_ask != null) { Reject(conn, "Wait for the current question to be answered."); return false; }
            if (_revealChoice != null) { Reject(conn, "Pick the card to show everyone first."); return false; }
            if (_awaitingAckFrom >= 0) { Reject(conn, "Click OK on the card you were shown first."); return false; }
            return true;
        }

        /// <summary>The seat <paramref name="steps"/> places from <paramref name="seat"/> in the current play direction.</summary>
        private int SeatStep(int seat, int steps)
        {
            int n = _players.Count;
            return (((seat + steps * _direction) % n) + n) % n;
        }

        private bool ValidSuggestion(int conn, SuggestionMsg m)
        {
            bool ok = CardCatalog.Exists(m.characterId) && CardCatalog.TypeOf(m.characterId) == CardType.Character &&
                      CardCatalog.Exists(m.roomId) && CardCatalog.TypeOf(m.roomId) == CardType.Room &&
                      CardCatalog.Exists(m.weaponId) && CardCatalog.TypeOf(m.weaponId) == CardType.Weapon;
            if (!ok) Reject(conn, "Pick one character, one room and one weapon.");
            return ok;
        }

        private Player BySender(int conn) => _players.FirstOrDefault(p => p.ConnectionId == conn);
        private Player Find(int id) => _players.FirstOrDefault(p => p.Id == id);
        private int SeatOf(int id) => _players.FindIndex(p => p.Id == id);

        private List<string> Shuffled(IEnumerable<string> source)
        {
            var list = source.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        private static string Describe(string c, string r, string w) =>
            $"{CardCatalog.DisplayName(c)} in the {CardCatalog.DisplayName(r)} with the {CardCatalog.DisplayName(w)}";

        private void Log(string line)
        {
            _log.Add(line);
            if (_log.Count > MaxLogLines) _log.RemoveAt(0);
        }

        private void Reject(int conn, string reason) =>
            _transport.Send(conn, MsgType.Rejected, new RejectedMsg { reason = reason });

        private void RejectFatal(int conn, string reason)
        {
            _transport.Send(conn, MsgType.Rejected, new RejectedMsg { reason = reason, fatal = true });
            if (conn != LocalConnection) _transport.Kick(conn);
        }
    }
}
