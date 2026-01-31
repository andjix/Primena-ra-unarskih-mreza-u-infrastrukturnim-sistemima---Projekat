using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

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
        const int REPO_TCP_PORT = 19100; // RequestManager <-> Repository

        static Dictionary<string, FileData> files = new Dictionary<string, FileData>(StringComparer.OrdinalIgnoreCase);
        static object locker = new object();

        static void Main(string[] args)
        {
            Socket repoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint repoEP = new IPEndPoint(IPAddress.Any, REPO_TCP_PORT);
            repoSocket.Bind(repoEP);

            repoSocket.Blocking = false;
            repoSocket.Listen(SOMAXCONN);

            Console.WriteLine($"[SERVER] Repozitorijum slusa na {repoEP}");

            List<Socket> accepted = new List<Socket>();
            byte[] buffer = new byte[8192];

            while (true)
            {
                if (repoSocket.Poll(2000 * 1000, SelectMode.SelectRead))
                {
                    Socket s = repoSocket.Accept();
                    s.Blocking = false;
                    accepted.Add(s);
                    Console.WriteLine("[SERVER] Povezan upravljac zahteva.");
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

        static string HandleRequest(string request)
        {
            string[] parts = request.Split('|');
            string cmd = parts[0].ToUpperInvariant();

            switch (cmd)
            {
                case "HELLO":
                    return "OK|HELLO";

                case "LIST":
                    return CmdList();

                case "UPLOAD":
                    if (parts.Length < 4) return "ERROR|BAD_FORMAT";
                    return CmdUpload(parts[1], parts[2], parts[3]);

                case "DOWNLOAD":
                    if (parts.Length < 2) return "ERROR|BAD_FORMAT";
                    return CmdDownload(parts[1]);

                // AUTO LOCK protokol (ne postoji u meniju, ali radi u pozadini)
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

                case "STATS":
                    return CmdStats();

                default:
                    return "ERROR|UNKNOWN_COMMAND";
            }
        }

        static string CmdList()
        {
            lock (locker)
            {
                if (files.Count == 0) return "OK|LIST|EMPTY";

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

                // download je dozvoljen i ako je zaključan (čitanje)
                f.LastAccessUtc = DateTime.UtcNow;

                string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(f.Content ?? ""));
                return "OK|DOWNLOAD|" + f.Author + "|" + b64;
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

                // Mora prvo OPEN da bi mogao EDIT
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

                // Mora prvo OPEN da bi mogao DELETE
                if (!f.IsLocked) return "ERROR|NOT_OPENED";
                if (!string.Equals(f.LockedBy, clientId ?? "", StringComparison.OrdinalIgnoreCase))
                    return "ERROR|LOCKED_BY|" + f.LockedBy;

                files.Remove(name);
                return "OK|DELETED";
            }
        }

        static string CmdStats()
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
