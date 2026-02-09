using System;

namespace Common
{
    [Serializable]
    public class FileData   
    {
        public string Name;            
        public string Author;          
        public string LastModified;    
        public string Content;         
    }

    public enum OperationType
    {
        Add,
        Edit,
        Read,
        Delete 
    }

    [Serializable]
    public class Request    
    {
        public string FileName;        
        public OperationType Operation;
        public string ClientId;        
    }

    [Serializable]
    public class Response
    {
        public bool Ok;
        public string Message;    
        public FileData File;  
        public FileData[] Files;   
        public int RmTcpPort;      
        public string StatsText;   
    }
}
