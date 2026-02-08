using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace RequestManager
{
    internal class Program
    {
        const int SOMAXCONN = 126;

        const int CLIENT_TCP_PORT = 19010; // Client -> RequestManager
        const int CLIENT_UDP_PORT = 19011; // Client -> RequestManager (STATS UDP) 

        const int REPO_TCP_PORT = 19100;   // RequestManager -> Repository
        static readonly IPAddress REPO_IP = IPAddress.Loopback;

        static Dictionary<Socket, string> clientIds = new Dictionary<Socket, string>();

        // evidencija “aktivnih zahteva” (zakljucano po fajlu)
        static Dictionary<string, string> lockedBy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static object locksGuard = new object();

        static void Main(string[] args)
        {
            Task.Run(() => UdpStatsServer());

            Socket serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Any, CLIENT_TCP_PORT);
            serverSocket.Bind(serverEP);

            serverSocket.Blocking = false;
            serverSocket.Listen(SOMAXCONN);

            Console.WriteLine($"[RM] TCP slusa na {serverEP} (klijenti)");
            Console.WriteLine($"[RM] UDP slusa na {IPAddress.Any}:{CLIENT_UDP_PORT} (STATS)");
            Console.WriteLine($"[RM] Prosledjuje u repo {REPO_IP}:{REPO_TCP_PORT}");

            List<Socket> clients = new List<Socket>();
            byte[] buffer = new byte[8192];

            while (true)
            {
                if (serverSocket.Poll(2000 * 1000, SelectMode.SelectRead))
                {
                    Socket c = serverSocket.Accept();
                    c.Blocking = false;
                    clients.Add(c);
                    Console.WriteLine("[RM] Povezan klijent.");
                }

                for (int i = clients.Count - 1; i >= 0; i--)
                {
                    Socket c = clients[i];
                    try
                    {
                        if (c.Poll(10 * 1000, SelectMode.SelectRead))
                        {
                            int br = c.Receive(buffer);

                            if (br == 0)
                            {
                                ReleaseLocksIfKnown(c);

                                Console.WriteLine("[RM] Klijent diskonekt.");
                                c.Close();
                                clients.RemoveAt(i);
                                continue;
                            }

                            string req = Encoding.UTF8.GetString(buffer, 0, br).Trim();
                            if (req.Length == 0) continue;

                            if (req.StartsWith("HELLO|", StringComparison.OrdinalIgnoreCase))
                            {
                                var p = req.Split('|');
                                if (p.Length >= 2) clientIds[c] = p[1];
                            }

                            // presretni OPEN/CLOSE da bi RM bio autoritet za zauzece
                            string resp = HandleWithLocks(req, c);

                            c.Send(Encoding.UTF8.GetBytes(resp));
                        }
                    }
                    catch
                    {
                        ReleaseLocksIfKnown(c);
                        try { c.Close(); } catch { }
                        clients.RemoveAt(i);
                    }
                }
            }
        }

        static string HandleWithLocks(string req, Socket clientSocket)
        {
            var p = req.Split('|');
            string cmd = p[0].ToUpperInvariant();

            if (cmd == "OPEN" && p.Length >= 3)
            {
                string name = p[1];
                string clientId = p[2];

                lock (locksGuard)
                {
                    if (lockedBy.TryGetValue(name, out var who) && !string.Equals(who, clientId, StringComparison.OrdinalIgnoreCase))
                    {
                        return "ODBIJENO";
                    }
                    lockedBy[name] = clientId;
                }

                string repoResp = ForwardToRepository(req);

                // ako repo vrati gresku, lock
                if (!repoResp.StartsWith("OK|OPENED", StringComparison.OrdinalIgnoreCase))
                {
                    lock (locksGuard)
                    {
                        if (lockedBy.TryGetValue(name, out var who) && string.Equals(who, clientId, StringComparison.OrdinalIgnoreCase))
                            lockedBy.Remove(name);
                    }
                }

                return repoResp;
            }

            if (cmd == "CLOSE" && p.Length >= 3)
            {
                string name = p[1];
                string clientId = p[2];

                lock (locksGuard)
                {
                    if (lockedBy.TryGetValue(name, out var who) && string.Equals(who, clientId, StringComparison.OrdinalIgnoreCase))
                        lockedBy.Remove(name);
                }

                return ForwardToRepository(req);
            }

            // ostalo samo prosledi
            return ForwardToRepository(req);
        }

        static void ReleaseLocksIfKnown(Socket c)
        {
            if (clientIds.TryGetValue(c, out string cid))
            {
                // oslobodi sve iz RM evidencije
                lock (locksGuard)
                {
                    var keys = new List<string>();
                    foreach (var kv in lockedBy)
                        if (string.Equals(kv.Value, cid, StringComparison.OrdinalIgnoreCase))
                            keys.Add(kv.Key);

                    foreach (var k in keys) lockedBy.Remove(k);
                }

                // oslobodi i u repo
                ForwardToRepository("RELEASE_ALL|" + cid);
                clientIds.Remove(c);
            }
        }

        static string ForwardToRepository(string request)
        {
            Socket repo = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            repo.Connect(new IPEndPoint(REPO_IP, REPO_TCP_PORT));

            byte[] reqBytes = Encoding.UTF8.GetBytes(request);
            repo.Send(reqBytes);

            byte[] buffer = new byte[8192];
            int br = repo.Receive(buffer);
            string resp = Encoding.UTF8.GetString(buffer, 0, br);

            repo.Close();
            return resp;
        }

        static void UdpStatsServer()
        {
            UdpClient udp = new UdpClient(CLIENT_UDP_PORT);
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                try
                {
                    byte[] reqBytes = udp.Receive(ref remote);
                    string req = Encoding.UTF8.GetString(reqBytes).Trim();

                    // RM prosledi repo-u
                    string resp = "ERROR|UDP_ONLY_STATS";
                    if (req.StartsWith("STATS", StringComparison.OrdinalIgnoreCase))
                        resp = ForwardToRepository(req);

                    byte[] respBytes = Encoding.UTF8.GetBytes(resp);
                    udp.Send(respBytes, respBytes.Length, remote);
                }
                catch
                {
                }
            }
        }
    }
}
