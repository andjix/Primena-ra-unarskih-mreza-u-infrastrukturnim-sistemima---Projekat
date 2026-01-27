using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FileClient
{
    internal class Client
    {
        const int TCP_PORT = 19010;
        const int UDP_PORT = 19011;

        static void Main(string[] args)
        {
            // TCP povezivanje
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Loopback, TCP_PORT);
            byte[] buffer = new byte[8192];

            Console.Write("Unesi ClientId (npr. Filip): ");
            string clientId = Console.ReadLine();

            Console.WriteLine("Pritisni Enter za povezivanje...");
            Console.ReadLine();

            clientSocket.Connect(serverEP);
            Console.WriteLine("Povezan na server (TCP)!");

            while (true)
            {
                Console.WriteLine("\n--- MENI ---");
                Console.WriteLine("1) LIST");
                Console.WriteLine("2) UPLOAD");
                Console.WriteLine("3) DOWNLOAD");
                Console.WriteLine("4) LOCK");
                Console.WriteLine("5) EDIT");
                Console.WriteLine("6) UNLOCK");
                Console.WriteLine("7) DELETE");
                Console.WriteLine("8) STATS (UDP)");
                Console.WriteLine("9) KRAJ");
                Console.Write("Izbor: ");
                string izbor = Console.ReadLine();

                if (izbor == "9") break;

                string request = "";

                if (izbor == "1")
                {
                    request = "LIST";
                    TcpSendReceive(clientSocket, request, buffer);
                }
                else if (izbor == "2")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();
                    Console.Write("Autor: ");
                    string author = Console.ReadLine();
                    Console.Write("Sadrzaj (jedna linija): ");
                    string content = Console.ReadLine();

                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content ?? ""));
                    request = $"UPLOAD|{name}|{author}|{b64}";
                    TcpSendReceive(clientSocket, request, buffer);
                }
                else if (izbor == "3")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    request = $"DOWNLOAD|{name}";
                    string resp = TcpSendReceive(clientSocket, request, buffer);

                    
                    // OK|DOWNLOAD|author|base64
                    var parts = resp.Split('|');
                    if (parts.Length >= 4 && parts[0] == "OK" && parts[1] == "DOWNLOAD")
                    {
                        string author = parts[2];
                        string content = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
                        Console.WriteLine($"Autor: {author}");
                        Console.WriteLine($"Sadrzaj: {content}");
                    }
                }
                else if (izbor == "4")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    request = $"LOCK|{name}|{clientId}";
                    TcpSendReceive(clientSocket, request, buffer);
                }
                else if (izbor == "5")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();
                    Console.Write("Novi sadrzaj (jedna linija): ");
                    string content = Console.ReadLine();

                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content ?? ""));
                    request = $"EDIT|{name}|{clientId}|{b64}";
                    TcpSendReceive(clientSocket, request, buffer);
                }
                else if (izbor == "6")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    request = $"UNLOCK|{name}|{clientId}";
                    TcpSendReceive(clientSocket, request, buffer);
                }
                else if (izbor == "7")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    request = $"DELETE|{name}|{clientId}";
                    TcpSendReceive(clientSocket, request, buffer);
                }
                else if (izbor == "8")
                {
                    UdpStats(serverEP.Address.ToString(), UDP_PORT);
                }
                else
                {
                    Console.WriteLine("Nepoznat izbor.");
                }
            }

            Console.WriteLine("Klijent zavrsava sa radom.");
            clientSocket.Close();
        }

        static string TcpSendReceive(Socket socket, string request, byte[] buffer)
        {
            try
            {
                byte[] reqBytes = Encoding.UTF8.GetBytes(request);
                int sent = socket.Send(reqBytes);
                if (sent > 0) Console.WriteLine("Zahtev poslat: " + request);

                int brBajta = socket.Receive(buffer);
                string resp = Encoding.UTF8.GetString(buffer, 0, brBajta);
                Console.WriteLine("Odgovor: " + resp);
                return resp;
            }
            catch (SocketException ex)
            {
                Console.WriteLine("Greska TCP: " + ex.Message);
                return "ERROR|TCP";
            }
        }

        static void UdpStats(string host, int port)
        {
            try
            {
                using (UdpClient udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2000;

                    byte[] req = Encoding.UTF8.GetBytes("STATS");
                    udp.Send(req, req.Length, host, port);

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = udp.Receive(ref remote);

                    string s = Encoding.UTF8.GetString(resp);
                    Console.WriteLine("UDP STATS odgovor: " + s);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("UDP greska: " + ex.Message);
            }
        }
    }
}
