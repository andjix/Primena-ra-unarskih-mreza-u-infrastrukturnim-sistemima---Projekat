using Common;
using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace FileClient
{
    internal class Program
    {
        const int REPO_UDP_PORT = 19000;
        static int rmPort;

        static void Main(string[] args)
        {
            Console.Write("Unesite ClientId (npr. Filip): ");
            string clientId = Console.ReadLine();

            // PRIJAVA
            Response prijava = UdpSend("PRIJAVA|" + clientId);
            rmPort = prijava.RmTcpPort;

            // LIST
            Response list = UdpSend("LIST");
            PrintList(list);

            Socket tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcp.Connect(new IPEndPoint(IPAddress.Loopback, rmPort));

            while (true)
            {
                Console.WriteLine("\n--- MENI ---");
                Console.WriteLine("1) ADD");
                Console.WriteLine("2) READ");
                Console.WriteLine("3) EDIT");
                Console.WriteLine("4) DELETE");
                Console.WriteLine("5) STATS");
                Console.WriteLine("6) EXIT");
                Console.Write("Izbor: ");
             
                string c = Console.ReadLine();
                if (c == "6") break;

                if (c == "1")
                {
                    FileData f = new FileData();
                    Console.Write("Ime fajla: "); f.Name = Console.ReadLine();
                    Console.Write("Sadrzaj: "); f.Content = Console.ReadLine();

                    Request r = new Request { ClientId = clientId, FileName = f.Name, Operation = OperationType.Add };
                    SendObject(tcp, new object[] { r, f });
                    Response resp = (Response)ReceiveObject(tcp);

                    if (resp.Ok)
                        Console.WriteLine("Uspesno dodavanje");
                    else
                        Console.WriteLine("ERROR - ADD File: " + resp.Message);


                }

                if (c == "2")
                {
                    Console.Write("Ime fajla: ");
                    SendObject(tcp, new Request { ClientId = clientId, FileName = Console.ReadLine(), Operation = OperationType.Read });
                    PrintFile(((Response)ReceiveObject(tcp)).File);
                }

                if (c == "3")
                {
                    Console.Write("Ime fajla: ");
                    string name = Console.ReadLine();

                    SendObject(tcp, new Request { ClientId = clientId, FileName = name, Operation = OperationType.Edit });
                    Response r1 = (Response)ReceiveObject(tcp);
                    if (!r1.Ok) { Console.WriteLine(r1.Message); continue; }

                    Console.WriteLine("Trenutni sadrzaj: " + r1.File.Content);
                    Console.Write("Novi sadrzaj: ");
                    r1.File.Content = Console.ReadLine();

                    SendObject(tcp, new object[] {
                        new Request{ ClientId = clientId, FileName = name, Operation = OperationType.Edit },
                        r1.File
                    });

                    Response resp = (Response)ReceiveObject(tcp);

                    if (resp.Ok)
                    {
                        Console.WriteLine("Datoteka izmenjena.");
                        PrintFile(resp.File);
                    }
                    else
                    {
                        Console.WriteLine("Izmena nije uspela.");
                    }

                }

                if (c == "4")
                {
                    Console.Write("Ime fajla: ");
                    SendObject(tcp, new Request { ClientId = clientId, FileName = Console.ReadLine(), Operation = OperationType.Delete });
                    Response resp = (Response)ReceiveObject(tcp);

                    if (resp.Ok)
                        Console.WriteLine("Uspesno brisanje.");
                    else
                        Console.WriteLine("Brisanje nije uspelo.");

                }

                if (c == "5")
                {
                    Console.Write("Date yyyy-MM-dd: ");
                    Console.WriteLine(UdpSend($"STATS|{clientId}|{Console.ReadLine()}").StatsText);
                }
            }
            tcp.Close();
        }

        // UDP

        static Response UdpSend(string msg)
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

        //TCP FRAMING

        static void SendObject(Socket s, object o)
        {
            byte[] d = Serialization.ToBytes(o);
            s.Send(BitConverter.GetBytes(d.Length));
            s.Send(d);
        }

        static object ReceiveObject(Socket s)
        {
            byte[] len = ReceiveAll(s, 4);
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
                r += x;
            }
            return b;
        }

        static void PrintList(Response r)
        {
            if (r.Files == null) return;
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
