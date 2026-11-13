using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace NoClue.Net
{
    /// <summary>
    /// Accepts player connections. Call <see cref="Poll"/> from the main thread to receive
    /// connect / message / disconnect callbacks there.
    /// </summary>
    public sealed class TcpHostServer : IDisposable
    {
        public const int DefaultPort = 47778;

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private int _nextConnectionId = 1; // 0 is reserved for the host's own local player

        private readonly ConcurrentQueue<FramedConnection> _accepted = new();
        private readonly Dictionary<int, FramedConnection> _connections = new();

        public int Port { get; private set; }

        public event Action<int> Connected;
        public event Action<int, Envelope> MessageReceived;
        public event Action<int> Disconnected;

        /// <summary>Starts listening on the first free port from <paramref name="preferredPort"/> upward.</summary>
        public void Start(int preferredPort = DefaultPort)
        {
            for (int port = preferredPort; port < preferredPort + 10; port++)
            {
                try
                {
                    _listener = new TcpListener(IPAddress.Any, port);
                    _listener.Start();
                    Port = port;
                    break;
                }
                catch (SocketException)
                {
                    _listener = null;
                }
            }
            if (_listener == null) throw new InvalidOperationException("No free TCP port to host on.");

            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "NoClue TCP accept" };
            _acceptThread.Start();
            Debug.Log($"[NoClue] Hosting on TCP port {Port}");
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    _accepted.Enqueue(new FramedConnection(client));
                }
                catch (Exception) when (!_running)
                {
                    return; // listener stopped
                }
                catch (SocketException e)
                {
                    Debug.LogWarning($"[NoClue] Accept failed: {e.Message}");
                }
            }
        }

        public void Poll()
        {
            while (_accepted.TryDequeue(out var conn))
            {
                int id = _nextConnectionId++;
                _connections[id] = conn;
                Connected?.Invoke(id);
            }

            List<int> dropped = null;
            foreach (var pair in _connections)
            {
                while (pair.Value.Incoming.TryDequeue(out var env))
                    MessageReceived?.Invoke(pair.Key, env);

                if (pair.Value.IsClosed && pair.Value.Incoming.IsEmpty)
                    (dropped ??= new List<int>()).Add(pair.Key);
            }

            if (dropped == null) return;
            foreach (int id in dropped)
            {
                _connections.Remove(id);
                Disconnected?.Invoke(id);
            }
        }

        public void Send(int connectionId, string type, object payload)
        {
            if (_connections.TryGetValue(connectionId, out var conn)) conn.Send(type, payload);
        }

        /// <summary>Closes a connection; the Disconnected callback fires on the next Poll.</summary>
        public void Kick(int connectionId)
        {
            if (_connections.TryGetValue(connectionId, out var conn)) conn.Close();
        }

        public void Dispose()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            foreach (var c in _connections.Values) c.Close();
            _connections.Clear();
            while (_accepted.TryDequeue(out var c)) c.Close();
        }
    }
}
