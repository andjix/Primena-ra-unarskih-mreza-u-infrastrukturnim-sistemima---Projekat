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

        // lista aktivnih zahteva (zauzeća)
        static List<Request> activeRequests = new List<Request>();
        static object guard = new object();

        static void Main(string[] args)
        {
            Socket server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            server.Bind(new IPEndPoint(IPAddress.Any, RM_TCP_PORT));
            server.Listen(SOMAXCONN);
            server.Blocking = false;

            Console.WriteLine($"[RM] TCP listening on {IPAddress.Any}:{RM_TCP_PORT}");

            List<Socket> clients = new List<Socket>();

            while (true)
            {
                if (server.Poll(2000 * 1000, SelectMode.SelectRead))
                {
                    Socket c = server.Accept();
                    c.Blocking = false;
                    clients.Add(c);
                    Console.WriteLine("[RM] Client connected.");
                }

                for (int i = clients.Count - 1; i >= 0; i--)
                {
                    Socket c = clients[i];
                    try
                    {
                        if (!c.Poll(10 * 1000, SelectMode.SelectRead))
                            continue;

                        object obj = ReceiveObject(c);
                        if (obj == null)
                        {
                            ReleaseClientLocks(c);
                            c.Close();
                            clients.RemoveAt(i);
                            Console.WriteLine("[RM] Client disconnected.");
                            continue;
                        }

                        // zapamti ClientId zbog lockova
                        if (obj is Request rq && !string.IsNullOrWhiteSpace(rq.ClientId))
                            clientIds[c] = rq.ClientId;
                        else if (obj is object[] arr && arr.Length > 0 && arr[0] is Request rq2)
                            clientIds[c] = rq2.ClientId;

                        Response resp = HandleClient(obj);
                        SendObject(c, resp);
                    }
                    catch
                    {
                        ReleaseClientLocks(c);
                        try { c.Close(); } catch { }
                        clients.RemoveAt(i);
                    }
                }
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
            byte[] data = ReceiveAll(s, len);
            if (data == null) return null;

            return Serialization.FromBytes<object>(data);
        }

        static byte[] ReceiveAll(Socket s, int size)
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

                    if (!get.Ok)
                    {
                        Unlock(req.FileName, req.ClientId);
                        return get;
                    }

                    return new Response { Ok = true, Message = "EDIT_FILE", File = get.File };
                }
            }

            if (obj is object[] arr2 && arr2.Length >= 2 &&
                arr2[0] is Request r && arr2[1] is FileData f)
            {
                if (r.Operation == OperationType.Add)
                    return ForwardToRepo(r, f);

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

        // REPO

        static Response ForwardToRepo(Request req, FileData file)
        {
            Socket repo = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            repo.Connect(new IPEndPoint(REPO_IP, REPO_TCP_PORT));

            object payload = (file == null) ? (object)req : new object[] { req, file };
            SendObject(repo, payload);

            Response resp = (Response)ReceiveObject(repo);
            repo.Close();

            return resp;
        }

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
