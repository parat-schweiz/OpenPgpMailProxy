using System;
namespace OpenPgpMailProxy
{
    public enum MailboxType
    {
        InboundInput,
        InboundOutput,
        OutboundInput,
        OutboundOutput,
    }

    public interface IMailboxService
    {
        IMailbox Get(string username, MailboxType type);
    }
}
