using System;
namespace OpenPgpMailProxy
{
    public class NopMailProcessor : IMailProcessor
    {
        public Envelope Process(Envelope input)
        {
            return input;
        }
    }
}
