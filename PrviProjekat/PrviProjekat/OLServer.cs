using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace PrviProjekat {
    public class OLServer {
        public OLServer(string serverPath, int cacheSize) {
            this.serverPath = serverPath;
            httpL.Prefixes.Add(serverPath);
            cache = new LRUCache(cacheSize, true);
        }

        public bool Start() {
            try {
                httpL.Start();
                listener = new Thread(Listen);
                listener.Start();
            }
            catch (Exception e) {
                Logger.EchoLog(Logger.Event.Critical, $"Server failed to start: {e.Message}");
                return false;
            }
            Logger.EchoLog(Logger.Event.Notify, $"Started server at web address {serverPath}");
            return true;
        }
        public void Stop() {
            httpL.Stop();
            httpL.Close();
            listener.Join();
            Logger.EchoLog(Logger.Event.Notify, "Server closed");
        }

        private void Listen() {
            while (httpL.IsListening) {
                try {
                    HttpListenerContext request = httpL.GetContext();
                    Logger.EchoLog(request.Request, "Request recieved");
                    ThreadPool.QueueUserWorkItem(ProcessRequest, request);
                }
                catch (Exception e) {
                    Logger.Error(e.Message);
                }
            }
            Logger.Log(Logger.Event.Notify, "Server is no longer listening");
        }
        private void ProcessRequest(object? request) {
            Stopwatch threadTime = new();
            threadTime.Start();
            
            HttpListenerContext context = (HttpListenerContext) request !;
            Logger.Log(context.Request, "Request started processing");
            
            bool requestRight = false;
            bool OLCommunicationProtocolComplete = false;
            string rawUrl = context.Request.RawUrl !;
            ResponseData cachedResponse = null !;
            QueryTranslator translator = new();

            try {
                string arguments = context.Request.Url!.AbsolutePath.ToLower();
                
                if (arguments == "/") {
                    SendResponse(context.Response, "Search OpenLibrary by sending a query to ./search.\n" +
                                                   "Request ./syntax for query syntax description.");
                }
                else if (arguments == "/syntax") {
                    SendResponse(context.Response, 
                                "Syntax:\n" +
                                "  (<authors>|<title>|<subjects>|<publisher>|<key>|<work_year>|<edition_year>)\n" +
                                "  {&(<authors>|<title>|<subjects>|<publisher>|<key>|<work_year>|<edition_year>)}\n" +
                                "  [&<fields_sort_lang_variations>]\n" +
                                "  \n" +
                                "  Argument description:\n" +
                                "      sort          - directly passed to the OLAPI query\n" +
                                "      lang          - two letter acronym of the desired language.\n" +
                                "                      Two letter acronym will promote matching results,\n" +
                                "                      (three letter one will act as a strict query filter (*) - probably will not implement as it doesn't fit nicely in this architecture I have here)\n" +
                                "      fields        - OLAPI response fields to forward from each match found at OpenLibrary\n" +
                                "      work_year     - solr range for the year the work was first published (*)\n" +
                                "      edition_year  - solr range for the year desired edition was published (*)\n" +
                                "      authors       - comma separated list of a subset of a title's authors (*)\n" +
                                "      title         - a title to search for (*)\n" +
                                "      subjects      - comma separated list of a subset of a title's subjects (*)\n" +
                                "      publisher     - publisher of a title's editions (*)\n" +
                                "      key           - the OLAPI key of a title (*)\n" +
                                "  \n" +
                                "  *Multi-valued queries on this argument translate to an OR chain\n" +
                                "  OLAPI - OpenLibrary API\n");
                }
                else if (arguments == "/search") {
                    translator.Translate(context.Request.QueryString);
                    OLRequest subscription = null!;
                    Logger.EchoLog(Logger.Event.Notify, "Translation chain:\n\t" +
                                                       $"   {context.Request.RawUrl}\n\t" +
                                                       $"   -> {translator.CanonicalSource}\n\t" +
                                                       $"   -> {translator.TranslatedQuery}");

                    lock (_lock) {
                        if (cache.Contains(translator.CanonicalSource)) {
                            Logger.Log(Logger.Event.Notify, "Found response in local cache");
                            cachedResponse = cache[translator.CanonicalSource];
                        }
                        else if (!requestsToOLAPI.ContainsKey(translator.CanonicalSource)) {
                            // posto ova nit prva trazi response koji nije u cache-u ili je izbacen iz njega, mora pribaviti response sa OpenLibrary
                            Logger.EchoLog(Logger.Event.Notify, "Request not cached, but obtained API call privilege");
                            requestRight = true;
                            requestsToOLAPI.Add(translator.CanonicalSource, new OLRequest());
                        }
                        else {
                            // sve ostale niti koje u medjuvremenu (dok se response ne kesira) zele response za isti upit moraju sacekati "fetcher" nit
                            subscription = requestsToOLAPI[translator.CanonicalSource];
                            subscription.Subscribe();
                            Logger.Log(Logger.Event.Notify, $"Awaiting on Thread {subscription.Fetcher} to fetch response from OpenLibrary");
                        }
                    }

                    if (!requestRight && cachedResponse == null) {
                        lock (subscription.Lock) {
                            // Wait se mora ograditi jer inace moze nastupiti deadlock u veoma specificnoj
                            // situaciji: ukoliko se PulsaAll izvrsi kada se neka nit zaustavi
                            // izmedju ovog i prethodnog lock-a
                            // while treba jer navodno OS moze sporadicno da probudi niti koje se blokiraju na wait
                            while (subscription.Response == null && subscription.FetcherAborted == false) {
                                Logger.Log(Logger.Event.Synchro, $"Blocking");
                                Monitor.Wait(subscription.Lock);
                                Logger.Log(Logger.Event.Synchro, $"Woke up");
                            }
                            if (subscription.Response != null) {
                                // ne cita se iz cache-a jer teoretski dok nit dobije pravo pristupa, ako je server veoma opterecen
                                // moguce je da zahtev bude u medjuvremenu izbacen
                                Logger.Log(Logger.Event.Notify, $"Response ready");
                                cachedResponse = subscription.Response;
                            }
                            // moguce je da dodje do izuzetka/greske prilikom slanja zahteva
                            // u tom slucaju postoje dve opcije:
                            // 1) da se jednoj niti koja ceka daje pravo da obavi API poziv
                            //      + mozda je problem bio sporadicne prirode, samo jedan zahtev propada
                            //      - ako je problem na OpenLibrary strani, ovaj proces ce potrajati
                            /*
                            else {
                                Logger.EchoLog(Logger.Event.Notify, "API caller failed, obtained request privilege");
                                requestRight = true;
                                subscription.Unsubscribe();
                            }//*/
                            // 2) da sve niti obustave zahtev
                            //      sada imamo obrnutu situaciju
                            //*
                            else {
                                throw new Exception("API caller failed, aborting my request");
                            }//*/
                        }
                    }
                    if (cachedResponse != null) {
                        Logger.EchoLog(Logger.Event.Response, $"Sending {cachedResponse.Body.Length}B long cached response");
                        SendResponse(context.Response, cachedResponse);
                        return;
                    }

                    Logger.Log(Logger.Event.Network, "Sending request to OpenLibrary's API");
                    if (!OLFetch(translator.TranslatedQuery, out string response)) {
                        SendResponse(context.Response, "Request to OpenLibrary's API failed (Internal serve error)", 500);
                        throw new WebException(response);
                    }
                    Logger.EchoLog(Logger.Event.Network, "Response acquired successfully");
                    // response od OpenLibrary API-a, osim trazenih radova, sadrzi i metapodatke koje korisnika ovog servera verovatno ne interesuju
                    JsonObject json = JsonNode.Parse(response).AsObject();
                    CacheSlot newEntry;
                    if (json["numFound"].GetValue<int>() == 0) {
                        newEntry = new CacheSlot(translator.CanonicalSource, new ResponseData("Found no results!", 404));
                        Logger.Log(Logger.Event.Notify, "The acquired response contains no work data");
                    }
                    else {
                        foreach (string field in responseJunk) {
                            json.Remove(field);
                        }
                        response = json.ToJsonString();
                        Logger.Log(Logger.Event.Notify, "Stripped excess data from the retrieved JSON object");
                        newEntry = new CacheSlot(translator.CanonicalSource, Encoding.UTF8.GetBytes(response), "application/json; charset=utf-8");
                    }
                    lock (_lock) {
                        OLRequest myRequest = requestsToOLAPI[translator.CanonicalSource];
                        lock (myRequest.Lock) {
                            LRUCache.InsertionMethod insertionType = cache.Add(newEntry);
                            Logger.Log(Logger.Event.Notify, $"Cached {newEntry.Body.Length}B of data; operation type: {insertionType}");
                            // Kao sto smo napomenuli, niti "subsciber-i" koje cekaju na pribavljeni request ga citaju iz specijalnog polja strukture OLRequest
                            // Jer u medjuvremenu ne postoji garancija da ce odgovor ostati u cache-u dovoljno dugo da bi ga svaka nit procitala pod velikim opterecenjima
                            myRequest.Response = newEntry.Response;
                            if (myRequest.SubscriberCount == 0) {
                                Logger.Log(Logger.Event.Synchro, $"No other threads waiting on {translator.CanonicalSource}");
                            }
                            else {
                                Logger.Log(Logger.Event.Synchro, $"Pulsing all threads waiting on {translator.CanonicalSource}");
                                // Pulsiramo samo niti koje cekaju na pribavljeni response
                                Monitor.PulseAll(myRequest.Lock);
                            }
                            requestsToOLAPI.Remove(translator.CanonicalSource);
                        }
                    }
                    OLCommunicationProtocolComplete = true;
                    Logger.EchoLog(Logger.Event.Response, $"Sending {newEntry.Response.Body.Length}B long response");
                    // "fetcher" salje odgovor nakon upisa u kes, kako bi svaka nit koja ceka mogla sto ranije da inicira komunikaciju
                    SendResponse(context.Response, newEntry.Response);
                }
                else {
                    SendResponse(context.Response, "Request unrecognized", 404);
                }
            }
            catch (Exception e) {
                Logger.Error($"Error while processing request {translator.CanonicalSource} -> {e.Message}");
                try {
                    SendResponse(context.Response, $"Internal server error:\n {e.Message}", 500);
                }
                catch {
                    Logger.Error("Couldn't send an error response back to the client because the communication stream has already been closed");
                }
            }
            finally {
                // Postoje slucajevi u kojima se moze iznenada zahtevati obustava rada niti koja salje upit, na primer:
                //  - korisnik ponisti svoji zahtev
                //  - server se nasilno zaustavi
                //  - greske ili visoko opterecenje na strani OpenLibrary-a
                if (requestRight && !OLCommunicationProtocolComplete) {
                    Logger.Error("Left early in communication with OpenLibrary's API");
                    lock (_lock) {
                        if (requestsToOLAPI.TryGetValue(translator.CanonicalSource, out OLRequest myRequest)) {
                            if (myRequest.SubscriberCount > 0) {
                                lock (myRequest.Lock) {
                                    myRequest.FetcherAborted = myRequest.Response == null;
                                    Monitor.PulseAll(myRequest.Lock);
                                    Logger.Log(Logger.Event.Synchro, "Signaled other threads waiting on this request to wake up");
                                }
                            }
                            else {
                                Logger.Log(Logger.Event.Notify, "No trailing requests queued");
                            }
                            // U tim slucajevima je jako bitno da se iz recnika aktivnih request-a ukloni struktura za koju je odgovorna ova nit
                            // kako bi naredna nit koja trazi odgovor na isti request mogla ponovo pokusati sa pribavljanjem
                            // U suprotnom se moze desiti da se zaglavi u isti red u kom cekaju stare niti
                            requestsToOLAPI.Remove(translator.CanonicalSource);
                        }
                    }
                }
                Logger.EchoLog(Logger.Event.Time, $"Finished processing in {threadTime.ElapsedMilliseconds * .001}s");
            }
        }
        private void SendResponse(HttpListenerResponse httpResponse, ResponseData responese)
            => SendResponse(httpResponse, responese.Body, responese.ContentType, responese.StatusCode);

        private void SendResponse(HttpListenerResponse httpResponse, string textResponse, int statusCode = 200)
            => SendResponse(httpResponse, new ResponseData(textResponse, statusCode));

        private void SendResponse(HttpListenerResponse httpResponse, byte[] body, string contentType, int statusCode = 200) {
            httpResponse.StatusCode = statusCode;
            httpResponse.ContentType = contentType;
            httpResponse.ContentLength64 = body.Length;
            httpResponse.OutputStream.Write(body, 0, body.Length);
            httpResponse.OutputStream.Close();
            Logger.Log(Logger.Event.Network, $"Response transfer through network initiated. Response status code: {statusCode}");
        }
        private bool OLFetch(string query, out string response) {
            bool fetched = false;
            string olUrl = apiQueryPrefix + query;
            HttpResponseMessage olResponse = null;
            try {
                olResponse = olClient.GetAsync(olUrl).Result;
                fetched = true;
                olResponse.EnsureSuccessStatusCode();
                response = olResponse.Content.ReadAsStringAsync().Result;
                return true;
            }
            catch (HttpRequestException e) {
                if (fetched) { 
                    response = olResponse.ReasonPhrase + $"({olResponse.StatusCode})";
                    return false;
                }
                response = "Open Library API GET request failed -> " + e.Message;
            }
            catch (Exception e) {
                response = "Open Library API GET request failed -> " + e.Message;
            }
            return false;
        }

        private readonly string serverPath;
        private readonly object _lock = new();
        private Thread listener;
        private LRUCache cache;
        private HttpClient olClient = new();
        private HttpListener httpL = new();
        private Dictionary<string, OLRequest> requestsToOLAPI = new();
        private const string apiQueryPrefix = "https://openlibrary.org/search.json?";
        private static readonly string[] responseJunk = {
            "start",
            "numFoundExact",
            "num_found",
            "documentation_url",
            "q",
            "offset"
        };
    }
}