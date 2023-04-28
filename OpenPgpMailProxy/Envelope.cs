using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MimeKit;

namespace OpenPgpMailProxy
{
    public class Envelope
    {
        public string Id { get; private set; }
        public MimeMessage Message { get; private set; }

        public string Data => Encoding.ASCII.GetString(Bytes);

        public byte[] Bytes
        { 
            get
            {
                using (var stream = new MemoryStream())
                {
                    Message.WriteTo(stream);
                    return stream.ToArray();
                }
            }
        }

        private static MimeMessage ToMessage(string data)
        {
            using (var stream = new MemoryStream(Encoding.ASCII.GetBytes(data)))
            {
                return MimeMessage.Load(stream);
            }
        }

        public Envelope(string id, string data)
         : this(id, ToMessage(data))
        {
        }

        public Envelope(string id, MimeMessage message)
        {
            Id = id;
            Message = message;
        }
    }
}
