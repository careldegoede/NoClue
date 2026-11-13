using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace NoClue.Net
{
    /// <summary>
    /// One TCP connection carrying length-prefixed JSON envelopes
    /// (4-byte big-endian length, then UTF-8 bytes).
    /// Reads happen on a background thread; received envelopes are queued for the main thread.
    /// </summary>
    public sealed class FramedConnection : IDisposable
    {
        private const int MaxFrameBytes = 1 << 20; // 1 MB, far above any real message

        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly object _sendLock = new();
        private readonly Thread _readThread;
        private volatile bool _closed;

        public readonly ConcurrentQueue<Envelope> Incoming = new();
        public bool IsClosed => _closed;
        public string RemoteAddress { get; }

        public FramedConnection(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            _stream = client.GetStream();
            RemoteAddress = client.Client.RemoteEndPoint?.ToString() ?? "?";
            _readThread = new Thread(ReadLoop) { IsBackground = true, Name = "NoClue TCP read " + RemoteAddress };
            _readThread.Start();
        }

        public void Send(string type, object payload)
        {
            if (_closed) return;
            var env = new Envelope { type = type, payload = payload == null ? "{}" : JsonUtility.ToJson(payload) };
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(env));
            byte[] header =
            {
                (byte)(body.Length >> 24), (byte)(body.Length >> 16), (byte)(body.Length >> 8), (byte)body.Length
            };
            try
            {
                lock (_sendLock)
                {
                    _stream.Write(header, 0, 4);
                    _stream.Write(body, 0, body.Length);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException)
            {
                Close();
            }
        }

        private void ReadLoop()
        {
            var header = new byte[4];
            try
            {
                while (!_closed)
                {
                    if (!ReadExactly(header, 4)) break;
                    int len = (header[0] << 24) | (header[1] << 16) | (header[2] << 8) | header[3];
                    if (len <= 0 || len > MaxFrameBytes) break;

                    var body = new byte[len];
                    if (!ReadExactly(body, len)) break;

                    var env = JsonUtility.FromJson<Envelope>(Encoding.UTF8.GetString(body));
                    if (env != null && !string.IsNullOrEmpty(env.type)) Incoming.Enqueue(env);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or ObjectDisposedException or ArgumentException)
            {
                // connection dropped or garbage received: fall through to close
            }
            Close();
        }

        private bool ReadExactly(byte[] buffer, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = _stream.Read(buffer, read, count - read);
                if (n == 0) return false; // remote closed
                read += n;
            }
            return true;
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _stream.Close(); } catch { /* already gone */ }
            try { _client.Close(); } catch { /* already gone */ }
        }

        public void Dispose() => Close();

        public static T Read<T>(Envelope env) => JsonUtility.FromJson<T>(env.payload);
    }
}
