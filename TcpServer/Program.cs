using Server;

class Program {
    public static void Main(String[] args) {
        Console.Title = "TCP服务器控制台";
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== TCP服务器启动 ===");
        Console.ResetColor();

        // 获取端口参数（默认为8889）
        int port = 8889;
        if (args.Length > 0 && int.TryParse(args[0], out int parsedPort))
        {
            port = parsedPort;
        }

        // 创建并启动服务器
        TcpServer server = new TcpServer(port);
        server.Start();

        Console.WriteLine($"服务器已启动在端口: {port}");

        // 等待退出信号
        var exitEvent = new ManualResetEvent(false);
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            exitEvent.Set();
        };

        exitEvent.WaitOne();

        // 停止服务器
        server.Stop();
        Console.WriteLine("服务器已停止，按任意键退出...");
        Console.ReadKey();
    }
}


public static class ConsoleExtensions
{
    public static void WriteColor(string text, ConsoleColor color)
    {
        var originalColor = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = originalColor;
    }
}