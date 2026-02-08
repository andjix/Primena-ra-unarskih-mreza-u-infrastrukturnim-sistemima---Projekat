using Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace RepositoryServer
{
    internal class Program
    {
        const int SOMAXCONN = 126;

        const int REPO_UDP_PORT = 19000;   
        const int REPO_TCP_PORT = 19100;   // RM <-> Repo
        const int RM_TCP_PORT = 19010;     // port koji repo javlja klijentu

        static List<FileData> files = new List<FileData>();
        static object guard = new object();

        static void Main(string[] args)
        {
            Task.Run(() => UdpServer());

            Socket tcpServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpServer.Bind(new IPEndPoint(IPAddress.Any, REPO_TCP_PORT));
            tcpServer.Listen(SOMAXCONN);
            tcpServer.Blocking = false;

            Console.WriteLine($"[REPO] UDP {REPO_UDP_PORT}, TCP {REPO_TCP_PORT}");

            List<Socket> rms = new List<Socket>();

            while (true)
            {
                if (tcpServer.Poll(2000 * 1000, SelectMode.SelectRead))
                {
                    Socket rm = tcpServer.Accept();
                    rm.Blocking = false;
                    rms.Add(rm);
                    Console.WriteLine("[REPO] RM connected.");
                }

                for (int i = rms.Count - 1; i >= 0; i--)
                {
                    Socket rm = rms[i];
                    try
                    {
                        if (!rm.Poll(10 * 1000, SelectMode.SelectRead))
                            continue;

                        object obj = ReceiveObject(rm);
                        if (obj == null)
                        {
                            rm.Close();
                            rms.RemoveAt(i);
                            continue;
                        }

                        Response resp = HandleTcp(obj);
                        SendObject(rm, resp);
                    }
                    catch
                    {
                        try { rm.Close(); } catch { }
                        rms.RemoveAt(i);
                    }
                }
            }
        }

        // UDP server koji odgovara na PRIJAVA, LIST i STATS komande

        static void UdpServer()
        {
            Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udp.Bind(new IPEndPoint(IPAddress.Any, REPO_UDP_PORT));

            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            byte[] buffer = new byte[65536];

            while (true)
            {
                int br = udp.ReceiveFrom(buffer, ref remote);
                object obj = Serialization.FromBytes<object>(buffer.Take(br).ToArray());

                Response resp = HandleUdp(obj);
                byte[] outBytes = Serialization.ToBytes(resp);
                udp.SendTo(outBytes, remote);
            }
        }

        static Response HandleUdp(object obj)
        {
            string s = obj as string;
            if (s == null) return new Response { Ok = false, Message = "BAD_UDP" };

            string[] p = s.Split('|');
            string cmd = p[0].ToUpperInvariant();

            if (cmd == "PRIJAVA")
                return new Response { Ok = true, RmTcpPort = RM_TCP_PORT };

            if (cmd == "LIST")
            {
                lock (guard)
                {
                    return new Response
                    {
                        Ok = true,
                        Files = files.Select(Clone).ToArray()
                    };
                }
            }

            if (cmd == "STATS")
            {
                string clientId = p.Length >= 2 ? p[1] : "";
                DateTime after = DateTime.MinValue;

                if (p.Length >= 3)
                    DateTime.TryParseExact(p[2], "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out after);

                lock (guard)
                {
                    int mem = files
                        .Where(f => f.Author == clientId)
                        .Sum(f => (f.Content ?? "").Length);

                    var latest = files
                        .Where(f => ParseTime(f.LastModified) >= after)
                        .OrderByDescending(f => ParseTime(f.LastModified))
                        .FirstOrDefault();

                    string latestText = latest == null
                        ? "NONE"
                        : $"{latest.Author}|{latest.Name}|{latest.LastModified}";

                    return new Response
                    {
                        Ok = true,
                        StatsText = $"MEM={mem};LATEST_AFTER={latestText}"
                    };
                }
            }

            return new Response { Ok = false, Message = "UNKNOWN_UDP" };
        }

        // TCP

        static Response HandleTcp(object obj)
        {
            if (obj is Request r)
                return HandleRepoRequest(r, null);

            if (obj is object[] arr && arr[0] is Request rr && arr[1] is FileData fd)
                return HandleRepoRequest(rr, fd);

            return new Response { Ok = false, Message = "BAD_TCP" };
        }

        static Response HandleRepoRequest(Request r, FileData f)
        {
            lock (guard)
            {
                if (r.Operation == OperationType.Add)
                {
                    f.Author = r.ClientId;
                    f.LastModified = Now();
                    files.Add(Clone(f));
                    return new Response { Ok = true };
                }

                if (r.Operation == OperationType.Read)
                {
                    var x = files.FirstOrDefault(z => z.Name == r.FileName);
                    if (x == null) return new Response { Ok = false, Message = "NOT_FOUND" };
                    return new Response { Ok = true, File = Clone(x) };
                }

                if (r.Operation == OperationType.Edit)
                {
                    var x = files.FirstOrDefault(z => z.Name == r.FileName);
                    if (x == null) return new Response { Ok = false, Message = "NOT_FOUND" };

                    x.Content = f.Content;
                    x.LastModified = Now();
                    return new Response { Ok = true, File = Clone(x) };
                }

                if (r.Operation == OperationType.Delete)
                {
                    files.RemoveAll(z => z.Name == r.FileName);
                    return new Response { Ok = true };
                }
            }
            return new Response { Ok = false };
        }

        // TCP FRAMING

        static void SendObject(Socket s, object o)
        {
            byte[] data = Serialization.ToBytes(o);
            s.Send(BitConverter.GetBytes(data.Length));
            s.Send(data);
        }

        static object ReceiveObject(Socket s)
        {
            byte[] len = ReceiveAll(s, 4);
            if (len == null) return null;

            byte[] data = ReceiveAll(s, BitConverter.ToInt32(len, 0));
            return Serialization.FromBytes<object>(data);
        }

        static byte[] ReceiveAll(Socket s, int size)
        {
            byte[] b = new byte[size];
            int r = 0;
            while (r < size)
            {
                int x = s.Receive(b, r, size - r, SocketFlags.None);
                if (x == 0) return null;
                r += x;
            }
            return b;
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        static DateTime ParseTime(string s) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;

        static FileData Clone(FileData f) =>
            new FileData { Name = f.Name, Author = f.Author, Content = f.Content, LastModified = f.LastModified };
    }
}
