
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ServerCRM.Utils
{
    public class ESLClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private readonly string _password;
        private TcpClient? _client;
        private NetworkStream? _stream;
        private readonly SemaphoreSlim _writeLock = new(1, 1);
        private readonly StringBuilder _recvBuffer = new();

       
        public event Action<string>? OnEventReceived;

        public bool IsConnected => _client?.Connected ?? false;

        public ESLClient(string host, int port, string password)
        {
            _host = host;
            _port = port;
            _password = password;
        }

        public async Task ConnectAsync()
        {
            if (IsConnected) return;
            _client = new TcpClient();
            await _client.ConnectAsync(_host, _port);
            _stream = _client.GetStream();

            var banner = await ReadFrameAsync();
            if (string.IsNullOrEmpty(banner) || !banner.Contains("Content-Type: auth/request"))
                throw new Exception("Unexpected ESL banner: " + banner?.Split('\n').FirstOrDefault());

            await SendRawAsync($"auth {_password}\n\n");

            var authReply = await ReadFrameAsync();
            if (authReply == null || !authReply.Contains("Reply-Text: +OK"))
                throw new Exception("ESL auth failed: " + authReply);
        }

        public async Task StartEventListenerAsync()
        {
            if (!IsConnected) await ConnectAsync();
            await SendRawAsync("events plain ALL\n\n");

            _ = Task.Run(async () =>
            {
                var buff = new byte[8192];
                while (IsConnected)
                {
                    try
                    {
                        var read = await _stream!.ReadAsync(buff, 0, buff.Length);
                        if (read <= 0) break;
                        var s = Encoding.UTF8.GetString(buff, 0, read);
                        lock (_recvBuffer)
                        {
                            _recvBuffer.Append(s);
                        }

                        while (true)
                        {
                            string frame;
                            lock (_recvBuffer)
                            {
                                var current = _recvBuffer.ToString();
                                var idx = current.IndexOf("\n\n", StringComparison.Ordinal);
                                if (idx < 0) break;
                                frame = current.Substring(0, idx);
                                _recvBuffer.Remove(0, idx + 2);
                            }
                           
                            try { OnEventReceived?.Invoke(frame); } catch (Exception ex) { Console.WriteLine($"Error in event handler: {ex.Message}"); }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Listener failed: {ex.Message}");
                        break;
                    }
                }
            });
        }

        public async Task<string> SendCommandAsync(string cmd)
        {
            if (!IsConnected) await ConnectAsync();

            if (!cmd.EndsWith("\n\n")) cmd = cmd + "\n\n";

            await SendRawAsync(cmd);

            var reply = await ReadFrameAsync();
            return reply ?? string.Empty;
        }

        private async Task SendRawAsync(string data)
        {
            if (_stream == null) throw new InvalidOperationException("Not connected (stream is null)");
            var bytes = Encoding.UTF8.GetBytes(data);

            await _writeLock.WaitAsync();
            try
            {
                await _stream.WriteAsync(bytes, 0, bytes.Length);
                await _stream.FlushAsync();
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private async Task<string?> ReadFrameAsync()
        {
            if (_stream == null) return null;
            var buffer = new byte[8192];

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                try
                {
                    if (_stream.DataAvailable)
                    {
                        int read = await _stream.ReadAsync(buffer, 0, buffer.Length);
                        if (read <= 0) break;
                        var s = Encoding.UTF8.GetString(buffer, 0, read);
                        lock (_recvBuffer) { _recvBuffer.Append(s); }
                    }

                    string? frame = null;
                    lock (_recvBuffer)
                    {
                        var current = _recvBuffer.ToString();
                        var idx = current.IndexOf("\n\n", StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            frame = current.Substring(0, idx);
                            _recvBuffer.Remove(0, idx + 2);
                        }
                    }

                    if (frame != null)
                    {
                        Console.WriteLine("--- FreeSWITCH Frame Received ---");
                        Console.WriteLine(frame);
                        Console.WriteLine("-----------------------------------");
                        return frame;
                    }

                    await Task.Delay(20);
                }
                catch (Exception)
                {
                    await Task.Delay(20);
                }
            }

            lock (_recvBuffer)
            {
                if (_recvBuffer.Length > 0)
                {
                    var s = _recvBuffer.ToString();
                    _recvBuffer.Clear();
                    return s;
                }
            }

            return null;
        }

        public void Dispose()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _writeLock.Dispose();
        }
    }
}
