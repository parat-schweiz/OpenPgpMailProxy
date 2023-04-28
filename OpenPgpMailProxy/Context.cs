using System;

namespace OpenPgpMailProxy
{
    public class Context
    {
        public Config Config { get; private set; }
        public IMailboxService Mailboxes { get; private set; }

        public Context(Config config)
        {
            Config = config;
            Mailboxes = new FolderMailboxService(config.MailboxesPath);
        }
    }
};
