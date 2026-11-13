using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace NoClue.Net
{
    [Serializable]
    public class GameAdvert
    {
        public string magic = LanDiscovery.Magic;
        public string gameId;
        public string hostName;
        public string hostCharacterId;
        public int tcpPort;
        public int playerCount;
        public int maxPlayers;
        public bool inProgress;

        [NonSerialized] public string address;     // filled in by the listener from the packet source
        [NonSerialized] public float lastSeen;
    }

    /// <summary>Host side: broadcasts a <see cref="GameAdvert"/> about once a second.</summary>
    public sealed class LanBroadcaster : IDisposable
    {
        private readonly UdpClient _udp = new() { EnableBroadcast = true };
        private readonly List<IPEndPoint> _targets;
        private float _nextSend;

        public LanBroadcaster()
        {
            _targets = LanDiscovery.BroadcastAddresses()
                .Select(a => new IPEndPoint(a, LanDiscovery.Port)).ToList();
        }

        public void Tick(GameAdvert advert)
        {
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + 1f;

            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(advert));
            foreach (var target in _targets)
            {
                try { _udp.Send(data, data.Length, target); }
                catch (SocketException) { /* adapter down; try the others */ }
            }
        }

        public void Dispose() => _udp.Close();
    }

    /// <summary>Join side: collects adverts heard on the LAN.</summary>
    public sealed class LanListener : IDisposable
    {
        private const float ExpireSeconds = 3.5f;

        private readonly UdpClient _udp;
        private readonly Thread _thread;
        private readonly object _lock = new();
        private readonly Dictionary<string, GameAdvert> _games = new();
        private volatile bool _running = true;
        private volatile float _now;

        public LanListener()
        {
            AndroidLan.Acquire();   // Android drops broadcasts without a Wi-Fi multicast lock
            _udp = new UdpClient { ExclusiveAddressUse = false };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscovery.Port));
            _thread = new Thread(ReceiveLoop) { IsBackground = true, Name = "NoClue LAN listen" };
            _thread.Start();
        }

        private void ReceiveLoop()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _udp.Receive(ref from);
                    var ad = JsonUtility.FromJson<GameAdvert>(Encoding.UTF8.GetString(data));
                    if (ad == null || ad.magic != LanDiscovery.Magic || string.IsNullOrEmpty(ad.gameId)) continue;

                    ad.address = from.Address.ToString();
                    ad.lastSeen = _now;
                    lock (_lock) _games[ad.gameId] = ad;
                }
                catch (Exception) when (!_running)
                {
                    return;
                }
                catch (Exception)
                {
                    // malformed packet or transient socket error: keep listening
                }
            }
        }

        /// <summary>Games heard in the last few seconds. Call from the main thread.</summary>
        public List<GameAdvert> CurrentGames()
        {
            _now = Time.unscaledTime;
            lock (_lock)
            {
                foreach (var stale in _games.Where(g => _now - g.Value.lastSeen > ExpireSeconds).Select(g => g.Key).ToList())
                    _games.Remove(stale);
                return _games.Values.OrderBy(g => g.hostName).ToList();
            }
        }

        public void Dispose()
        {
            _running = false;
            _udp.Close();
            AndroidLan.Release();
        }
    }

    public static class LanDiscovery
    {
        public const int Port = 47777;
        public const string Magic = "NoClue/1";

        /// <summary>The limited broadcast address plus each IPv4 adapter's subnet broadcast.</summary>
        public static IEnumerable<IPAddress> BroadcastAddresses()
        {
            var result = new HashSet<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        byte[] ip = ua.Address.GetAddressBytes();
                        byte[] mask = ua.IPv4Mask.GetAddressBytes();
                        for (int i = 0; i < 4; i++) ip[i] = (byte)(ip[i] | ~mask[i]);
                        result.Add(new IPAddress(ip));
                    }
                }
            }
            catch (NetworkInformationException)
            {
                // fall back to the limited broadcast only
            }
            result.Add(IPAddress.Loopback); // lets a second copy on the same PC see the game
            return result;
        }
    }
}
