using System;

namespace NoClue.Net
{
    /// <summary>Wire format: every TCP frame is one Envelope serialized with JsonUtility.</summary>
    [Serializable]
    public class Envelope
    {
        public string type;
        public string payload;
    }

    public static class MsgType
    {
        // client -> host
        public const string JoinRequest = "JoinRequest";
        public const string Rejoin = "Rejoin";
        public const string PickCharacter = "PickCharacter";
        public const string StartGame = "StartGame";
        public const string Roll = "Roll";                 // pick 3 cards, then spin the die
        public const string RevealChoice = "RevealChoice"; // after rolling "Show all": which of the 3 cards
        public const string ShowCard = "ShowCard";
        public const string AckShown = "AckShown";
        public const string Accuse = "Accuse";
        public const string EjectPlayer = "EjectPlayer";

        // host -> client
        public const string Welcome = "Welcome";
        public const string Rejected = "Rejected";
        public const string View = "View";
        public const string Toast = "Toast";
    }

    // ---------- client -> host ----------

    [Serializable] public class JoinRequestMsg { public string name; }
    [Serializable] public class RejoinMsg { public string token; }
    [Serializable] public class PickCharacterMsg { public string characterId; }
    [Serializable] public class StartGameMsg { }
    [Serializable] public class SuggestionMsg { public string characterId; public string roomId; public string weaponId; }
    /// <summary>cardId empty = "I have none".</summary>
    [Serializable] public class ShowCardMsg { public string cardId; }
    [Serializable] public class EjectPlayerMsg { public int playerId; }
    /// <summary>The asker clicked OK on the card they were shown; the turn may move on.</summary>
    [Serializable] public class AckShownMsg { }
    [Serializable] public class RevealChoiceMsg { public string cardId; }

    // ---------- the die ----------

    public enum DieFace { AskNext, ShowAll, Reverse }

    public static class Die
    {
        public const int Sides = 6;

        /// <summary>Faces 0-3 ask the next player, 4 shows a card to everyone, 5 reverses play direction.</summary>
        public static DieFace FaceOf(int side) => side switch
        {
            4 => DieFace.ShowAll,
            5 => DieFace.Reverse,
            _ => DieFace.AskNext,
        };

        public static string Label(DieFace face) => face switch
        {
            DieFace.ShowAll => "SHOW ALL",
            DieFace.Reverse => "REVERSE",
            _ => "ASK NEXT",
        };
    }

    // ---------- host -> client ----------

    [Serializable] public class WelcomeMsg { public int playerId; public string token; public string gameId; }

    /// <summary>fatal = the connection is about to be closed (game full, in progress, ...).</summary>
    [Serializable] public class RejectedMsg { public string reason; public bool fatal; }

    [Serializable] public class ToastMsg { public string text; }

    // ---------- the per-player snapshot ----------

    public enum Phase { Lobby, Playing, GameOver }

    public enum NoteKind { None, Owned, ShownBy, Out }

    /// <summary>What the player the game is waiting on has to click OK on.</summary>
    public enum AckKind { None, ShownToMe, PublicReveal, NobodyShowed }

    [Serializable]
    public class PlayerView
    {
        public int id;
        public string name;
        public string characterId;   // also the profile image
        public bool isHost;
        public bool connected;
        public bool eliminated;      // wrong accusation
        public bool ejected;         // removed by host after disconnect
        public int handCount;
    }

    [Serializable]
    public class CardNote
    {
        public string cardId;
        public NoteKind kind;
        public int byPlayerId;       // ShownBy: who showed it. Out: whose card it was.
    }

    [Serializable]
    public class AskView
    {
        public bool active;
        public int askerId;
        public int responderId;      // whose answer we are waiting for
        public string characterId;
        public string roomId;
        public string weaponId;
    }

    [Serializable]
    public class GameView
    {
        public Phase phase;
        public string gameId;
        public int myPlayerId;
        public int activePlayerId = -1;
        public PlayerView[] players = Array.Empty<PlayerView>();
        public string[] hand = Array.Empty<string>();
        public CardNote[] notes = Array.Empty<CardNote>();
        public AskView ask = new();
        public string[] log = Array.Empty<string>();

        // +1 = clockwise (seat order), -1 = reversed.
        public int direction = 1;

        // The last die throw; clients animate it once per new rollSeq.
        public int rollSeq;
        public int rollSide = -1;
        public int rollerId = -1;

        // "Show all": the roller is choosing which of their 3 cards to reveal to everyone.
        public AskView revealChoice = new();

        // The last public reveal (everyone sees it). holderId -1 = nobody else holds that card.
        public int publicRevealSeq;
        public string publicRevealCardId;
        public int publicRevealHolderId = -1;
        public int publicRevealAskerId = -1;

        // After a card is shown (privately or to all), the turn waits until this player clicks OK. -1 = not waiting.
        public int awaitingAckFrom = -1;
        public AckKind ackKind;

        // NobodyShowed: the question that went all the way round unanswered.
        public AskView unanswered = new();

        // Last card shown to me; seq increases with every new card.
        public int lastShownSeq;
        public string lastShownCardId;
        public int lastShownById;

        public int winnerId = -1;
        public string[] solution = Array.Empty<string>(); // only filled at GameOver
    }
}
