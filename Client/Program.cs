using System; 
using System.Text;
using System.Net; 
using System.Net.Sockets; 

namespace Client
{
    class Program
    {
        static void Main(string[] args)
        {
            Connect();
            
        }

        static void Connect()
        {
            // 1. 서버에 접속
            IPEndPoint ipep = new IPEndPoint(IPAddress.Parse("10.201.11.76"), 5000);
            Socket client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            client.Connect(ipep);
            
            Console.WriteLine("Socket connect");
            
            Byte[] buffer = new Byte[1024];
            string? input = Console.ReadLine()?.Trim();; // 보낼 메시지 작성
            buffer = Encoding.UTF8.GetBytes(input??""); // 메시지를 바이트 배열로 변환
            client.Send(buffer); // 서버로 데이터 전송

            client.Shutdown(SocketShutdown.Both);
            client.Close();
        }
    }
}