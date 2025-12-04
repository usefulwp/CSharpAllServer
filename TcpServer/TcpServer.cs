using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Server
{
    public class TcpServer
    {
        private TcpListener listener;
        private List<ClientHandler> clients = new List<ClientHandler>();
        private bool isRunning = false;
        private readonly object lockObj = new object();

        public int Port { get; }
        public event Action<string> OnLogMessage;
        public TcpServer(int port=8889)
        {
            Port = port;
            OnLogMessage += (msg) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]{msg}");
        }

        public void Start()
        {
            if (isRunning)
            {
                Log("服务器已经在运行");
                return;
            }
            try
            {
                listener = new TcpListener(IPAddress.Any, Port);
                listener.Start();
                isRunning = true;
                Log($"tcp服务器启动，监听端口：{Port}");
                Task.Run(() => AcceptClients());
            }
            catch (Exception e)
            {
                Log($"服务器启动失败：{e.Message}");
            }
        }

        private async Task AcceptClients()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    var handler = new ClientHandler(client, this);
                    lock (lockObj)
                    {
                        clients.Add(handler);
                    }
                    Log($"客户端连接：{client.Client.RemoteEndPoint},当前客户端数：{clients.Count}");
                    _ = Task.Run(() => handler.HandleClientAsync());
                }
                catch (Exception e)
                {
                    if (isRunning)
                    {
                        Log($"接受连接异常:{e.Message}");
                    }
                }
            }
        }

        public async Task BroadcastAsync(string message, ClientHandler sender = null)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }
            List<ClientHandler> clientsCopy;
            lock (lockObj)
            {
                clientsCopy = new List<ClientHandler>(clients);
            }
            var tasks = new List<Task>();
            foreach (var client in clientsCopy)
            {
                if (client != sender && client.IsConnected)
                {
                    tasks.Add(client.SendAsync(message));
                }
            }
            await Task.WhenAll(tasks);
            Log($"广播消息:{message}");
        }

        /// <summary>
        /// 移除客户端
        /// </summary>
        public void RemoveClient(ClientHandler client)
        {
            lock (lockObj)
            {
                clients.Remove(client);
            }

            Log($"客户端断开连接，剩余: {clients.Count}");
        }

        public async Task SendToClientAsync(string clientId, string message)
        {
            ClientHandler targetClient = null;
            lock (lockObj)
            {
                foreach (var client in clients)
                {
                    if (client.ClientId == clientId)
                    {
                        targetClient = client;
                        break;
                    }
                }
            }
            if (targetClient != null && targetClient.IsConnected)
            {
                await targetClient.SendAsync(message);
            }
        }

        public List<ClientInfo> GetClientsInfo()
        {
            var infoList = new List<ClientInfo>();
            lock (lockObj)
            {
                foreach (var client in clients)
                {
                    infoList.Add(new ClientInfo
                    {
                        ClientId = client.ClientId,
                        EndPoint = client.EndPoint,
                        ConnectTime = client.ConnectTime,
                        IsConnected = client.IsConnected
                    });
                }
            }
            return infoList;
        }

        public void Stop()
        {
            if (!isRunning) return;
            isRunning = false;
            try
            {
                listener?.Stop();
                lock (lockObj)
                {
                    foreach (var client in clients)
                    {
                        client.Disconnect();
                    }
                    clients.Clear();
                }
                Log("服务器已停止");
            }
            catch (Exception e)
            {
                Log($"停止服务器异常: {e.Message}");
            }
        }

        private void Log(string message)
        {
            OnLogMessage?.Invoke(message);
        }

        public class ClientHandler
        {
            private readonly TcpClient client;
            private readonly NetworkStream stream;
            private readonly TcpServer server;
            private bool isConnected = false;

            public string ClientId { get; }
            public string EndPoint { get; }
            public DateTime ConnectTime { get; }
            public bool IsConnected => isConnected && client?.Connected == true;

            public ClientHandler(TcpClient client, TcpServer server)
            {
                this.client = client;
                this.server = server;
                this.stream = client.GetStream();

                EndPoint = client.Client.RemoteEndPoint?.ToString() ?? "未知";
                ClientId = Guid.NewGuid().ToString();
                ConnectTime = DateTime.Now;
                isConnected = true;
            }

            public async Task HandleClientAsync()
            {
                byte[] lengthBuffer = new byte[4];
                try
                {
                    while (isConnected && client.Connected)
                    {
                        if (!await ReadFullyAsync(lengthBuffer, 4))
                        {
                            break;
                        }
                        int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                        if (messageLength <= 0 || messageLength > 10 * 1024 * 1024)
                        {
                            server.Log($"无效的消息长度：{messageLength},来自: {EndPoint}");
                            break;
                        }
                        byte[] messageBuffer = new byte[messageLength];
                        if (!await ReadFullyAsync(messageBuffer, messageLength))
                        {
                            break;
                        }
                        string message = Encoding.UTF8.GetString(messageBuffer);
                        await ProcessMessageAsync(message);
                    }
                }
                catch (Exception e)
                {
                    server.Log($"处理客户端 {EndPoint} 异常：{e.Message}");
                }
                finally
                {
                    Disconnect();
                }
            }

            public void Disconnect()
            {
                if (!isConnected) return;
                isConnected = false;
                try
                {
                    stream?.Close();
                    client?.Close();
                }
                catch (Exception e) {
                    server.Log($"客户端{EndPoint}关闭失败");
                }
                server.RemoveClient(this);
            }

            public async Task ProcessMessageAsync(string message)
            {
                server.Log($"收到来自 {EndPoint} 的消息：{message}");
                if (message == "Heartbeat")
                {
                    await SendAsync("HeartbeatResponse");
                    return;
                }
                string response = await ProcessBusinessLogic(message);
                if (!string.IsNullOrEmpty(response))
                {
                    await SendAsync(response);
                }
                //await server.BroadcastAsync($"[{EndPoint}]: {message}", this);
            }

            public async Task<string> ProcessBusinessLogic(string message)
            {
                if (message.StartsWith("echo:"))
                {
                    return $"服务器回应： {message.Substring(5)}";
                }
                else if (message.StartsWith("time"))
                {
                    return $"服务器时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                }
                else if (message.StartsWith("getClientCnt"))
                {
                    var clients = server.GetClientsInfo();
                    return $"当前客户端数: {clients.Count}";
                }
                return $"已收到：{message}";
            }

            public async Task SendAsync(string message)
            {
                if (!isConnected || stream == null)
                {
                    return;
                }
                try
                {
                    byte[] data = Encoding.UTF8.GetBytes(message);
                    byte[] length = BitConverter.GetBytes(data.Length);
                    await stream.WriteAsync(length, 0, 4);
                    await stream.WriteAsync(data, 0, data.Length);
                    await stream.FlushAsync();
                    server.Log($"发送给 {EndPoint}: {message}");
                }
                catch (Exception e)
                {
                    server.Log($"发送给 {EndPoint} 失败： {e.Message}");
                    Disconnect();
                }
            }

            public async Task<bool> ReadFullyAsync(byte[] buffer, int count)
            {
                int totalRead = 0;
                while (totalRead < count)
                {
                    if (!client.Connected || stream == null)
                    {
                        return false;
                    }
                    int bytesRead = await stream.ReadAsync(buffer, totalRead, count - totalRead);
                    if (bytesRead == 0)
                        return false;
                    totalRead += bytesRead;
                }
                return true;
            }

        }
    }

    public class ClientInfo { 
        public string ClientId { get; set; }
        public string EndPoint { get; set; }
        public DateTime ConnectTime { get; set; }

        public bool IsConnected { get; set; }

        public override string ToString()
        {
            return $"{EndPoint} (ID: {ClientId}) - 连接时间：{ConnectTime:HH:mm:ss} - {(IsConnected ? "在线" : "离线")}";
        }
    }
}
