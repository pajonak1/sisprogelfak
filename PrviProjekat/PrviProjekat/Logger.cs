using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PrviProjekat {
    public static class Logger {
        public enum Event {
            Error,
            Notify,
            Request,
            Synchro,
            Network,
            Critical,
            Response,
        }

        public static void EchoLog(Event logT, string message) {
            string formatted = Format(logT, message);
            if (LogPreformatted(formatted))
                Console.WriteLine(formatted);
        }
        public static void Log(Event logT, string message) 
            => LogPreformatted(Format(logT, message));
        
        private static bool LogPreformatted(string formatted) {
            lock (_lock) {
                try {
                    using (StreamWriter sw = new StreamWriter(path, true)) {
                        sw.WriteLine(formatted);
                    }
                    return true;
                }
                catch (Exception e) {
                    Console.WriteLine(Format(Event.Error, "Caught exception while logging -> " + e.Message));
                    return false;
                }
            }
        }
        public static void EchoLog(HttpListenerRequest request, string additionalInformation = "")
            => EchoLog(Event.Request, FormatRequest(request, additionalInformation));
        
        public static void Log(HttpListenerRequest request, string additionalInformation = "") 
            => Log(Event.Request, FormatRequest(request, additionalInformation));

        public static void Error(string message)
            => EchoLog(Event.Error, message);
        

        private static string Format(Event logT, string message) 
            => $"[{DateTime.Now}] " +
               $"[{logT.ToString()}] " +
               $"Thread {Thread.CurrentThread.ManagedThreadId}: " +
               $"{message}";

        private static string FormatRequest(HttpListenerRequest request, string additionalInformation = "")
            => request.HttpMethod + " " +
               request.Url + " from " +
               request.RemoteEndPoint.ToString() + (additionalInformation == "" ? "" : " -> ") +
               additionalInformation;

        private static readonly object _lock = new();
        private const string path = "ServerLog.txt";
    }
}
