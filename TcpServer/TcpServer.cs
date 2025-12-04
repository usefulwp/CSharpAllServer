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
        public TcpServer(int port)
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
                    if (isRunning) {
                        Log($"接受连接异常:{e.Message}");
                    }
                }
            }
        }

        public async Task BroadcastAsync(string message, ClientHandler sender = null) {
            if (string.IsNullOrEmpty(message)){ 
                
            }
        }

        private void Log(string v)
        {
            throw new NotImplementedException();
        }

        private class ClientHandler
        {
            private TcpClient client;
            private TcpServer tcpServer;

            public ClientHandler(TcpClient client, TcpServer tcpServer)
            {
                this.client = client;
                this.tcpServer = tcpServer;
            }

            internal void HandleClientAsync()
            {
                throw new NotImplementedException();
            }
        }
    }
}
