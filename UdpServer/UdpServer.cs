using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class UdpServer :IDisposable
    {
        private UdpClient _udpServer;
        CancellationTokenSource _cancellationTokenSource;
        bool _isRunning = false;
        readonly int _port;
        readonly string _host;
        readonly ConcurrentDictionary<string, ClientInfo> _clients;

        public class ClientInfo {
            public IPEndPoint EndPoint { get; set; }
            public DateTime LastActiveTime { get; set; }
            public string ClientId { get; set; }
            public ClientInfo(IPEndPoint endPoint) {
                EndPoint = endPoint;
                LastActiveTime = DateTime.Now;
                ClientId = $"{endPoint.Address}:{endPoint.Port}";
            }

            public void UpdateActiveTime() {
                LastActiveTime = DateTime.Now;
            }
        }

        public event EventHandler<string> OnLog;
        public event EventHandler<ClientInfo> OnClientConnected;
        public event EventHandler<ClientInfo> OnClientDisconnected;
        public event EventHandler<ReceivedData> OnDataReceived;
        public class ReceivedData
        {
            public byte[] RawData { get; set; }
            public string TextData { get; set; }
            public ClientInfo Client { get; set; }
            public DateTime ReceiveTime { get; set; }
            public ReceivedData(byte[] data,ClientInfo client)
            {
                RawData = data;
                Client= client;
                ReceiveTime= DateTime.Now;
                try
                {
                    TextData = Encoding.UTF8.GetString(data);
                }
                catch {
                    TextData = string.Empty;
                }
            }
            public T DeserializeJson<T>()
            {
                try
                {
                    return JsonConvert.DeserializeObject<T>(TextData);
                }
                catch {
                    return default(T);
                }
            }
        }

        public UdpServer(int port=8889,string host="0.0.0.0") {
            _port = port;
            _host = host;
            _clients = new ConcurrentDictionary<string, ClientInfo>();
        }

        public async Task StartAsync() {
            if (_isRunning) {
                Log("服务器已经在运行中");
                return;
            }
            try
            {
                _cancellationTokenSource = new CancellationTokenSource();
                _udpServer = new UdpClient(new IPEndPoint(IPAddress.Parse(_host), _port));
                _isRunning = true;
                Log($"udp服务器启动在{_host}:{_port}");
                _ = Task.Run(() => ReceiveLoopAsync(_cancellationTokenSource.Token));
                _ = Task.Run(() => CleanupClientsLoopAsync(_cancellationTokenSource.Token));
                _ = Task.Run(() => HeartbeatLoopAsync(_cancellationTokenSource.Token));
                await Task.Delay(-1, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                Log("服务器正常停止");
            }
            catch (Exception ex) {
                Log($"服务器启动失败: {ex.Message}");
                throw;
            }
        }


        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var result = await _udpServer.ReceiveAsync().WithCancellation(cancellationToken);
                    var clientEndPoint = result.RemoteEndPoint;
                    var clientKey = $"{clientEndPoint.Address}:{clientEndPoint.Port}";
                    var clientInfo = _clients.AddOrUpdate(clientKey,
                        key =>
                        {
                            var newClient = new ClientInfo(clientEndPoint);
                            OnClientConnected?.Invoke(this, newClient);
                            Log($"新客户端连接：{key}");
                            return newClient;
                        }, (key, existingClient) =>
                        {
                            existingClient.UpdateActiveTime();
                            return existingClient;
                        });
                    var receivedData = new ReceivedData(result.Buffer, clientInfo);
                    OnDataReceived?.Invoke(this, receivedData);
                    await ProcessReceivedDataAsync(receivedData);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException ex)
                {
                    Log($"Socket错误: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log($"接受数据错误: {ex.Message}");
                }
            }
        }

        private async Task ProcessReceivedDataAsync(ReceivedData data)
        {
            try {
                if (data.TextData.StartsWith("{") && data.TextData.EndsWith("}")) {
                    await ProcessJsonMessageAsync(data);
                }
                else {
                    await ProcessTextMessageAsync(data);
                }
            } catch (Exception ex) {
                Log($"处理数据错误: {ex.Message}");
            }
            
        }

        private async Task ProcessTextMessageAsync(ReceivedData data)
        {
            var text = data.TextData.Trim().ToLower();

            switch (text)
            {
                case "hello":
                case "hi":
                    await SendTextAsync($"你好，{data.Client.EndPoint.Address}！", data.Client.EndPoint);
                    break;

                case "time":
                    var currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    await SendTextAsync($"服务器时间: {currentTime}", data.Client.EndPoint);
                    break;

                case "ping":
                    await SendTextAsync("pong", data.Client.EndPoint);
                    break;

                case "clients":
                    var clientCount = _clients.Count;
                    await SendTextAsync($"当前客户端数量: {clientCount}", data.Client.EndPoint);
                    break;

                case var s when s.StartsWith("echo "):
                    var echoText = data.TextData.Substring(5);
                    await SendTextAsync(echoText, data.Client.EndPoint);
                    break;

                default:
                    // 回显收到的消息
                    await SendTextAsync($"收到: {data.TextData}", data.Client.EndPoint);
                    break;
            }
        }

        private async Task ProcessJsonMessageAsync(ReceivedData data)
        {
            try
            {
                dynamic jsonData = JsonConvert.DeserializeObject(data.TextData);
                string type = jsonData?.type?.ToString() ?? "unknown";

                object response = null;

                switch (type.ToLower())
                {
                    case "login":
                        string username = jsonData?.username?.ToString() ?? "anonymous";
                        response = new
                        {
                            type = "login_response",
                            status = "success",
                            message = $"欢迎 {username}",
                            server_time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            client_id = data.Client.ClientId
                        };
                        break;

                    case "chat":
                        string message = jsonData?.message?.ToString() ?? "";
                        string sender = jsonData?.sender?.ToString() ?? "unknown";
                        response = new
                        {
                            type = "chat_response",
                            status = "received",
                            sender = sender,
                            message = message,
                            timestamp = DateTime.Now.ToString("HH:mm:ss")
                        };
                        break;

                    case "heartbeat":
                        response = new
                        {
                            type = "heartbeat_response",
                            status = "alive",
                            timestamp = DateTime.Now.ToString("HH:mm:ss")
                        };
                        break;

                    default:
                        response = new
                        {
                            type = "ack",
                            status = "received",
                            original = jsonData
                        };
                        break;
                }

                if (response != null)
                {
                    await SendJsonAsync(response, data.Client.EndPoint);
                }
            }
            catch (Exception ex)
            {
                await SendTextAsync($"JSON解析错误: {ex.Message}", data.Client.EndPoint);
            }
        }

        private async Task SendJsonAsync(object response, IPEndPoint endPoint)
        {
            try
            {
                var json = JsonConvert.SerializeObject(response);
                await SendTextAsync(json, endPoint);
            }
            catch (Exception ex) {
                Log($"发送json失败: {ex.Message}");
            }
        }

        private async Task SendTextAsync(string text, IPEndPoint clientEndpoint)
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(text);
                await _udpServer.SendAsync(bytes,bytes.Length, clientEndpoint);
                Log($"发送消息到{clientEndpoint}:{text}");
            }
            catch (Exception ex) {
                Log($"发送消息失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 日志记录
        /// </summary>
        private void Log(string message)
        {
            var logMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
            Console.WriteLine(logMessage);
            OnLog?.Invoke(this, logMessage);
        }

        public async Task BroadcastAsync(string message, IPEndPoint excludeEndpoint = null) {
            foreach (var client in _clients.Values) {
                if (excludeEndpoint == null || !client.EndPoint.Equals(excludeEndpoint)) { 
                    await SendTextAsync(message,client.EndPoint);
                }
            }
            Log($"广播消息给{_clients.Count}个客户端");
        }


        /// <summary>
        /// 广播JSON消息
        /// </summary>
        public async Task BroadcastJsonAsync(object data, IPEndPoint excludeEndpoint = null)
        {
            foreach (var client in _clients.Values)
            {
                if (excludeEndpoint == null || !client.EndPoint.Equals(excludeEndpoint))
                {
                    await SendJsonAsync(data, client.EndPoint);
                }
            }
        }

        /// <summary>
        /// 清理不活跃的客户端
        /// </summary>
        private async Task CleanupClientsLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                await Task.Delay(10000, cancellationToken); // 每10秒检查一次

                var timeoutTime = DateTime.Now.AddSeconds(-30); // 30秒超时
                var clientsToRemove = new List<string>();

                foreach (var kvp in _clients)
                {
                    if (kvp.Value.LastActiveTime < timeoutTime)
                    {
                        clientsToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in clientsToRemove)
                {
                    if (_clients.TryRemove(key, out var clientInfo))
                    {
                        OnClientDisconnected?.Invoke(this, clientInfo);
                        Log($"客户端超时断开: {key}");
                    }
                }
            }
        }

        /// <summary>
        /// 心跳循环（可选）
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _isRunning)
            {
                await Task.Delay(30000, cancellationToken); // 每30秒发送一次心跳

                var heartbeatData = new
                {
                    type = "server_heartbeat",
                    timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    client_count = _clients.Count
                };

                await BroadcastJsonAsync(heartbeatData);
            }
        }

        /// <summary>
        /// 获取当前客户端列表
        /// </summary>
        public List<ClientInfo> GetClients()
        {
            return new List<ClientInfo>(_clients.Values);
        }
        /// <summary>
        /// 获取服务器统计信息
        /// </summary>
        public Dictionary<string, object> GetStats()
        {
            return new Dictionary<string, object>
            {
                ["status"] = _isRunning ? "running" : "stopped",
                ["port"] = _port,
                ["host"] = _host,
                ["client_count"] = _clients.Count,
            };
        }

        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();
            _udpServer?.Close();
            Log("服务器已停止");
        }

        void IDisposable.Dispose()
        {
            Stop();
            _cancellationTokenSource?.Dispose();
            _udpServer?.Dispose();
        }
    }
}

/// <summary>
/// Task扩展方法，支持取消
/// </summary>
public static class TaskExtensions
{
    public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<bool>();

        using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            if (task != await Task.WhenAny(task, tcs.Task))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await task;
    }
}
