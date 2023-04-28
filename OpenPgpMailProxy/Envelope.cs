using System;
using System.Collections.Generic;

namespace OpenPgpMailProxy
{
    public class Envelope
    {
        public string From { get; private set; }
        public IEnumerable<string> To { get; private set; }
        public string Data { get; private set; }

        public Envelope(string from, IEnumerable<string> to, string data)
        {
            From = from;
            To = to;
            Data = data;
        }
    }
}
