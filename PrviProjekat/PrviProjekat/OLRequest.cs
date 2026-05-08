using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PrviProjekat {
    public class OLRequest {
        public OLRequest() {
            Lock = new object();
            Fetcher = Thread.CurrentThread.ManagedThreadId;
            Response = null;
            FetcherAborted = false;
            SubscriberCount = 0;
        }

        public int SubscriberCount { get; private set; }
        public int Fetcher { get; set; }
        public bool FetcherAborted { get; set; }
        public object Lock { get; }
        public ResponeseData Response { get; set; }

        public void Subscribe()
            => SubscriberCount++;

        public void Unsubscribe()
            => SubscriberCount--;
    }
}
