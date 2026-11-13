using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NoClue.Core
{
    public enum CardType { Character, Room, Weapon }

    /// <summary>
    /// The fixed set of cards. A card's id is its image file name under Resources/Cards/&lt;Group&gt;/.
    /// </summary>
    public static class CardCatalog
    {
        public static readonly string[] Characters =
            { "AntieLana", "Chane", "Dad", "GrandMa", "GrandPa", "Miane", "Mom", "UnclePeter" };

        public static readonly string[] Rooms =
            { "Bedroom", "Braai", "Garage", "GardenShed", "Kitchen", "Study", "SwimmingPool", "TVRoom" };

        public static readonly string[] Weapons =
            { "BraaiTongs", "CellCharger", "CoffeeMug", "ExtensionCord", "GardenFork", "Spanner" };

        public const int MaxPlayers = 8;

        private static readonly Dictionary<string, CardType> s_Types = BuildTypes();
        private static readonly Dictionary<string, Sprite> s_Sprites = new();

        public static IEnumerable<string> All => Characters.Concat(Rooms).Concat(Weapons);

        public static string[] OfType(CardType type) => type switch
        {
            CardType.Character => Characters,
            CardType.Room => Rooms,
            _ => Weapons,
        };

        public static bool Exists(string id) => id != null && s_Types.ContainsKey(id);

        public static CardType TypeOf(string id) => s_Types[id];

        /// <summary>"SwimmingPool" -> "Swimming Pool", "TVRoom" -> "TV Room".</summary>
        public static string DisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var sb = new System.Text.StringBuilder(id.Length + 4);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool boundary = i > 0 && char.IsUpper(c) &&
                                (char.IsLower(id[i - 1]) || (i + 1 < id.Length && char.IsLower(id[i + 1])));
                if (boundary) sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString();
        }

        public static Sprite GetSprite(string id)
        {
            if (!Exists(id)) return null;
            if (s_Sprites.TryGetValue(id, out var sprite)) return sprite;

            string folder = TypeOf(id) switch
            {
                CardType.Character => "Characters",
                CardType.Room => "Rooms",
                _ => "Weapons",
            };
            sprite = Resources.Load<Sprite>($"Cards/{folder}/{id}");
            if (sprite == null) Debug.LogWarning($"[NoClue] Missing card sprite: Cards/{folder}/{id}");
            s_Sprites[id] = sprite;
            return sprite;
        }

        private static Dictionary<string, CardType> BuildTypes()
        {
            var d = new Dictionary<string, CardType>();
            foreach (var c in Characters) d[c] = CardType.Character;
            foreach (var r in Rooms) d[r] = CardType.Room;
            foreach (var w in Weapons) d[w] = CardType.Weapon;
            return d;
        }
    }
}
