using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading.Tasks;
using NoClue.Game;
using UnityEngine;

namespace NoClue.Net
{
    /// <summary>What the UI talks to: a link to the host, wherever it is.</summary>
    public interface ISession : IDisposable
    {
        bool IsHost { get; }
        /// <summary>False once the link to the host is gone.</summary>
        bool IsOpen { get; }
        void Send(string type, object payload);
        /// <summary>Main thread: drains network work and returns envelopes addressed to this player.</summary>
        void Poll(List<Envelope> received);
    }

    /// <summary>
    /// This device hosts: runs the TCP server, LAN advert and the <see cref="GameHost"/>.
    /// The host's own player is connection 0 and goes through the same message path, minus the socket.
    /// </summary>
    public sealed class HostSession : ISession, IHostTransport
    {
        private readonly TcpHostServer _server = new();
        private readonly LanBroadcaster _broadcaster;
        private readonly Queue<Envelope> _localInbox = new();

        public GameHost Host { get; }
        public bool IsHost => true;
        public bool IsOpen => true;

        public HostSession()
        {
            Host = new GameHost(this);
            _server.Start();
            _server.MessageReceived += Host.HandleMessage;
            _server.Disconnected += Host.HandleDisconnected;
            _broadcaster = new LanBroadcaster();
        }

        public void Send(string type, object payload) =>
            Host.HandleMessage(GameHost.LocalConnection, Wrap(type, payload));

        public void Poll(List<Envelope> received)
        {
            _server.Poll();
            _broadcaster.Tick(Host.BuildAdvert(_server.Port));
            while (_localInbox.Count > 0) received.Add(_localInbox.Dequeue());
        }

        // IHostTransport
        void IHostTransport.Send(int connectionId, string type, object payload)
        {
            if (connectionId == GameHost.LocalConnection) _localInbox.Enqueue(Wrap(type, payload));
            else _server.Send(connectionId, type, payload);
        }

        void IHostTransport.Kick(int connectionId) => _server.Kick(connectionId);

        public void Dispose()
        {
            _broadcaster.Dispose();
            _server.Dispose();
        }

        private static Envelope Wrap(string type, object payload) =>
            new() { type = type, payload = payload == null ? "{}" : JsonUtility.ToJson(payload) };
    }

    /// <summary>This device joined someone else's game over TCP.</summary>
    public sealed class RemoteSession : ISession
    {
        private FramedConnection _conn;

        public bool IsHost => false;
        public bool IsOpen => _conn != null && !_conn.IsClosed;
        public string Address { get; }

        private RemoteSession(string address, FramedConnection conn)
        {
            Address = address;
            _conn = conn;
        }

        /// <summary>Connects off the main thread; throws on failure or after <paramref name="timeoutMs"/>.</summary>
        public static async Task<RemoteSession> ConnectAsync(string address, int port, int timeoutMs = 4000)
        {
            var client = new TcpClient();
            var connect = client.ConnectAsync(address, port);
            // ConfigureAwait(false): never needs the Unity main thread, so it can't deadlock if someone blocks on it.
            if (await Task.WhenAny(connect, Task.Delay(timeoutMs)).ConfigureAwait(false) != connect)
            {
                client.Close();
                throw new TimeoutException($"No answer from {address}:{port}.");
            }
            await connect.ConfigureAwait(false); // surfaces SocketException
            return new RemoteSession($"{address}:{port}", new FramedConnection(client));
        }

        public void Send(string type, object payload) => _conn?.Send(type, payload);

        public void Poll(List<Envelope> received)
        {
            if (_conn == null) return;
            while (_conn.Incoming.TryDequeue(out var env)) received.Add(env);
        }

        public void Dispose()
        {
            _conn?.Close();
            _conn = null;
        }
    }
}
