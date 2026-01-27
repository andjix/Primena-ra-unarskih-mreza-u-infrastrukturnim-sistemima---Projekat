using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace FileServer
{
    class FileData
    {
        public string Name { get; set; }
        public string Content { get; set; }
        public string Author { get; set; }
        public bool IsLocked { get; set; }
    }

    class Program
    {
        static Dictionary<string, FileData> files = new Dictionary<string, FileData>();
        static object locker = new object();

        static void Main(string[] args)
        {
            TcpListener server = new TcpListener(IPAddress.Any, 5000);
            server.Start();

            Console.WriteLine("TCP Server pokrenut na portu 5000...");

            while (true)
            {
                TcpClient client = server.AcceptTcpClient();
                Console.WriteLine("Klijent povezan");

                Task.Run(() => HandleClient(client));
            }
        }

        static void HandleClient(TcpClient client)
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[4096];

            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

            Console.WriteLine("Primljen zahtev: " + request);

            string response = ProcessRequest(request);

            byte[] responseBytes = Encoding.UTF8.GetBytes(response);
            stream.Write(responseBytes, 0, responseBytes.Length);

            client.Close();
        }

        static string ProcessRequest(string request)
        {
            string[] parts = request.Split('|');
            string command = parts[0];

            lock (locker)
            {
                switch (command)
                {
                    case "ADD":
                        return AddFile(parts);

                    case "EDIT":
                        return EditFile(parts);

                    case "DELETE":
                        return DeleteFile(parts);

                    default:
                        return "Nepoznata komanda";
                }
            }
        }

        static string AddFile(string[] parts)
        {
            string name = parts[1];
            string content = parts[2];
            string author = parts[3];

            if (files.ContainsKey(name))
                return "Fajl vec postoji";

            files[name] = new FileData
            {
                Name = name,
                Content = content,
                Author = author,
                IsLocked = false
            };

            return "Fajl uspesno dodat";
        }

        static string EditFile(string[] parts)
        {
            string name = parts[1];
            string newContent = parts[2];

            if (!files.ContainsKey(name))
                return "Fajl ne postoji";

            if (files[name].IsLocked)
                return "Fajl je trenutno zauzet";

            files[name].IsLocked = true;
            files[name].Content = newContent;
            files[name].IsLocked = false;

            return "Fajl uspesno izmenjen";
        }

        static string DeleteFile(string[] parts)
        {
            string name = parts[1];

            if (!files.ContainsKey(name))
                return "Fajl ne postoji";

            if (files[name].IsLocked)
                return "Fajl je trenutno zauzet";

            files.Remove(name);
            return "Fajl uspesno obrisan";
        }
    }
}