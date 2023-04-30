using System;
using ThrowException.CSharpLibs.LogLib;

namespace OpenPgpMailProxy
{
    public class Context
    {
        public Config Config { get; private set; }
        public IMailboxService Mailboxes { get; private set; }
        public ILogger Log { get; private set; }

        public Context(Config config, ILogger log)
        {
            Config = config;
            Log = log;
            Mailboxes = new FolderMailboxService(config.MailboxesPath);
        }
    }
};
