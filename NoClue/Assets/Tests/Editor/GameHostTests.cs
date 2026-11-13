using System.Collections.Generic;
using System.Linq;
using NoClue.Core;
using NoClue.Game;
using NoClue.Net;
using NUnit.Framework;
using UnityEngine;

namespace NoClue.Tests
{
    public class GameHostTests
    {
        /// <summary>Records what the host sends; connection N = player N's socket.</summary>
        private sealed class FakeTransport : IHostTransport
        {
            public readonly Dictionary<int, GameView> Views = new();
            public readonly Dictionary<int, WelcomeMsg> Welcomes = new();
            public readonly List<(int conn, RejectedMsg msg)> Rejections = new();
            public readonly List<int> Kicked = new();

            public void Send(int conn, string type, object payload)
            {
                // round-trip through JSON, exactly like the wire
                string json = JsonUtility.ToJson(payload);
                switch (type)
                {
                    case MsgType.View: Views[conn] = JsonUtility.FromJson<GameView>(json); break;
                    case MsgType.Welcome: Welcomes[conn] = JsonUtility.FromJson<WelcomeMsg>(json); break;
                    case MsgType.Rejected: Rejections.Add((conn, JsonUtility.FromJson<RejectedMsg>(json))); break;
                }
            }

            public void Kick(int conn) => Kicked.Add(conn);
        }

        private FakeTransport _net;
        private GameHost _host;

        private void Msg(int conn, string type, object payload) =>
            _host.HandleMessage(conn, new Envelope { type = type, payload = JsonUtility.ToJson(payload) });

        /// <summary>Host (conn 0) plus <paramref name="others"/> joiners on conns 1..n, each with a character.</summary>
        private void SetUpLobby(int others)
        {
            _net = new FakeTransport();
            _host = new GameHost(_net, seed: 1234) { RollDie = () => 0 };   // "Ask next" unless a test says otherwise
            for (int conn = 0; conn <= others; conn++)
            {
                Msg(conn, MsgType.JoinRequest, new JoinRequestMsg { name = "P" + conn });
                Msg(conn, MsgType.PickCharacter, new PickCharacterMsg { characterId = CardCatalog.Characters[conn] });
            }
        }

        private int PlayerIdOf(int conn) => _net.Welcomes[conn].playerId;
        private GameView ViewOf(int conn) => _net.Views[conn];

        private void Start() => Msg(GameHost.LocalConnection, MsgType.StartGame, new StartGameMsg());

        [Test]
        public void CannotPickACharacterSomeoneElseHas()
        {
            SetUpLobby(1);
            Msg(1, MsgType.PickCharacter, new PickCharacterMsg { characterId = CardCatalog.Characters[0] });

            Assert.That(_net.Rejections.Any(r => r.conn == 1));
            var me = ViewOf(1).players.Single(p => p.id == PlayerIdOf(1));
            Assert.AreEqual(CardCatalog.Characters[1], me.characterId);
        }

        [Test]
        public void OnlyHostCanStart_AndNeedsTwoPlayers()
        {
            SetUpLobby(0);
            Start();
            Assert.AreEqual(Phase.Lobby, _host.Phase);

            SetUpLobby(1);
            Msg(1, MsgType.StartGame, new StartGameMsg());
            Assert.AreEqual(Phase.Lobby, _host.Phase);

            Start();
            Assert.AreEqual(Phase.Playing, _host.Phase);
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(4)]
        [TestCase(7)]
        public void DealsEveryCardExceptTheSolution_Evenly(int others)
        {
            SetUpLobby(others);
            Start();

            var solution = _host.SolutionForTests;
            Assert.AreEqual(CardType.Character, CardCatalog.TypeOf(solution[0]));
            Assert.AreEqual(CardType.Room, CardCatalog.TypeOf(solution[1]));
            Assert.AreEqual(CardType.Weapon, CardCatalog.TypeOf(solution[2]));

            var hands = Enumerable.Range(0, others + 1).Select(c => ViewOf(c).hand).ToList();
            var dealt = hands.SelectMany(h => h).ToList();
            Assert.AreEqual(19, dealt.Count);
            CollectionAssert.AllItemsAreUnique(dealt.Concat(solution));
            Assert.LessOrEqual(hands.Max(h => h.Length) - hands.Min(h => h.Length), 1);

            foreach (var c in Enumerable.Range(0, others + 1))
                Assert.That(ViewOf(c).notes.Where(n => n.kind == NoteKind.Owned).Select(n => n.cardId),
                    Is.EquivalentTo(ViewOf(c).hand));
        }

        [Test]
        public void JoinAfterStartIsRejected()
        {
            SetUpLobby(1);
            Start();
            Msg(5, MsgType.JoinRequest, new JoinRequestMsg { name = "Late" });
            Assert.That(_net.Rejections.Any(r => r.conn == 5 && r.msg.fatal));
            Assert.Contains(5, _net.Kicked);
        }

        [Test]
        public void AskPassesOnUntilSomeoneShows_AndOnlyAskerLearnsTheCard()
        {
            SetUpLobby(2);
            Start();

            // Ask about cards held by player on conn 2 only, so conn 1 must say "none".
            var hand2 = ViewOf(2).hand;
            string c = hand2.FirstOrDefault(x => CardCatalog.TypeOf(x) == CardType.Character) ?? _host.SolutionForTests[0];
            string r = hand2.FirstOrDefault(x => CardCatalog.TypeOf(x) == CardType.Room) ?? _host.SolutionForTests[1];
            string w = hand2.FirstOrDefault(x => CardCatalog.TypeOf(x) == CardType.Weapon) ?? _host.SolutionForTests[2];
            Assume.That(new[] { c, r, w }.Any(hand2.Contains));
            Assume.That(!new[] { c, r, w }.Any(ViewOf(1).hand.Contains));

            Msg(0, MsgType.Roll, new SuggestionMsg { characterId = c, roomId = r, weaponId = w });
            Assert.AreEqual(PlayerIdOf(1), ViewOf(0).ask.responderId);

            Msg(1, MsgType.ShowCard, new ShowCardMsg { cardId = "" });
            Assert.AreEqual(PlayerIdOf(2), ViewOf(0).ask.responderId);

            string shown = new[] { c, r, w }.First(hand2.Contains);
            Msg(2, MsgType.ShowCard, new ShowCardMsg { cardId = shown });

            var asker = ViewOf(0);
            Assert.IsFalse(asker.ask.active);
            Assert.AreEqual(shown, asker.lastShownCardId);
            var note = asker.notes.Single(n => n.cardId == shown);
            Assert.AreEqual(NoteKind.ShownBy, note.kind);
            Assert.AreEqual(PlayerIdOf(2), note.byPlayerId);

            Assert.IsFalse(ViewOf(1).notes.Any(n => n.cardId == shown), "bystander must not learn the card");

            // the turn waits until the asker has looked at the card and clicked OK
            Assert.AreEqual(PlayerIdOf(0), ViewOf(1).awaitingAckFrom);
            Assert.AreEqual(PlayerIdOf(0), ViewOf(1).activePlayerId);
            Msg(1, MsgType.AckShown, new AckShownMsg());   // only the asker can acknowledge
            Assert.AreEqual(PlayerIdOf(0), ViewOf(1).activePlayerId);

            Msg(0, MsgType.AckShown, new AckShownMsg());
            Assert.AreEqual(-1, ViewOf(1).awaitingAckFrom);
            Assert.AreEqual(PlayerIdOf(1), ViewOf(0).activePlayerId, "turn passes to the next player");
        }

        [Test]
        public void CannotSayNoneWhileHoldingACard()
        {
            SetUpLobby(1);
            Start();
            var hand1 = ViewOf(1).hand;
            string c = hand1.FirstOrDefault(x => CardCatalog.TypeOf(x) == CardType.Character) ?? _host.SolutionForTests[0];
            Msg(0, MsgType.Roll, new SuggestionMsg { characterId = c, roomId = _host.SolutionForTests[1], weaponId = _host.SolutionForTests[2] });
            Assume.That(hand1.Contains(c));

            Msg(1, MsgType.ShowCard, new ShowCardMsg { cardId = "" });
            Assert.IsTrue(ViewOf(0).ask.active);
            Assert.That(_net.Rejections.Any(x => x.conn == 1));
        }

        [Test]
        public void NobodyHasThem_AskerIsToldAndTurnWaitsForOk()
        {
            SetUpLobby(1);
            Start();
            var s = _host.SolutionForTests;
            Msg(0, MsgType.Roll, new SuggestionMsg { characterId = s[0], roomId = s[1], weaponId = s[2] });
            Msg(1, MsgType.ShowCard, new ShowCardMsg { cardId = "" });

            var v = ViewOf(0);
            Assert.IsFalse(v.ask.active);
            Assert.AreEqual(PlayerIdOf(0), v.awaitingAckFrom);
            Assert.AreEqual(AckKind.NobodyShowed, v.ackKind);
            Assert.IsTrue(v.unanswered.active);
            Assert.AreEqual(s[1], v.unanswered.roomId);
            Assert.AreEqual(PlayerIdOf(0), v.activePlayerId, "turn waits");

            Msg(0, MsgType.AckShown, new AckShownMsg());
            Assert.AreEqual(PlayerIdOf(1), ViewOf(0).activePlayerId);
            Assert.AreEqual(-1, ViewOf(0).awaitingAckFrom);
        }

        [Test]
        public void WrongAccusation_KnocksOut_AndRevealsCardsToEveryone()
        {
            SetUpLobby(2);
            Start();
            var s = _host.SolutionForTests;
            string wrongRoom = CardCatalog.Rooms.First(x => x != s[1]);
            var hand0 = ViewOf(0).hand;

            Msg(0, MsgType.Accuse, new SuggestionMsg { characterId = s[0], roomId = wrongRoom, weaponId = s[2] });

            Assert.AreEqual(Phase.Playing, _host.Phase);
            Assert.IsTrue(ViewOf(1).players.Single(p => p.id == PlayerIdOf(0)).eliminated);
            foreach (int conn in new[] { 1, 2 })
            foreach (var card in hand0)
            {
                var n = ViewOf(conn).notes.Single(x => x.cardId == card);
                Assert.AreEqual(NoteKind.Out, n.kind);
                Assert.AreEqual(PlayerIdOf(0), n.byPlayerId);
            }
            Assert.AreEqual(PlayerIdOf(1), ViewOf(1).activePlayerId);

            // eliminated players are skipped when a question goes round
            Msg(1, MsgType.Roll, new SuggestionMsg { characterId = s[0], roomId = s[1], weaponId = s[2] });
            Assert.AreEqual(PlayerIdOf(2), ViewOf(1).ask.responderId);
            Msg(2, MsgType.ShowCard, new ShowCardMsg { cardId = "" });
            Assert.IsFalse(ViewOf(1).ask.active, "skips the out player and comes back to the asker");
        }

        [Test]
        public void CorrectAccusation_Wins()
        {
            SetUpLobby(1);
            Start();
            var s = _host.SolutionForTests;
            Msg(0, MsgType.Accuse, new SuggestionMsg { characterId = s[0], roomId = s[1], weaponId = s[2] });

            Assert.AreEqual(Phase.GameOver, _host.Phase);
            Assert.AreEqual(PlayerIdOf(0), ViewOf(1).winnerId);
            CollectionAssert.AreEqual(s, ViewOf(1).solution);
        }

        [Test]
        public void LastPlayerStanding_Wins()
        {
            SetUpLobby(1);
            Start();
            var s = _host.SolutionForTests;
            Msg(0, MsgType.Accuse, new SuggestionMsg { characterId = s[0], roomId = s[1], weaponId = CardCatalog.Weapons.First(x => x != s[2]) });

            Assert.AreEqual(Phase.GameOver, _host.Phase);
            Assert.AreEqual(PlayerIdOf(1), ViewOf(1).winnerId);
        }

        [Test]
        public void DisconnectedPlayer_CanRejoinTheirSeat()
        {
            SetUpLobby(2);
            Start();
            var token = _net.Welcomes[1].token;
            var hand = ViewOf(1).hand;

            _host.HandleDisconnected(1);
            Assert.IsFalse(ViewOf(0).players.Single(p => p.id == PlayerIdOf(1)).connected);

            Msg(9, MsgType.Rejoin, new RejoinMsg { token = token });
            Assert.AreEqual(PlayerIdOf(1), _net.Welcomes[9].playerId);
            CollectionAssert.AreEquivalent(hand, ViewOf(9).hand);
            Assert.IsTrue(ViewOf(0).players.Single(p => p.id == PlayerIdOf(1)).connected);
        }

        [Test]
        public void HostEjectsDisconnectedPlayer_CardsRevealed_QuestionMovesOn()
        {
            SetUpLobby(2);
            Start();
            var s = _host.SolutionForTests;
            var hand1 = ViewOf(1).hand;

            Msg(0, MsgType.Roll, new SuggestionMsg { characterId = s[0], roomId = s[1], weaponId = s[2] });
            Assert.AreEqual(PlayerIdOf(1), ViewOf(0).ask.responderId);

            _host.HandleDisconnected(1);
            Msg(2, MsgType.EjectPlayer, new EjectPlayerMsg { playerId = PlayerIdOf(1) });  // not the host: ignored
            Assert.AreEqual(PlayerIdOf(1), ViewOf(0).ask.responderId);

            Msg(0, MsgType.EjectPlayer, new EjectPlayerMsg { playerId = PlayerIdOf(1) });
            Assert.AreEqual(PlayerIdOf(2), ViewOf(0).ask.responderId);
            Assert.IsTrue(hand1.All(card => ViewOf(2).notes.Any(n => n.cardId == card && n.kind == NoteKind.Out)));
        }

        // ------------------------------------------------------------------ the die

        private const int SideAskNext = 0, SideShowAll = 4, SideReverse = 5;

        private SuggestionMsg Solution() => new()
        {
            characterId = _host.SolutionForTests[0], roomId = _host.SolutionForTests[1], weaponId = _host.SolutionForTests[2],
        };

        [Test]
        public void EveryRollIsSeenByAllPlayers()
        {
            SetUpLobby(2);
            Start();
            _host.RollDie = () => SideAskNext;
            Msg(0, MsgType.Roll, Solution());

            foreach (int conn in new[] { 0, 1, 2 })
            {
                Assert.AreEqual(1, ViewOf(conn).rollSeq);
                Assert.AreEqual(SideAskNext, ViewOf(conn).rollSide);
                Assert.AreEqual(PlayerIdOf(0), ViewOf(conn).rollerId);
            }
        }

        [Test]
        public void Reverse_AsksThePreviousPlayer_AndTurnsGoBackwards()
        {
            SetUpLobby(3);   // seats 0,1,2,3
            Start();
            _host.RollDie = () => SideReverse;
            Msg(0, MsgType.Roll, Solution());

            Assert.AreEqual(-1, ViewOf(0).direction);
            Assert.AreEqual(PlayerIdOf(3), ViewOf(0).ask.responderId, "reversed: the question goes to the last seat");

            // nobody holds solution cards: question goes 3 -> 2 -> 1 -> back to asker
            Msg(3, MsgType.ShowCard, new ShowCardMsg { cardId = "" });
            Assert.AreEqual(PlayerIdOf(2), ViewOf(0).ask.responderId);
            Msg(2, MsgType.ShowCard, new ShowCardMsg { cardId = "" });
            Msg(1, MsgType.ShowCard, new ShowCardMsg { cardId = "" });

            Assert.IsFalse(ViewOf(0).ask.active);
            Msg(0, MsgType.AckShown, new AckShownMsg());   // "nobody could show you a card" -> OK
            Assert.AreEqual(PlayerIdOf(3), ViewOf(0).activePlayerId, "reversed: next turn is the last seat");

            // reversing again restores the normal direction
            Msg(3, MsgType.Roll, Solution());
            Assert.AreEqual(1, ViewOf(0).direction);
            Assert.AreEqual(PlayerIdOf(0), ViewOf(0).ask.responderId);
        }

        [Test]
        public void ShowAll_RevealsTheChosenCardToEveryone()
        {
            SetUpLobby(2);
            Start();
            var hand2 = ViewOf(2).hand;
            string room = hand2.FirstOrDefault(x => CardCatalog.TypeOf(x) == CardType.Room);
            Assume.That(room, Is.Not.Null);

            _host.RollDie = () => SideShowAll;
            Msg(0, MsgType.Roll, new SuggestionMsg { characterId = _host.SolutionForTests[0], roomId = room, weaponId = _host.SolutionForTests[2] });
            Assert.IsTrue(ViewOf(0).revealChoice.active);
            Assert.IsFalse(ViewOf(0).ask.active, "no normal question on Show all");

            Msg(0, MsgType.RevealChoice, new RevealChoiceMsg { cardId = "Nope" });            // not one of the 3
            Assert.IsTrue(ViewOf(0).revealChoice.active);

            Msg(0, MsgType.RevealChoice, new RevealChoiceMsg { cardId = room });
            foreach (int conn in new[] { 0, 1 })
            {
                var note = ViewOf(conn).notes.Single(n => n.cardId == room);
                Assert.AreEqual(NoteKind.ShownBy, note.kind);
                Assert.AreEqual(PlayerIdOf(2), note.byPlayerId);
                Assert.AreEqual(PlayerIdOf(2), ViewOf(conn).publicRevealHolderId);
            }
            Assert.AreEqual(NoteKind.Owned, ViewOf(2).notes.Single(n => n.cardId == room).kind);

            // turn waits for the roller's OK
            Assert.AreEqual(PlayerIdOf(0), ViewOf(1).awaitingAckFrom);
            Assert.AreEqual(AckKind.PublicReveal, ViewOf(0).ackKind);
            Msg(0, MsgType.AckShown, new AckShownMsg());
            Assert.AreEqual(PlayerIdOf(1), ViewOf(0).activePlayerId);
        }

        [Test]
        public void ShowAll_NobodyElseHasIt()
        {
            SetUpLobby(1);
            Start();
            _host.RollDie = () => SideShowAll;
            Msg(0, MsgType.Roll, Solution());
            Msg(0, MsgType.RevealChoice, new RevealChoiceMsg { cardId = _host.SolutionForTests[1] });

            Assert.AreEqual(-1, ViewOf(1).publicRevealHolderId);
            Assert.AreEqual(_host.SolutionForTests[1], ViewOf(1).publicRevealCardId);
            Assert.IsFalse(ViewOf(1).notes.Any(n => n.cardId == _host.SolutionForTests[1]));
        }

        [Test]
        public void LobbyDisconnectFreesTheCharacter()
        {
            SetUpLobby(1);
            _host.HandleDisconnected(1);
            Msg(2, MsgType.JoinRequest, new JoinRequestMsg { name = "New" });
            Msg(2, MsgType.PickCharacter, new PickCharacterMsg { characterId = CardCatalog.Characters[1] });
            Assert.AreEqual(CardCatalog.Characters[1], ViewOf(2).players.Single(p => p.id == PlayerIdOf(2)).characterId);
        }
    }
}
