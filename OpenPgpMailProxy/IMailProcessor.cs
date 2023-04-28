using System;

namespace OpenPgpMailProxy
{
    public interface IMailProcessor
    {
        Envelope Process(Envelope input);
    }
}
