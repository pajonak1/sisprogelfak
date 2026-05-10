namespace PrviProjekat {
    class Program {
        static void Main(string[] args) {
            ThreadPool.SetMinThreads(300, 300);
            OLServer server = new OLServer(serverURL, 1000);
            if (!server.Start())
                return;
            
            Logger.RawConsoleLine("Press Enter to stop the server");
            Console.ReadLine();
            server.Stop();
        }

        private const string serverURL = "http://localhost:8080/";
    }
}

// https://openlibrary.org/
/*
    /
    /search?[author, title, year, key][&sort][&lang]
*/