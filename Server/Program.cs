using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FileServer
{
    [Serializable]
    class FileData
    {
        public string Name;
        public string Author;
        public string Content;

        public DateTime LastModifiedUtc;
        public DateTime LastAccessUtc;

        public bool IsLocked;
        public string LockedBy;
    }

    internal class Server
    {
        const int SOMAXCONN = 126;

        // TCP port za operacije nad datotekama
        const int TCP_PORT = 19010;

        // UDP port za pregled stanja (STATS)
        const int UDP_PORT = 19011;

        static Dictionary<string, FileData> files = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
        static object locker = new object();

        static void Main(string[] args)
        {
            // UDP server start (u pozadini)
            Task.Run(() => UdpStatsServer());

            // TCP server
            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Any, TCP_PORT);
            serverSocket.Bind(serverEP);

            serverSocket.Blocking = false;
            serverSocket.Listen(SOMAXCONN);

            Console.WriteLine($"TCP Server slusa na {serverEP} (operacije nad datotekama)");
            Console.WriteLine($"UDP Server slusa na {IPAddress.Any}:{UDP_PORT} (STATS)");
            Console.WriteLine("Pritisni Enter za start...");
            Console.ReadLine();

            List<Socket> acceptedSockets = new List<Socket>();
            byte[] buffer = new byte[8192];

            while (true)
            {
                // prihvatanje novih klijenata
                if (serverSocket.Poll(2000 * 1000, SelectMode.SelectRead))
                {
                    Socket acceptedSocket = serverSocket.Accept();
                    acceptedSocket.Blocking = false;
                    acceptedSockets.Add(acceptedSocket);

                    IPEndPoint clientEP = acceptedSocket.RemoteEndPoint as IPEndPoint;
                    Console.WriteLine($"Povezao se klijent: {clientEP}");
                }

                // obrada zahteva (više klijenata)
                for (int i = acceptedSockets.Count - 1; i >= 0; i--)
                {
                    Socket s = acceptedSockets[i];

                    try
                    {
                        if (s.Poll(10 * 1000, SelectMode.SelectRead))
                        {
                            int brBajta = s.Receive(buffer);

                            // klijent prekinuo
                            if (brBajta == 0)
                            {
                                Console.WriteLine("Klijent se diskonektovao.");
                                s.Close();
                                acceptedSockets.RemoveAt(i);
                                continue;
                            }

                            string request = Encoding.UTF8.GetString(buffer, 0, brBajta).Trim();
                            if (request.Length == 0) continue;

                            string response = HandleRequest(request);

                            byte[] respBytes = Encoding.UTF8.GetBytes(response);
                            s.Send(respBytes);
                        }
                    }
                    catch (SocketException)
                    {
                        // greška ili diskonekt -> ukloni socket
                        try { s.Close(); } catch { }
                        acceptedSockets.RemoveAt(i);
                    }
                    catch
                    {
                        
                    }
                }
            }
        }

        static string HandleRequest(string request)
        {
            // Komande: KOMANDA|p1|p2|p3
            string[] parts = request.Split('|');
            string cmd = parts[0].ToUpperInvariant();

            switch (cmd)
            {
                case "LIST":
                    return CmdList();

                case "UPLOAD":
                    if (parts.Length < 4) return "ERROR|BAD_FORMAT";
                    return CmdUpload(parts[1], parts[2], parts[3]);

                case "DOWNLOAD":
                    if (parts.Length < 2) return "ERROR|BAD_FORMAT";
                    return CmdDownload(parts[1]);

                case "LOCK":
                    if (parts.Length < 3) return "ERROR|BAD_FORMAT";
                    return CmdLock(parts[1], parts[2]);

                case "UNLOCK":
                    if (parts.Length < 3) return "ERROR|BAD_FORMAT";
                    return CmdUnlock(parts[1], parts[2]);

                case "EDIT":
                    if (parts.Length < 4) return "ERROR|BAD_FORMAT";
                    return CmdEdit(parts[1], parts[2], parts[3]);

                case "DELETE":
                    if (parts.Length < 3) return "ERROR|BAD_FORMAT";
                    return CmdDelete(parts[1], parts[2]);

                default:
                    return "ERROR|UNKNOWN_COMMAND";
            }
        }

        static string CmdList()
        {
            lock (locker)
            {
                if (files.Count == 0) return "OK|LIST|EMPTY";

                // OK|LIST|name,author,locked;name2,author2,locked
                var items = files.Values
                    .OrderBy(f => f.Name)
                    .Select(f => $"{f.Name},{f.Author},{(f.IsLocked ? "1" : "0")}");
                return "OK|LIST|" + string.Join(";", items);
            }
        }

        static string CmdUpload(string name, string author, string base64Content)
        {
            lock (locker)
            {
                if (string.IsNullOrWhiteSpace(name)) return "ERROR|BAD_NAME";
                if (files.ContainsKey(name)) return "ERROR|ALREADY_EXISTS";

                string content;
                try
                {
                    content = Encoding.UTF8.GetString(Convert.FromBase64String(base64Content));
                }
                catch
                {
                    return "ERROR|BAD_CONTENT";
                }

                files[name] = new FileData
                {
                    Name = name,
                    Author = author ?? "",
                    Content = content ?? "",
                    LastModifiedUtc = DateTime.UtcNow,
                    LastAccessUtc = DateTime.UtcNow,
                    IsLocked = false,
                    LockedBy = ""
                };

                return "OK|UPLOADED";
            }
        }

        static string CmdDownload(string name)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";

                f.LastAccessUtc = DateTime.UtcNow;
                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(f.Content ?? ""));
                return "OK|DOWNLOAD|" + f.Author + "|" + b64;
            }
        }

        static string CmdLock(string name, string clientId)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";
                if (f.IsLocked) return "ERROR|LOCKED_BY|" + f.LockedBy;

                f.IsLocked = true;
                f.LockedBy = clientId ?? "";
                return "OK|LOCKED";
            }
        }

        static string CmdUnlock(string name, string clientId)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";
                if (!f.IsLocked) return "ERROR|NOT_LOCKED";
                if (!string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    return "ERROR|LOCKED_BY|" + f.LockedBy;

                f.IsLocked = false;
                f.LockedBy = "";
                return "OK|UNLOCKED";
            }
        }

        static string CmdEdit(string name, string clientId, string base64NewContent)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";

                // "alarm" uslov: ako je zauzeto od drugog, odbij
                if (f.IsLocked && !string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    return "ERROR|LOCKED_BY|" + f.LockedBy;

                string newContent;
                try
                {
                    newContent = Encoding.UTF8.GetString(Convert.FromBase64String(base64NewContent));
                }
                catch
                {
                    return "ERROR|BAD_CONTENT";
                }

                f.Content = newContent ?? "";
                f.LastModifiedUtc = DateTime.UtcNow;
                return "OK|EDITED";
            }
        }

        static string CmdDelete(string name, string clientId)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";

                // "alarm" uslov
                if (f.IsLocked && !string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    return "ERROR|LOCKED_BY|" + f.LockedBy;

                files.Remove(name);
                return "OK|DELETED";
            }
        }

        static void UdpStatsServer()
        {
            UdpClient udp = new UdpClient(UDP_PORT);
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    byte[] reqBytes = udp.Receive(ref remote);
                    string req = Encoding.UTF8.GetString(reqBytes).Trim();

                    string resp = "ERROR|UDP_ONLY_STATS";
                    if (req.ToUpperInvariant() == "STATS")
                        resp = BuildStats();

                    byte[] respBytes = Encoding.UTF8.GetBytes(resp);
                    udp.Send(respBytes, respBytes.Length, remote);
                }
                catch
                {
                    
                }
            }
        }

        static string BuildStats()
        {
            lock (locker)
            {
                int total = files.Count;
                long mem = files.Values.Sum(f => (long)((f.Content ?? "").Length));

                var byAuthor = files.Values
                    .GroupBy(f => f.Author ?? "")
                    .OrderBy(g => g.Key)
                    .Select(g => $"{g.Key}={g.Count()}");

                return "OK|STATS|TOTAL=" + total + "|MEM=" + mem + "|AUTHORS=" + string.Join(";", byAuthor);
            }
        }
    }
}
