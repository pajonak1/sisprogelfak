using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PrviProjekat {
    public class LRUCache {
        public LRUCache(int capacity) {
            cache = new Dictionary<string, LinkedListNode<CacheSlot>>(capacity);
            emptyCount = capacity;
        }
        
        public enum InsertionMethod {
            Simple,
            Replacement
        }

        public ResponeseData this[string request] {
            get => Read(request);
            set => Add(new CacheSlot(request, value));
        }

        public bool Contains(string request)
            => cache.ContainsKey(request);

        public InsertionMethod Add(string request, byte[] body, string contentType)
            => Add(new CacheSlot(request, body, contentType));

        public InsertionMethod Add(CacheSlot value) {
            if (emptyCount > 0) {
                SimpleInsert(value.Requestee, value);
                emptyCount--;
                return InsertionMethod.Simple;
            }
            ReplaceInsert(value.Requestee, value);
            return InsertionMethod.Replacement;
        }
        public ResponeseData Read(string request) {
            LinkedListNode<CacheSlot> slot = cache[request];
            lruChain.Remove(slot);
            lruChain.AddFirst(slot);
            return slot.Value.Response;
        }

        private void ReplaceInsert(string request, CacheSlot response) {
            cache.Remove(lruChain.Last.Value.Requestee);
            lruChain.RemoveLast();
            SimpleInsert(request, response);
        }
        private void SimpleInsert(string request, CacheSlot response)
            => cache.Add(request, lruChain.AddFirst(response));
        
        // Povezana lista je idealna za LRU jer ima O(1) dodavanje na pocetak, izbacivanje sa kraja, kao i promenu pozicije cvora u listi...
        private LinkedList<CacheSlot> lruChain = new();
        // ... Jedina mana je O(n) pretraga, pa zato koristimo pomocni Dictionary koji cuva sve cvorove liste radi O(1) (average) pristupa cvorovima
        private Dictionary<string, LinkedListNode<CacheSlot>> cache;
        private int emptyCount;
    }
}
