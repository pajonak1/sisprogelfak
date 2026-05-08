using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PrviProjekat {
    public class CacheSlot {
        public CacheSlot(string requestee, ResponeseData response) {
            Response = response;
            Requestee = requestee;
        }
        public CacheSlot(string requestee, byte[] body, string contentType) 
        : this(requestee, new ResponeseData(body, contentType)) {
        }

        public byte[] Body { get => Response.Body; }
        public string Requestee { get; private set; }
        public string ContentType { get => Response.ContentType; }
        public ResponeseData Response { get; private set; }
    }
}
