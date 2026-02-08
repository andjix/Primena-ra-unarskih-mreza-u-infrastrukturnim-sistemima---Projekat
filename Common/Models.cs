using System;

namespace Common
{
    [Serializable]
    public class FileData   // Datoteka
    {
        public string Name;            // Naziv
        public string Author;          // Autor 
        public string LastModified;    // Vremenski trenutak poslednje promene
        public string Content;         // Sadrzaj 
    }

    public enum OperationType
    {
        Add,
        Edit,
        Read,
        Delete 
    }

    [Serializable]
    public class Request    // Zahtev
    {
        public string FileName;        // Putanja/ime datoteke
        public OperationType Operation;
        public string ClientId;        // korisnicko ime (da RM moze da vodi evidenciju)
    }

    [Serializable]
    public class Response
    {
        public bool Ok;
        public string Message;    
        public FileData File;  
        public FileData[] Files;   
        public int RmTcpPort;      // odgovor na PRIJAVA
        public string StatsText;   // odgovor na STATS
    }
}
