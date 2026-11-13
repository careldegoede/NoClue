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

    /// <summary>
    /// Host side, two ways to be found:
    /// 1) broadcasts a <see cref="GameAdvert"/> about once a second (fast, but Wi-Fi repeaters often drop it);
    /// 2) answers direct "anyone hosting?" probes on <see cref="LanDiscovery.ProbePort"/> with a unicast advert,
    ///    which gets through repeaters that drop broadcasts.
    /// </summary>
    public sealed class LanBroadcaster : IDisposable
    {
        private readonly UdpClient _udp = new() { EnableBroadcast = true };
        private readonly List<IPEndPoint> _targets;
        private readonly UdpClient _probe;
        private readonly Thread _probeThread;
        private volatile byte[] _lastAdvert;
        private volatile bool _running = true;
        private float _nextSend;

        public LanBroadcaster()
        {
            _targets = LanDiscovery.BroadcastAddresses()
                .Select(a => new IPEndPoint(a, LanDiscovery.Port)).ToList();
            Debug.Log("[NoClue] LAN advert targets: " + string.Join(", ", _targets));

            try
            {
                _probe = new UdpClient(new IPEndPoint(IPAddress.Any, LanDiscovery.ProbePort));
                _probeThread = new Thread(AnswerProbes) { IsBackground = true, Name = "NoClue probe answer" };
                _probeThread.Start();
            }
            catch (SocketException e)
            {
                Debug.LogWarning($"[NoClue] Can't answer discovery probes (port {LanDiscovery.ProbePort} busy?): {e.Message}");
                _probe = null;
            }
        }

        public void Tick(GameAdvert advert)
        {
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + 1f;

            byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(advert));
            _lastAdvert = data;
            foreach (var target in _targets)
            {
                try { _udp.Send(data, data.Length, target); }
                catch (SocketException) { /* adapter down; try the others */ }
            }
        }

        private void AnswerProbes()
        {
            var from = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                    byte[] data = _probe.Receive(ref from);
                    if (Encoding.UTF8.GetString(data) != LanDiscovery.ProbeMagic) continue;
                    var advert = _lastAdvert;
                    if (advert != null) _probe.Send(advert, advert.Length, from);   // straight back to the asker
                }
                catch (Exception) when (!_running) { return; }
                catch (Exception) { /* bad packet / transient error: keep answering */ }
            }
        }

        public void Dispose()
        {
            _running = false;
            _udp.Close();
            _probe?.Close();
        }
    }

    /// <summary>Join side: collects adverts heard on the LAN, and probes the local network for hosts.</summary>
    public sealed class LanListener : IDisposable
    {
        private const float ExpireSeconds = 4.5f;
        private const float ProbeEverySeconds = 2f;

        private readonly UdpClient _udp;
        private readonly Thread _thread;
        private readonly object _lock = new();
        private readonly Dictionary<string, GameAdvert> _games = new();
        private readonly List<IPEndPoint> _probeTargets;
        private volatile bool _running = true;
        private volatile float _now;
        private float _nextProbe;
        private int _probing;

        public LanListener()
        {
            AndroidLan.Acquire();   // Android drops broadcasts without a Wi-Fi multicast lock
            _udp = new UdpClient { ExclusiveAddressUse = false };
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, LanDiscovery.Port));
            _probeTargets = LanDiscovery.ProbeTargets().Select(a => new IPEndPoint(a, LanDiscovery.ProbePort)).ToList();
            Debug.Log($"[NoClue] LAN probe: {_probeTargets.Count} addresses");
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

        /// <summary>Games heard in the last few seconds. Call from the main thread (also drives probing).</summary>
        public List<GameAdvert> CurrentGames()
        {
            _now = Time.unscaledTime;
            if (_now >= _nextProbe)
            {
                _nextProbe = _now + ProbeEverySeconds;
                SendProbes();
            }

            lock (_lock)
            {
                foreach (var stale in _games.Where(g => _now - g.Value.lastSeen > ExpireSeconds).Select(g => g.Key).ToList())
                    _games.Remove(stale);
                return _games.Values.OrderBy(g => g.hostName).ToList();
            }
        }

        /// <summary>One tiny "anyone hosting?" packet to every address nearby, off the main thread.</summary>
        private void SendProbes()
        {
            if (_probeTargets.Count == 0 || Interlocked.Exchange(ref _probing, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] probe = Encoding.UTF8.GetBytes(LanDiscovery.ProbeMagic);
                    foreach (var target in _probeTargets)
                    {
                        if (!_running) return;
                        try { _udp.Send(probe, probe.Length, target); }
                        catch (SocketException) { /* unreachable address: skip */ }
                    }
                }
                catch (ObjectDisposedException) { /* closed while sending */ }
                finally { Interlocked.Exchange(ref _probing, 0); }
            });
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
        public const int Port = 47777;        // adverts arrive here (join side listens)
        public const int ProbePort = 47779;   // hosts answer direct probes here
        public const string Magic = "NoClue/1";
        public const string ProbeMagic = "NoClue?/1";

        /// <summary>This device's IPv4 networks: (address, mask) per active adapter.</summary>
        public static List<(IPAddress ip, IPAddress mask)> LocalSubnets()
        {
            var result = new List<(IPAddress, IPAddress)>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        if (ua.IPv4Mask.Equals(IPAddress.Any)) continue;
                        result.Add((ua.Address, ua.IPv4Mask));
                    }
                }
            }
            catch (Exception e)
            {
                // Android/IL2CPP can refuse to enumerate adapters
                Debug.LogWarning("[NoClue] Network adapters unavailable: " + e.Message);
            }

            // Android fallback: ask the Wi-Fi service directly
            if (result.Count == 0 && AndroidLan.TryGetWifi(out var wifiIp, out var wifiMask))
                result.Add((wifiIp, wifiMask));
            return result;
        }

        /// <summary>The limited broadcast address plus each IPv4 adapter's subnet broadcast.</summary>
        public static IEnumerable<IPAddress> BroadcastAddresses()
        {
            var result = new HashSet<IPAddress> { IPAddress.Broadcast };
            foreach (var (ip, mask) in LocalSubnets())
            {
                byte[] b = ip.GetAddressBytes();
                byte[] m = mask.GetAddressBytes();
                for (int i = 0; i < 4; i++) b[i] = (byte)(b[i] | ~m[i]);
                result.Add(new IPAddress(b));
            }
            result.Add(IPAddress.Loopback); // lets a second copy on the same PC see the game
            return result;
        }

        /// <summary>
        /// Every other host address on our local networks (only networks of up to 1022 hosts, e.g. the usual
        /// 192.168.x.0/24), plus loopback. Used for direct probes that get through broadcast-dropping repeaters.
        /// </summary>
        public static IEnumerable<IPAddress> ProbeTargets()
        {
            var result = new HashSet<IPAddress> { IPAddress.Loopback };
            foreach (var (ip, mask) in LocalSubnets())
            {
                uint addr = ToUInt(ip), m = ToUInt(mask);
                uint hostBits = ~m;
                if (hostBits == 0 || hostBits > 1023) continue;   // skip huge networks
                uint network = addr & m;
                for (uint h = 1; h < hostBits; h++)
                {
                    uint a = network | h;
                    if (a != addr) result.Add(FromUInt(a));
                }
            }
            return result;
        }

        private static uint ToUInt(IPAddress a)
        {
            byte[] b = a.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        private static IPAddress FromUInt(uint v) =>
            new(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
    }
}
