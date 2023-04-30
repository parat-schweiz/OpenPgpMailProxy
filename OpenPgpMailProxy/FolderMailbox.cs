using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MimeKit;
using ThrowException.CSharpLibs.BytesUtilLib;

namespace OpenPgpMailProxy
{
    public class FolderMailbox : IMailbox
    {
        private DirectoryInfo _directory;

        public FolderMailbox(string path)
        {
            _directory = new DirectoryInfo(path);
        }

        public void Delete(Envelope envelope)
        {
            var filename = Path.Combine(_directory.FullName, envelope.Id);
            if (File.Exists(filename))
            {
                File.Delete(filename);
                Console.Error.WriteLine("Mail delete from {0}", filename);
            }
        }

        public void Enqueue(Envelope envelope)
        {
            var filename = Path.Combine(_directory.FullName, envelope.Id);
            using (var file = File.OpenWrite(filename))
            {
                envelope.Message.WriteTo(file);
            }
            Console.Error.WriteLine("Mail written to {0}", filename);
        }

        public bool Exists(Envelope envelope)
        {
            var filename = Path.Combine(_directory.FullName, envelope.Id);
            return File.Exists(filename);
        }

        public IEnumerable<Envelope> List()
        {
            foreach (var fileInfo in _directory.GetFiles())
            {
                var id = fileInfo.Name;
                using (var file = File.OpenRead(fileInfo.FullName))
                {
                    var message = MimeMessage.Load(file);
                    yield return new Envelope(id, message);
                }
            }
        }
    }
}
