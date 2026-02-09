using Common;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ConstrainedExecution;

namespace FileClient
{
    internal class Program
    {
        const int REPO_UDP_PORT = 19000;
        static int rmPort;

        static void Main(string[] args)
        {
            try
            {
                Console.Write("Unesite ClientId (npr. Filip): ");
                string clientId = Console.ReadLine();

                // PRIJAVA
                Response prijava = UdpSend("PRIJAVA|" + clientId);
                if (prijava == null || !prijava.Ok)
                {
                    Console.WriteLine("PRIJAVA nije uspela: " + prijava?.Message);
                    Console.WriteLine("Pritisni bilo koji taster da zatvoris...");
                    Console.ReadKey();
                    return;
                }

                rmPort = prijava.RmTcpPort;

                
                Response startList = UdpSend("LIST");
                PrintList(startList);

                Socket tcp = null;

                // proverava da li je veza aktivna i omogucava ponovno povezivanje ako pukne
                bool EnsureConnected()
                {
                    try
                    {
                        if (tcp != null && tcp.Connected)
                            return true;

                        try { tcp?.Close(); } catch { }
                        tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                        tcp.Connect(new IPEndPoint(IPAddress.Loopback, rmPort));
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Ne mogu da se povezem sa RM (TCP): " + ex.Message);
                        return false;
                    }
                }

                // inicijalno povezivanje
                EnsureConnected();

                while (true)
                {
                    Console.WriteLine("\n--- MENI ---");
                    Console.WriteLine("1) ADD");
                    Console.WriteLine("2) READ");
                    Console.WriteLine("3) EDIT");
                    Console.WriteLine("4) DELETE");
                    Console.WriteLine("5) STATS");
                    Console.WriteLine("6) LIST");
                    Console.WriteLine("7) RECONNECT");
                    Console.WriteLine("8) EXIT");
                    Console.Write("Izbor: ");

                    string c = Console.ReadLine();

                    if (c == "8")
                        break;

                    if (c == "6")
                    {
                        Response respList = UdpSend("LIST");
                        PrintList(respList);
                        continue;
                    }

                    if (c == "5")
                    {
                        Console.Write("Date yyyy-MM-dd: ");
                        Console.WriteLine(UdpSend($"STATS|{clientId}|{Console.ReadLine()}")?.StatsText);
                        continue;
                    }

                    if (c == "7")
                    {
                        
                        EnsureConnected();
                        continue;
                    }

                    
                    if (!EnsureConnected())
                    {
                        Console.WriteLine("TCP veza nije dostupna. Probaj RECONNECT (7).");
                        continue;
                    }

                    if (c == "1")
                    {
                        FileData f = new FileData();
                        Console.Write("Ime fajla: ");
                        f.Name = Console.ReadLine();
                        Console.Write("Sadrzaj: ");
                        f.Content = Console.ReadLine();

                        Request r = new Request
                        {
                            ClientId = clientId,
                            FileName = f.Name,
                            Operation = OperationType.Add
                        };

                        SendObject(tcp, new object[] { r, f });

                        if (!TryReceiveResponse(tcp, out Response resp))
                        {
                            Console.WriteLine("Veza prekinuta tokom ADD. Probaj RECONNECT (7).");
                            continue;
                        }

                        if (resp.Ok)
                        {
                            Console.WriteLine("Uspesno dodavanje");
                        }
                        else if (string.Equals(resp.Message, "ALREADY_EXISTS", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Fajl sa tim imenom vec postoji.");
                        }
                        else if (string.Equals(resp.Message, "BAD_NAME", StringComparison.OrdinalIgnoreCase))
                        {
                            Console.WriteLine("Naziv fajla ne sme biti prazan.");
                        }
                        else
                        {
                            Console.WriteLine("ERROR - ADD File: " + resp.Message);
                        }

                        continue;
                    }

                    if (c == "2")
                    {
                        Console.Write("Ime fajla: ");
                        string name = Console.ReadLine();

                        SendObject(tcp, new Request { ClientId = clientId, FileName = name, Operation = OperationType.Read });

                        if (!TryReceiveResponse(tcp, out Response resp))
                        {
                            Console.WriteLine("Veza prekinuta tokom READ. Probaj RECONNECT (7).");
                            continue;
                        }

                        if (!resp.Ok) Console.WriteLine(resp.Message);
                        else PrintFile(resp.File);

                        continue;
                    }

                    if (c == "3")
                    {
                        Console.Write("Ime fajla: ");
                        string name = Console.ReadLine();

                        SendObject(tcp, new Request { ClientId = clientId, FileName = name, Operation = OperationType.Edit });

                        if (!TryReceiveResponse(tcp, out Response r1))
                        {
                            Console.WriteLine("Veza prekinuta tokom EDIT (1. korak). Probaj RECONNECT (7).");
                            continue;
                        }

                        if (!r1.Ok)
                        {
                            Console.WriteLine(r1.Message);
                            continue;
                        }

                        Console.WriteLine("Trenutni sadrzaj: " + r1.File?.Content);

                        string mode;
                        while (true)
                        {
                            Console.WriteLine("1) Replace  2) Append");
                            Console.Write("Izbor (1/2): ");
                            mode = Console.ReadLine()?.Trim();
                            if (mode == "1" || mode == "2") break;
                            Console.WriteLine("Pogresan unos. Unesi samo 1 ili 2.");
                        }

                        Console.Write("Unesi tekst: ");
                        string txt = Console.ReadLine();

                        if (mode == "2")
                            r1.File.Content = (r1.File.Content ?? "") + txt;
                        else
                            r1.File.Content = txt;

                        SendObject(tcp, new object[] {
                            new Request{ ClientId = clientId, FileName = name, Operation = OperationType.Edit },
                            r1.File
                        });

                        if (!TryReceiveResponse(tcp, out Response resp2))
                        {
                            Console.WriteLine("Veza prekinuta tokom EDIT (2. korak). Probaj RECONNECT (7).");
                            continue;
                        }

                        if (resp2.Ok)
                        {
                            Console.WriteLine("Datoteka izmenjena.");
                            PrintFile(resp2.File);
                        }
                        else
                        {
                            Console.WriteLine("Izmena nije uspela: " + resp2.Message);
                        }

                        continue;
                    }

                    if (c == "4")
                    {
                        Console.Write("Ime fajla: ");
                        string name = Console.ReadLine();

                        SendObject(tcp, new Request { ClientId = clientId, FileName = name, Operation = OperationType.Delete });

                        if (!TryReceiveResponse(tcp, out Response resp))
                        {
                            Console.WriteLine("Veza prekinuta tokom DELETE. Probaj RECONNECT (7).");
                            continue;
                        }

                        if (resp.Ok)
                            Console.WriteLine("Uspesno brisanje.");
                        else
                            Console.WriteLine("Brisanje nije uspelo: " + resp.Message);

                        continue;
                    }

                    Console.WriteLine("Nepoznata opcija.");
                }

                try { tcp?.Close(); } catch { }
            }
            catch (Exception ex)
            {
                
                Console.WriteLine("FATAL: " + ex);
            }

            Console.WriteLine("Pritisni bilo koji taster da zatvoris...");
            Console.ReadKey();
        }

        // UDP
        static Response UdpSend(string msg)
        {
            try
            {
                Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                byte[] outBytes = Serialization.ToBytes(msg);
                udp.SendTo(outBytes, new IPEndPoint(IPAddress.Loopback, REPO_UDP_PORT));

                byte[] buf = new byte[65536];
                EndPoint r = new IPEndPoint(IPAddress.Any, 0);
                int br = udp.ReceiveFrom(buf, ref r);
                udp.Close();

                return Serialization.FromBytes<Response>(buf.Take(br).ToArray());
            }
            catch (Exception ex)
            {
                return new Response { Ok = false, Message = "UDP_ERROR: " + ex.Message };
            }
        }

        // TCP FRAMING
        static void SendObject(Socket s, object o)
        {
            byte[] d = Serialization.ToBytes(o);
            s.Send(BitConverter.GetBytes(d.Length));
            s.Send(d);
        }

        // pokusava da primi odgovor sa servera
        static bool TryReceiveResponse(Socket s, out Response resp)
        {
            resp = null;

            object o = ReceiveObject(s);
            if (o == null)
            {
                try { s.Close(); } catch { }
                return false;
            }

            resp = (Response)o;
            return true;
        }

        // prima jedan ceo objekat preko TCP-a
        static object ReceiveObject(Socket s)
        {
            byte[] len = ReceiveAll(s, 4);
            if (len == null) return null;

            int n = BitConverter.ToInt32(len, 0);
            if (n <= 0) return null;

            byte[] data = ReceiveAll(s, n);
            if (data == null) return null;

            return Serialization.FromBytes<object>(data);
        }

        // prima tacno zadati broj bajtova sa soketa
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

        static void PrintList(Response r)
        {
            if (r == null || r.Files == null) return;
            foreach (var f in r.Files)
                Console.WriteLine($"{f.Name} ([{f.Author}]) poslednja promena: {f.LastModified}");
        }

        static void PrintFile(FileData f)
        {
            if (f == null) return;
            Console.WriteLine($"{f.Name} | {f.Author} | {f.LastModified}");
            Console.WriteLine(f.Content);
        }
    }
}
