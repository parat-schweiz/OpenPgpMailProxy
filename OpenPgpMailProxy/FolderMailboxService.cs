using System;
using System.Collections.Generic;
using System.IO;

namespace OpenPgpMailProxy
{
    public class FolderMailboxService : IMailboxService
    {
        private readonly string _path;
        private Dictionary<string, object> _lockers;

        public FolderMailboxService(string path)
        {
            _path = path;
            _lockers = new Dictionary<string, object>();
        }

        private string GetUserFolder(string username)
        {
            var path = Path.Combine(_path, username);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private string GetMailboxFolder(string username, MailboxType type)
        {
            var userPath = GetUserFolder(username);
            var path = Path.Combine(userPath, type.ToString());
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }

        private object GetLocker(string label)
        {
            if (!_lockers.ContainsKey(label))
            {
                _lockers.Add(label, new object());
            }

            return _lockers[label];
        }

        public IMailbox Get(string username, MailboxType type)
        {
            var path = GetMailboxFolder(username, type);

            return new FolderMailbox(path, GetLocker(path));
        }
    }
}
