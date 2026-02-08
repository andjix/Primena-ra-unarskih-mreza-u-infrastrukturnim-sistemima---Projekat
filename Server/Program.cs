using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RepositoryServer
{
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

    internal class Program
    {
        const int SOMAXCONN = 126;

        // UDP repo port za PRIJAVA / LIST / STATS
        const int REPO_UDP_PORT = 19000;

        // TCP port za RequestManager <-> Repository
        const int REPO_TCP_PORT = 19100;

        // info koju repo vraca klijentu posle PRIJAVA
        const int RM_TCP_PORT = 19010;

        static Dictionary<string, FileData> files = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
        static object locker = new object();

        static void Main(string[] args)
        {
            // UDP servis za klijente (PRIJAVA/LIST/STATS)
            Task.Run(() => UdpRepoServer());

            Socket repoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint repoEP = new IPEndPoint(IPAddress.Any, REPO_TCP_PORT);
            repoSocket.Bind(repoEP);

            repoSocket.Blocking = false;
            repoSocket.Listen(SOMAXCONN);

            Console.WriteLine($"[SERVER] Repozitorijum TCP slusa na {repoEP}");
            Console.WriteLine($"[SERVER] Repozitorijum UDP slusa na {IPAddress.Any}:{REPO_UDP_PORT}");

            List<Socket> accepted = new List<Socket>();
            byte[] buffer = new byte[8192];

            while (true)
            {
                if (repoSocket.Poll(2000 * 1000, SelectMode.SelectRead))
                {
                    Socket s = repoSocket.Accept();
                    s.Blocking = false;
                    accepted.Add(s);
                    Console.WriteLine("[SERVER] Povezan upravljac zahteva (TCP).");
                }

                for (int i = accepted.Count - 1; i >= 0; i--)
                {
                    Socket s = accepted[i];
                    try
                    {
                        if (s.Poll(10 * 1000, SelectMode.SelectRead))
                        {
                            int br = s.Receive(buffer);

                            if (br == 0)
                            {
                                Console.WriteLine("[SERVER] Upravljač zahteva diskonekt.");
                                s.Close();
                                accepted.RemoveAt(i);
                                continue;
                            }

                            string req = Encoding.UTF8.GetString(buffer, 0, br).Trim();
                            if (req.Length == 0) continue;

                            string resp = HandleRequest(req);
                            s.Send(Encoding.UTF8.GetBytes(resp));
                        }
                    }
                    catch
                    {
                        try { s.Close(); } catch { }
                        accepted.RemoveAt(i);
                    }
                }
            }
        }

        static void UdpRepoServer()
        {
            using (UdpClient udp = new UdpClient(REPO_UDP_PORT))
            {
                IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

                while (true)
                {
                    try
                    {
                        byte[] reqBytes = udp.Receive(ref remote);
                        string req = Encoding.UTF8.GetString(reqBytes).Trim();
                        if (string.IsNullOrWhiteSpace(req)) continue;

                        string resp = HandleUdpRequest(req);

                        byte[] respBytes = Encoding.UTF8.GetBytes(resp);
                        udp.Send(respBytes, respBytes.Length, remote);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
        }

        static string HandleUdpRequest(string request)
        {
            // PRIJAVA|ClientId
            // LIST
            // STATS|ClientId|YYYY-MM-DD
            var p = request.Split('|');
            string cmd = p[0].ToUpperInvariant();

            switch (cmd)
            {
                case "PRIJAVA":
                    // repo vraca gde je RM TCP
                    return "OK|PRIJAVA|RM_TCP_PORT|" + RM_TCP_PORT;

                case "LIST":
                    return CmdList(includeLastModified: true);

                case "STATS":
                    {
                        string clientId = (p.Length >= 2) ? p[1] : "";
                        DateTime? after = null;
                        if (p.Length >= 3 && DateTime.TryParse(p[2], out var dt))
                            after = dt.ToUniversalTime();

                        return CmdStatsForText(clientId, after);
                    }

                default:
                    return "ERROR|UNKNOWN_UDP_COMMAND";
            }
        }

        static string HandleRequest(string request)
        {
            string[] parts = request.Split('|');
            string cmd = parts[0].ToUpperInvariant();

            switch (cmd)
            {
                case "HELLO":
                    return "OK|HELLO";

                case "LIST":
                    // TCP LIST (preko RM) - vrati i lastModified da klijent moze da ispise po tekstu
                    return CmdList(includeLastModified: true);

                case "UPLOAD":
                    if (parts.Length < 4) return "ERROR|BAD_FORMAT";
                    return CmdUpload(parts[1], parts[2], parts[3]);

                case "DOWNLOAD":
                    if (parts.Length < 2) return "ERROR|BAD_FORMAT";
                    return CmdDownload(parts[1]);

                case "OPEN":
                    if (parts.Length < 3) return "ERROR|BAD_FORMAT";
                    return CmdOpen(parts[1], parts[2]);

                case "CLOSE":
                    if (parts.Length < 3) return "ERROR|BAD_FORMAT";
                    return CmdClose(parts[1], parts[2]);

                case "RELEASE_ALL":
                    if (parts.Length < 2) return "ERROR|BAD_FORMAT";
                    return CmdReleaseAll(parts[1]);

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

        static string CmdList(bool includeLastModified)
        {
            lock (locker)
            {
                if (files.Count == 0) return "OK|LIST|EMPTY";

                var items = files.Values
                    .OrderBy(f => f.Name)
                    .Select(f =>
                    {
                        string lm = includeLastModified ? f.LastModifiedUtc.ToString("yyyy-MM-dd HH:mm:ss") : "";
                        return $"{f.Name},{f.Author},{lm},{(f.IsLocked ? "1" : "0")}";
                    });

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
                return "OK|DOWNLOAD|" + f.Author + "|" + f.LastModifiedUtc.ToString("yyyy-MM-dd HH:mm:ss") + "|" + b64;
            }
        }

        static string CmdOpen(string name, string clientId)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";
                if (f.IsLocked) return "ERROR|LOCKED_BY|" + f.LockedBy;

                f.IsLocked = true;
                f.LockedBy = clientId ?? "";
                return "OK|OPENED";
            }
        }

        static string CmdClose(string name, string clientId)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";
                if (!f.IsLocked) return "ERROR|NOT_LOCKED";

                if (!string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    return "ERROR|LOCKED_BY|" + f.LockedBy;

                f.IsLocked = false;
                f.LockedBy = "";
                return "OK|CLOSED";
            }
        }

        static string CmdReleaseAll(string clientId)
        {
            lock (locker)
            {
                foreach (var f in files.Values)
                {
                    if (f.IsLocked && string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        f.IsLocked = false;
                        f.LockedBy = "";
                    }
                }
                return "OK|RELEASED";
            }
        }

        static string CmdEdit(string name, string clientId, string base64NewContent)
        {
            lock (locker)
            {
                if (!files.TryGetValue(name, out FileData f)) return "ERROR|NOT_FOUND";

                if (!f.IsLocked) return "ERROR|NOT_OPENED";
                if (!string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
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

                if (!f.IsLocked) return "ERROR|NOT_OPENED";
                if (!string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    return "ERROR|LOCKED_BY|" + f.LockedBy;

                files.Remove(name);
                return "OK|DELETED";
            }
        }
        // Autor i naziv datoteke menjane NAJSKORIJE posle odredjenog datuma
        static string CmdStatsForText(string clientId, DateTime? afterUtc)
        {
            lock (locker)
            {
                var myFiles = files.Values
                    .Where(f => string.Equals(f.Author ?? "", clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f.Name)
                    .Select(f => $"{f.Name}:{(f.Content ?? "").Length}");

                string myMemPart = string.Join(";", myFiles);
                if (string.IsNullOrEmpty(myMemPart)) myMemPart = "EMPTY";

                IEnumerable<FileData> candidates = files.Values;
                if (afterUtc.HasValue)
                    candidates = candidates.Where(f => f.LastModifiedUtc >= afterUtc.Value);

                var latest = candidates
                    .OrderByDescending(f => f.LastModifiedUtc)
                    .FirstOrDefault();

                string latestPart = "NONE";
                if (latest != null)
                    latestPart = $"{latest.Author},{latest.Name},{latest.LastModifiedUtc:yyyy-MM-dd HH:mm:ss}";

                return $"OK|STATS|MY_FILES={myMemPart}|LATEST_AFTER={latestPart}";
            }
        }
    }
}
