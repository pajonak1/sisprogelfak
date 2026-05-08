using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PrviProjekat {
    public class ResponeseData {
        public ResponeseData(string textMessage) {
            Body = Encoding.UTF8.GetBytes(textMessage);
            ContentType = "text/plain; charset=utf-8";
        }
        public ResponeseData(byte[] body, string contentType) {
            Body = body;
            ContentType = contentType;
        }

        public byte[] Body { get; private set; }
        public string ContentType { get; private set; }
    }
}
