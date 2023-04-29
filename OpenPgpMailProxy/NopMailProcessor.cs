using System;
namespace OpenPgpMailProxy
{
    public class NopMailProcessor : IMailProcessor
    {
        public Envelope Process(Envelope input, IMailbox errorBox)
        {
            return input;
        }
    }
}
