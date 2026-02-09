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
            try
            {
                Task.Run(() => UdpServer());

                // uticnice za komunikaciju sa RM
                Socket tcpServer = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                tcpServer.Bind(new IPEndPoint(IPAddress.Any, REPO_TCP_PORT));
                tcpServer.Listen(SOMAXCONN);
                tcpServer.Blocking = false;

                Console.WriteLine($"[REPO] UDP {REPO_UDP_PORT}, TCP {REPO_TCP_PORT}");

                List<Socket> rms = new List<Socket>();

                while (true)
                {
                    // multipleksiranje - slusa sve
                    List<Socket> readSockets = new List<Socket>();
                    readSockets.Add(tcpServer);
                    readSockets.AddRange(rms);

                    Socket.Select(readSockets, null, null, 2000 * 1000);

                    // prihvatanje RM konekcija
                    if (readSockets.Contains(tcpServer))
                    {
                        Socket rm = tcpServer.Accept();
                        rm.Blocking = false;
                        rms.Add(rm);
                        Console.WriteLine("[REPO] RM connected.");
                        readSockets.Remove(tcpServer);
                    }

                    // detektuje da li je TCP konekcija sa upravljacem zahteva prekinuta
                    foreach (Socket rm in readSockets.ToList())
                    {
                        try
                        {
                            object obj = ReceiveObject(rm);
                            if (obj == null)
                            {
                                SafeClose(rm);
                                rms.Remove(rm);
                                continue;
                            }

                            // uspostavlja vezu sa repozitorijumom i prosledjuje zahteve
                            Response resp = HandleTcp(obj) ?? new Response { Ok = false, Message = "REPO_INTERNAL_ERROR" };
                            SendObject(rm, resp);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[REPO] ERROR (tcp): " + ex.Message);
                            SafeClose(rm);
                            rms.Remove(rm);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[REPO] FATAL: " + ex);
                Console.WriteLine("Pritisni bilo koji taster...");
                Console.ReadKey();
            }
        }

        static void SafeClose(Socket s)
        {
            try { s.Shutdown(SocketShutdown.Both); } catch { }
            try { s.Close(); } catch { }
        }

        // UDP server koji odgovara na PRIJAVA, LIST i STATS komande
        static void UdpServer()
        {
            try
            {
                Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                udp.Bind(new IPEndPoint(IPAddress.Any, REPO_UDP_PORT));

                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = new byte[65536];

                while (true)
                {
                    int br;
                    try
                    {
                        br = udp.ReceiveFrom(buffer, ref remote);
                    }
                    catch (SocketException ex)
                    {
                        Console.WriteLine("[REPO] UDP ERROR: " + ex.Message);
                        continue;
                    }

                    object obj;
                    try
                    {
                        obj = Serialization.FromBytes<object>(buffer.Take(br).ToArray());
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("[REPO] UDP BAD_PAYLOAD: " + ex.Message);
                        byte[] outBad = Serialization.ToBytes(new Response { Ok = false, Message = "BAD_UDP_PAYLOAD" });
                        try { udp.SendTo(outBad, remote); } catch { }
                        continue;
                    }

                    // da li handleUdp vraca validan obj ili null
                    Response resp = HandleUdp(obj) ?? new Response { Ok = false, Message = "REPO_INTERNAL_ERROR" };

                    // salje odgovor onom ko je poslao zahtev
                    byte[] outBytes = Serialization.ToBytes(resp);
                    try { udp.SendTo(outBytes, remote); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[REPO] UDP THREAD FATAL: " + ex);
                
            }
        }

        // prima UDP poruku, prepozna komandu i vraca odgovarajuci response
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

        // TCP - da li je dobio komandu ili komandu + fajl
        static Response HandleTcp(object obj)
        {
            if (obj is Request r)
                return HandleRepoRequest(r, null);

            if (obj is object[] arr && arr.Length >= 2 && arr[0] is Request rr && arr[1] is FileData fd)
                return HandleRepoRequest(rr, fd);

            return new Response { Ok = false, Message = "BAD_TCP" };
        }
        
        // rad nad datotekama - ADD, READ, EDIT...
        static Response HandleRepoRequest(Request r, FileData f)
        {
            lock (guard)
            {
                if (r.Operation == OperationType.Add)
                {
                    string name = f?.Name ?? r?.FileName;
                    if (string.IsNullOrWhiteSpace(name))
                        return new Response { Ok = false, Message = "BAD_NAME" };

                    if (files.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                        return new Response { Ok = false, Message = "ALREADY_EXISTS" };

                    if (f == null) return new Response { Ok = false, Message = "NO_FILEDATA" };

                    f.Name = name;

                    // repo dodeljuje autora (ako nije stigao) i postavlja vreme kad primi objekat
                    if (string.IsNullOrWhiteSpace(f.Author))
                        f.Author = r.ClientId;

                    f.LastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

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

                    if (f == null) return new Response { Ok = false, Message = "NO_FILEDATA" };

                    x.Content = f.Content;

                    // repo postavlja vreme kad primi izmenjeni objekat
                    x.LastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                    return new Response { Ok = true, File = Clone(x) };
                }


                if (r.Operation == OperationType.Delete)
                {
                    int removed = files.RemoveAll(z => z.Name == r.FileName);
                    if (removed == 0)
                        return new Response { Ok = false, Message = "NOT_FOUND" };

                    return new Response { Ok = true };
                }
            }

            return new Response { Ok = false, Message = "UNKNOWN_OP" };
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

            int n = BitConverter.ToInt32(len, 0);
            if (n <= 0) return null;

            byte[] data = ReceiveAll(s, n);
            if (data == null) return null;

            try
            {
                return Serialization.FromBytes<object>(data);
            }
            catch
            {
                return null;
            }
        }

        static byte[] ReceiveAll(Socket s, int size)
        {
            try
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
            catch
            {
                return null;
            }
        }

        static DateTime ParseTime(string s) =>
            DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;

        // saljemo kopiju da bismo zastitili internu listu
        static FileData Clone(FileData f) =>
            new FileData
            {
                Name = f.Name,
                Author = f.Author,
                Content = f.Content,
                LastModified = f.LastModified
            };
    }
}
