using Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace RequestManager
{
    internal class Program
    {
        const int SOMAXCONN = 126;

        const int RM_TCP_PORT = 19010;    // Client -> RM
        const int REPO_TCP_PORT = 19100;  // RM -> Repo
        static readonly IPAddress REPO_IP = IPAddress.Loopback;

        static Dictionary<Socket, string> clientIds = new Dictionary<Socket, string>();

       
        static List<Request> activeRequests = new List<Request>();
        static object guard = new object();

        static void Main(string[] args)
        {
            try
            {
                Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                server.Bind(new IPEndPoint(IPAddress.Any, RM_TCP_PORT));
                server.Listen(SOMAXCONN);
                server.Blocking = false;

                Console.WriteLine($"[RM] TCP listening on {IPAddress.Any}:{RM_TCP_PORT}");

                List<Socket> clients = new List<Socket>();

                while (true)
                {
                    List<Socket> readSockets = new List<Socket>();
                    readSockets.Add(server);
                    readSockets.AddRange(clients);

                    Socket.Select(readSockets, null, null, 2000 * 1000);

                    // novi klijent
                    if (readSockets.Contains(server))
                    {
                        Socket c = server.Accept();
                        c.Blocking = false;
                        clients.Add(c);
                        Console.WriteLine("[RM] Client connected.");
                        readSockets.Remove(server);
                    }

                    // poruke od klijenata
                    foreach (Socket c in readSockets.ToList())
                    {
                        try
                        {
                            object obj = ReceiveObject(c);
                            if (obj == null)
                            {
                                ReleaseClientLocks(c);
                                SafeClose(c);
                                clients.Remove(c);
                                Console.WriteLine("[RM] Client disconnected.");
                                continue;
                            }

                           
                            if (obj is Request rq && !string.IsNullOrWhiteSpace(rq.ClientId))
                                clientIds[c] = rq.ClientId;
                            else if (obj is object[] arr && arr.Length > 0 && arr[0] is Request rq2)
                                clientIds[c] = rq2.ClientId;

                            Response resp = HandleClient(obj);

                            
                            if (resp == null)
                                resp = new Response { Ok = false, Message = "RM_INTERNAL_ERROR" };

                            SendObject(c, resp);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("[RM] ERROR (client): " + ex.Message);
                            ReleaseClientLocks(c);
                            SafeClose(c);
                            clients.Remove(c);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                
                Console.WriteLine("[RM] FATAL: " + ex);
                Console.WriteLine("Pritisni bilo koji taster...");
                Console.ReadKey();
            }
        }

        // TCP FRAMING

        static void SendObject(Socket s, object obj)
        {
            byte[] payload = Serialization.ToBytes(obj);
            byte[] length = BitConverter.GetBytes(payload.Length);

            s.Send(length);
            s.Send(payload);
        }

        static object ReceiveObject(Socket s)
        {
            byte[] lenBuf = ReceiveAll(s, 4);
            if (lenBuf == null) return null;

            int len = BitConverter.ToInt32(lenBuf, 0);
            if (len <= 0) return null;

            byte[] data = ReceiveAll(s, len);
            if (data == null) return null;

            return Serialization.FromBytes<object>(data);
        }

        static byte[] ReceiveAll(Socket s, int size)
        {
            try
            {
                byte[] buffer = new byte[size];
                int received = 0;

                while (received < size)
                {
                    int r = s.Receive(buffer, received, size - received, SocketFlags.None);
                    if (r == 0) return null;
                    received += r;
                }

                return buffer;
            }
            catch (SocketException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
        }

        static void SafeClose(Socket s)
        {
            try { s.Shutdown(SocketShutdown.Both); } catch { }
            try { s.Close(); } catch { }
        }

        // LOCK

        static void ReleaseClientLocks(Socket c)
        {
            if (!clientIds.TryGetValue(c, out string clientId))
                return;

            lock (guard)
            {
                activeRequests.RemoveAll(r =>
                    r.Operation == OperationType.Edit &&
                    r.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            }

            clientIds.Remove(c);
        }

        // CLIENT HANDLING

        static Response HandleClient(object obj)
        {
            try
            {
                if (obj is Request req)
                {
                    if (req.Operation == OperationType.Add)
                        return new Response { Ok = false, Message = "SEND_FILEDATA_TOO" };

                    if (req.Operation == OperationType.Read)
                        return ForwardToRepo(req, null);

                    if (req.Operation == OperationType.Delete)
                    {
                        if (IsLockedByOther(req.FileName, req.ClientId))
                            return new Response { Ok = false, Message = "ODBIJENO" };

                        Lock(req.FileName, req.ClientId);
                        Response resp = ForwardToRepo(req, null);
                        Unlock(req.FileName, req.ClientId);
                        return resp;
                    }

                    if (req.Operation == OperationType.Edit)
                    {
                        if (IsLockedByOther(req.FileName, req.ClientId))
                            return new Response { Ok = false, Message = "ODBIJENO" };

                        Lock(req.FileName, req.ClientId);

                        Response get = ForwardToRepo(new Request
                        {
                            ClientId = req.ClientId,
                            FileName = req.FileName,
                            Operation = OperationType.Read
                        }, null);

                        if (get == null || !get.Ok)
                        {
                            Unlock(req.FileName, req.ClientId);
                            return get ?? new Response { Ok = false, Message = "REPO_NO_RESPONSE" };
                        }

                        return new Response { Ok = true, Message = "EDIT_FILE", File = get.File };
                    }
                }

                if (obj is object[] arr2 && arr2.Length >= 2 &&
                    arr2[0] is Request r && arr2[1] is FileData f)
                {
                    if (r.Operation == OperationType.Add)
                    {
                        f.Author = r.ClientId;
                        return ForwardToRepo(r, f);
                    }

                    if (r.Operation == OperationType.Edit)
                    {
                        if (IsLockedByOther(r.FileName, r.ClientId))
                            return new Response { Ok = false, Message = "ODBIJENO" };

                        if (!IsLockedBySame(r.FileName, r.ClientId))
                            return new Response { Ok = false, Message = "NOT_IN_EDIT" };

                        Response resp = ForwardToRepo(r, f);
                        Unlock(r.FileName, r.ClientId);
                        return resp;
                    }
                }

                return new Response { Ok = false, Message = "BAD_FORMAT" };
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RM] ERROR (HandleClient): " + ex.Message);
                return new Response { Ok = false, Message = "RM_INTERNAL_ERROR" };
            }
        }

        

        static Response ForwardToRepo(Request req, FileData file)
        {
            try
            {
                Socket repo = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                repo.Connect(new IPEndPoint(REPO_IP, REPO_TCP_PORT));

                object payload = (file == null) ? (object)req : new object[] { req, file };
                SendObject(repo, payload);

                object o = ReceiveObject(repo);
                SafeClose(repo);

                if (o == null)
                    return new Response { Ok = false, Message = "REPO_NO_RESPONSE" };

                return (Response)o;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RM] ERROR (Repo): " + ex.Message);
                return new Response { Ok = false, Message = "REPO_DOWN" };
            }
        }

        static string Now() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

        // LOCK HELPERS

        static bool IsLockedByOther(string fileName, string clientId)
        {
            lock (guard)
            {
                return activeRequests.Any(r =>
                    r.Operation == OperationType.Edit &&
                    r.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                    !r.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            }
        }

        static bool IsLockedBySame(string fileName, string clientId)
        {
            lock (guard)
            {
                return activeRequests.Any(r =>
                    r.Operation == OperationType.Edit &&
                    r.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                    r.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            }
        }

        static void Lock(string fileName, string clientId)
        {
            lock (guard)
            {
                activeRequests.Add(new Request
                {
                    FileName = fileName,
                    ClientId = clientId,
                    Operation = OperationType.Edit
                });
            }
        }

        static void Unlock(string fileName, string clientId)
        {
            lock (guard)
            {
                activeRequests.RemoveAll(r =>
                    r.Operation == OperationType.Edit &&
                    r.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase) &&
                    r.ClientId.Equals(clientId, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
