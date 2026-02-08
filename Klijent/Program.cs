using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace FileClient
{
    internal class Client
    {
        // RM 
        static int TCP_PORT = 19010;

        // RM UDP
        const int RM_UDP_PORT = 19011;

        // Repo UDP za PRIJAVA/LIST/STATS
        const int REPO_UDP_PORT = 19000;

        static void Main(string[] args)
        {
            byte[] buffer = new byte[8192];

            Console.Write("Unesi ClientId (npr. Filip): ");
            string clientId = Console.ReadLine();

            Console.WriteLine("Pritisni Enter za PRIJAVU na repozitorijum...");
            Console.ReadLine();

            // UDP prijava repo -> dobij RM TCP port
            int rmPort = UdpPrijavaGetRmPort(IPAddress.Loopback.ToString(), REPO_UDP_PORT, clientId);
            if (rmPort > 0) TCP_PORT = rmPort;

            // UDP LIST -> ispisi datoteke po tekstu
            UdpListAndPrint(IPAddress.Loopback.ToString(), REPO_UDP_PORT);

            // TCP konekcija na RM
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Loopback, TCP_PORT);

            Console.WriteLine("Pritisni Enter za povezivanje na upravljac zahteva...");
            Console.ReadLine();

            clientSocket.Connect(serverEP);
            Console.WriteLine("Povezan na upravljac zahteva!");

            // HELLO da RM zna koji je clientId vezan za ovaj socket
            TcpSendReceive(clientSocket, $"HELLO|{clientId}", buffer);

            while (true)
            {
                Console.WriteLine("\n--- MENI ---");
                Console.WriteLine("1) LIST");
                Console.WriteLine("2) UPLOAD");
                Console.WriteLine("3) DOWNLOAD");
                Console.WriteLine("4) RAD SA DATOTEKOM (EDIT/DELETE) [AUTO-LOCK]");
                Console.WriteLine("5) STATS");
                Console.WriteLine("6) KRAJ");
                Console.Write("Izbor: ");
                string izbor = Console.ReadLine();

                if (izbor == "6") break;

                if (izbor == "1")
                {
                    string resp = TcpSendReceive(clientSocket, "LIST", buffer);
                    PrintListFromResp(resp);
                }
                else if (izbor == "2")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    string author = clientId;

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
                    // OK|DOWNLOAD|author|lastModified|b64
                    if (parts.Length >= 5 && parts[0] == "OK" && parts[1] == "DOWNLOAD")
                    {
                        string author = parts[2];
                        string lastMod = parts[3];
                        string content = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4]));
                        Console.WriteLine($"Autor: {author}");
                        Console.WriteLine($"Poslednja promena: {lastMod}");
                        Console.WriteLine($"Sadrzaj: {content}");
                    }
                    else
                    {
                        Console.WriteLine("Neuspesno citanje.");
                    }
                }
                else if (izbor == "4")
                {
                    WorkWithFile(clientSocket, clientId, buffer);
                }
                else if (izbor == "5")
                {
                    UdpStats(IPAddress.Loopback.ToString(), RM_UDP_PORT, clientId);
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

            // OPEN (RM ce vratiti ODBIJENO ako je zauzeto)
            string openResp = TcpSendReceive(clientSocket, $"OPEN|{name}|{clientId}", buffer);
            if (openResp == "ODBIJENO")
            {
                Console.WriteLine("Datoteka je zauzeta. ODBIJENO.");
                return;
            }
            if (!openResp.StartsWith("OK|OPENED"))
            {
                return;
            }

            bool deleted = false;

            while (true)
            {
                Console.WriteLine("\n--- RAD SA DATOTEKOM ---");
                Console.WriteLine("1) EDIT (zameni ceo sadrzaj)");
                Console.WriteLine("2) EDIT (dodaj na kraj)");
                Console.WriteLine("3) DELETE");
                Console.WriteLine("4) NAZAD (zatvori datoteku)");
                Console.Write("Izbor: ");
                string c = Console.ReadLine();

                if (c == "4") break;

                if (c == "1")
                {
                    Console.Write("Novi sadrzaj (jedna linija): ");
                    string content = Console.ReadLine();
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(content ?? ""));
                    TcpSendReceive(clientSocket, $"EDIT|{name}|{clientId}|{b64}", buffer);
                }
                else if (c == "2")
                {
                    // prvo skida sadrzaj, pa dodaje
                    string d = TcpSendReceive(clientSocket, $"DOWNLOAD|{name}", buffer);
                    var parts = d.Split('|');
                    if (!(parts.Length >= 5 && parts[0] == "OK" && parts[1] == "DOWNLOAD"))
                    {
                        Console.WriteLine("Ne mogu da procitam sadrzaj za append.");
                        continue;
                    }

                    string oldContent = Encoding.UTF8.GetString(Convert.FromBase64String(parts[4]));
                    Console.Write("Tekst koji dodajes: ");
                    string add = Console.ReadLine();

                    string newContent = (oldContent ?? "") + (add ?? "");
                    string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(newContent));
                    TcpSendReceive(clientSocket, $"EDIT|{name}|{clientId}|{b64}", buffer);
                }
                else if (c == "3")
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

        static int UdpPrijavaGetRmPort(string host, int port, string clientId)
        {
            try
            {
                using (UdpClient udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2000;
                    string reqStr = $"PRIJAVA|{clientId}";
                    byte[] req = Encoding.UTF8.GetBytes(reqStr);
                    udp.Send(req, req.Length, host, port);

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = udp.Receive(ref remote);
                    string s = Encoding.UTF8.GetString(resp);

                    Console.WriteLine("UDP PRIJAVA odgovor: " + s);

                    // OK|PRIJAVA
                    var p = s.Split('|');
                    if (p.Length >= 4 && p[0] == "OK" && p[1] == "PRIJAVA" && int.TryParse(p[3], out int rmPort))
                        return rmPort;

                    return 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("UDP PRIJAVA greska: " + ex.Message);
                return 0;
            }
        }

        static void UdpListAndPrint(string host, int port)
        {
            try
            {
                using (UdpClient udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2000;

                    byte[] req = Encoding.UTF8.GetBytes("LIST");
                    udp.Send(req, req.Length, host, port);

                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] resp = udp.Receive(ref remote);
                    string s = Encoding.UTF8.GetString(resp);

                    Console.WriteLine("UDP LIST odgovor: " + s);
                    PrintListFromResp(s);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("UDP LIST greska: " + ex.Message);
            }
        }

        static void PrintListFromResp(string resp)
        {
            var parts = resp.Split('|');
            if (parts.Length < 3 || parts[0] != "OK" || parts[1] != "LIST")
                return;

            if (parts[2] == "EMPTY")
            {
                Console.WriteLine("Nema datoteka.");
                return;
            }

            Console.WriteLine("\n--- LISTA DATOTEKA ---");
            var items = parts[2].Split(';');
            foreach (var it in items)
            {
                var f = it.Split(',');
                if (f.Length >= 3)
                {
                    string name = f[0];
                    string author = f[1];
                    string lastMod = f[2];
                    Console.WriteLine($"{name} ([{author}]) poslednja promena: {lastMod}");
                }
            }
        }

        static void UdpStats(string host, int port, string clientId)
        {
            try
            {
                using (UdpClient udp = new UdpClient())
                {
                    udp.Client.ReceiveTimeout = 2000;

                    Console.Write("Unesi datum (YYYY-MM-DD) za 'najskorije posle datuma': ");
                    string date = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(date)) date = DateTime.Now.ToString("yyyy-MM-dd");

                    string reqStr = $"STATS|{clientId}|{date}";
                    byte[] req = Encoding.UTF8.GetBytes(reqStr);
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
