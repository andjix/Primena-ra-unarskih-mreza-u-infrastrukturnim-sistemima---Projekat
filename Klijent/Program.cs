using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FileClient
{
    internal class Client
    {
        const int TCP_PORT = 19010; // ide na RequestManager
        const int UDP_PORT = 19011; // ide na RequestManager

        static void Main(string[] args)
        {
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Loopback, TCP_PORT);
            byte[] buffer = new byte[8192];

            Console.Write("Unesi ClientId (npr. Filip): ");
            string clientId = Console.ReadLine();

            Console.WriteLine("Pritisni Enter za povezivanje...");
            Console.ReadLine();

            clientSocket.Connect(serverEP);
            Console.WriteLine("Povezan na upravljac zahteva (TCP)!");

            // HELLO da RequestManager zna koji je clientId vezan za ovaj socket
            TcpSendReceive(clientSocket, $"HELLO|{clientId}", buffer);

            while (true)
            {
                Console.WriteLine("\n--- MENI ---");
                Console.WriteLine("1) LIST");
                Console.WriteLine("2) UPLOAD");
                Console.WriteLine("3) DOWNLOAD");
                Console.WriteLine("4) RAD SA DATOTEKOM (EDIT/DELETE) [AUTO-LOCK]");
                Console.WriteLine("5) STATS (UDP)");
                Console.WriteLine("6) KRAJ");
                Console.Write("Izbor: ");
                string izbor = Console.ReadLine();

                if (izbor == "6") break;

                if (izbor == "1")
                {
                    TcpSendReceive(clientSocket, "LIST", buffer);
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
                    TcpSendReceive(clientSocket, $"UPLOAD|{name}|{author}|{b64}", buffer);
                }
                else if (izbor == "3")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    string resp = TcpSendReceive(clientSocket, $"DOWNLOAD|{name}", buffer);
     
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
                    WorkWithFile(clientSocket, clientId, buffer);
                }
                else if (izbor == "5")
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

        static void WorkWithFile(Socket clientSocket, string clientId, byte[] buffer)
        {
            Console.Write("Ime fajla: ");
            string name = Console.ReadLine();

            // automatski OPEN (zakljucavanje u pozadini)
            string openResp = TcpSendReceive(clientSocket, $"OPEN|{name}|{clientId}", buffer);
            if (!openResp.StartsWith("OK|OPENED"))
            {
                return;
            }

            bool deleted = false;

            while (true)
            {
                Console.WriteLine("\n--- RAD SA DATOTEKOM ---");
                Console.WriteLine("1) EDIT");
                Console.WriteLine("2) DELETE");
                Console.WriteLine("3) NAZAD (zatvori datoteku)");
                Console.Write("Izbor: ");
                string c = Console.ReadLine();

                if (c == "3") break;

                if (c == "1")
                {
                    Console.Write("Novi sadrzaj (jedna linija): ");
                    string content = Console.ReadLine();
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content ?? ""));
                    TcpSendReceive(clientSocket, $"EDIT|{name}|{clientId}|{b64}", buffer);
                }
                else if (c == "2")
                {
                    TcpSendReceive(clientSocket, $"DELETE|{name}|{clientId}", buffer);
                    deleted = true;
                    break;
                }
                else
                {
                    Console.WriteLine("Nepoznat izbor.");
                }
            }

            // automatski CLOSE (ako fajl nije obrisan)
            if (!deleted)
            {
                TcpSendReceive(clientSocket, $"CLOSE|{name}|{clientId}", buffer);
            }
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
