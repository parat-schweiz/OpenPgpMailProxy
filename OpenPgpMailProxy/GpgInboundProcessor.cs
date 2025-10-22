using System;
using System.IO;
using System.Linq;
using System.Text;
using MimeKit;
using ThrowException.CSharpLibs.LogLib;

namespace OpenPgpMailProxy
{
    public class GpgInboundProcessor : IMailProcessor
    {
        private readonly Context _context;
        private Gpg _gpg;

        public GpgInboundProcessor(Gpg gpg, Context context)
        {
            _gpg = gpg;
            _context = context;
        }

        private Envelope ProcessSecuredBody(Multipart body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            if (body.Headers[HeaderId.ContentType].Contains("protected-headers=\"v1\""))
            {
                foreach (var headerId in new HeaderId[] { HeaderId.From, HeaderId.To, HeaderId.Subject, HeaderId.MessageId })
                {
                    if (body.Headers.Any(h => h.Id == headerId))
                    {
                        input.Message.Headers[headerId] = body.Headers[headerId];
                    }
                }
                _context.Log.Verbose("Inbound: Protected headers updated");

                if (body.Count == 1)
                {
                    input.Message.Body = body.Single();
                }

                input.Message.Subject = subjectPrefix + input.Message.Subject;
                AutoKeyImport(input.Message.Body);
                _context.Log.Verbose("Inbound: Secured mail body returned");
                return input;
            }
            else
            {
                input.Message.Body = body;
                input.Message.Subject = subjectPrefix + input.Message.Subject;
                AutoKeyImport(input.Message.Body);
                _context.Log.Verbose("Inbound: Secured mail body returned");
                return input;
            }
        }

        private void AutoKeyImport(Multipart body)
        {
            foreach (var part in body)
            {
                AutoKeyImport(part);
            }
        }

        private void AutoKeyImport(MimePart body)
        {
            if (body.Content == null) return;
            var bytes = GetContentBytes(body.Content);
            if ((body.ContentType.MediaType == "application") &&
                (body.ContentType.MediaSubtype == "pgp-keys"))
            {
                foreach (var key in _gpg.ImportKey(bytes))
                {
                    _context.Log.Verbose("Inbound: Public key {0} imported", key.Id);
                }
            }
            else if (IsGpgPublicKey(bytes))
            {
                foreach (var key in _gpg.ImportKey(bytes))
                {
                    _context.Log.Verbose("Inbound: Public key {0} imported", key.Id);
                }
            }
        }

        private bool IsGpgPublicKey(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes).Trim();
            return text.StartsWith("-----BEGIN PGP PUBLIC KEY BLOCK-----", StringComparison.Ordinal);
        }

        private void AutoKeyImport(MimeEntity body)
        {
            if (body is Multipart multipart)
            {
                AutoKeyImport(multipart);
            }
            else if (body is MimePart mimepart)
            {
                AutoKeyImport(mimepart);
            }
        }

        private Envelope ProcessSecuredBody(MimeEntity body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            if (body is Multipart multipart)
            {
                return ProcessSecuredBody(multipart, subjectPrefix, input, errorBox);
            }
            else
            {
                _context.Log.Verbose("Inbound: Simple secured body");
                return ProcessSecuredBody(body, subjectPrefix, input, errorBox);
            }
        }

        private bool IsSignature(MimeEntity entity)
        {
            return
                entity.ContentType.MediaType == "application" &&
                entity.ContentType.MediaSubtype == "pgp-signature";
        }

        private bool GetSignedParts(Multipart body, out MimeEntity payload, out MimePart signature)
        {
            if (body.Count == 2)
            {
                if (IsSignature(body[0]) && !IsSignature(body[1]) &&
                    (body[0] is MimePart payload0))
                {
                    payload = body[1];
                    signature = payload0;
                    return true;
                }
                else if (!IsSignature(body[0]) && IsSignature(body[1]) &&
                         (body[1] is MimePart payload1))
                {
                    payload = body[0];
                    signature = payload1;
                    return true;
                }
                else
                {
                    payload = null;
                    signature = null;
                    return false;
                }
            }
            else
            {
                payload = null;
                signature = null;
                return false;
            }
        }

        private GpgTrust GetTrust(string keyId, MailboxAddress sender)
        {
            return _gpg.List(keyId).Single().Uids
                .Where(u => u.Mail == sender.Address).Max(u => u.Trust);
        }

        private string GetSignedTrustTag(GpgTrust trust)
        {
            switch (trust)
            {
                case GpgTrust.Full:
                case GpgTrust.Ultimate:
                    return GpgTags.SubjectTagVerified;
                case GpgTrust.Marginal:
                    return GpgTags.SubjectTagUnverified;
                default:
                    return GpgTags.SubjectTagUntrusted;
            }
        }

        private Envelope ProcessSigned(Multipart body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            _context.Log.Verbose("Inbound: Processing signed mail");
            if (GetSignedParts(body, out MimeEntity payload, out MimePart signature))
            {
                AutoKeyImport(payload);
                var payloadBytes = GetPartBytes(payload);
                var signatureBytes = GetContentBytes(signature.Content);
                var result = _gpg.VerifyDetached(payloadBytes, signatureBytes);

                if (result.Status == GpgStatus.Success)
                {
                    _context.Log.Notice("Inbound: Signature verification successful");
                    var trust = GetTrust(result.Signer, input.Message.From.First() as MailboxAddress);
                    subjectPrefix += GetSignedTrustTag(trust);
                    return ProcessSecuredBody(payload, subjectPrefix, input, errorBox);
                }
                else
                {
                    _context.Log.Warning("Inbound: Signature verification failed");
                    subjectPrefix += GpgTags.SubjectTagBad;
                    return ProcessSecuredBody(payload, subjectPrefix, input, errorBox);
                }
            }
            _context.Log.Warning("Inbound: Bad signature data");
            input.Message.Subject = subjectPrefix + input.Message.Subject;
            return input;
        }

        private Envelope Process(Multipart body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            switch (body.ContentType.MediaSubtype)
            {
                case "signed":
                    return ProcessSigned(body, subjectPrefix, input, errorBox);
                case "encrypted":
                    return ProcessEncrypted(body, subjectPrefix, input, errorBox);
                default:
                    if (HasGpgAttachment(body))
                    {
                        _context.Log.Verbose("Inbound: Processing OpenPGP attachment");
                        return ProcessGpgAttachment(input, body, subjectPrefix, errorBox);
                    }
                    else
                    {
                        return ProcessSecuredBody(body, subjectPrefix, input, errorBox);
                    }
            }
        }

        private Envelope ProcessEncrypted(Multipart body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            _context.Log.Verbose("Inbound: Processing encrypted mail");
            var encryptedBody = body.FirstOrDefault(p => p.ContentType.MediaType == "application" && p.ContentType.MediaSubtype == "octet-stream") as MimePart;
            var bytes = GetContentBytes(encryptedBody.Content);
            var result = _gpg.Decrypt(bytes, out byte[] output);
            if (result.Status == GpgStatus.Success)
            {
                _context.Log.Notice("Inbound: Mail decrypted");
                subjectPrefix += GpgTags.SubjectTagEncrypted;
                if (TryLoadMime(output, out MimeEntity entity))
                {
                    if (!string.IsNullOrEmpty(result.Signer))
                    {
                        var trust = GetTrust(result.Signer, input.Message.From.First() as MailboxAddress);
                        subjectPrefix += GetSignedTrustTag(trust);
                    }

                    return Process(entity, subjectPrefix, input, errorBox);
                }
                else
                {
                    var text = new TextPart("plain");
                    text.Content = new MimeContent(new MemoryStream(output));
                    input.Message.Body = text;
                    input.Message.Subject = subjectPrefix + input.Message.Subject;
                    return input;
                }
            }
            _context.Log.Warning("Inbound: Cannot decrypt mail");
            input.Message.Subject = subjectPrefix + input.Message.Subject;
            return input;
        }

        private Envelope ProcessGpgAttachment(Envelope input, Multipart body, string subjectPrefix, IMailbox errorBox)
        {
            var gpgAttachements = body.Where(IsGpgAttachment);
            switch (gpgAttachements.Count())
            {
                case 0:
                    _context.Log.Warning("Inbound: GPG attachment not found.");
                    return ProcessSecuredBody(body, subjectPrefix, input, errorBox);
                case 1:
                    return ProcessGpgAttachment(input, gpgAttachements.Single(), subjectPrefix, errorBox);
                default:
                    _context.Log.Warning("Inbound: Multiple GPG attachments found.");
                    return ProcessSecuredBody(body, subjectPrefix, input, errorBox);
            }
        }

        private Envelope ProcessGpgAttachment(Envelope input, MimeEntity gpgAttachment, string subjectPrefix, IMailbox errorBox)
        {
            var bytes = GetAttachementBytes(gpgAttachment);
            var result = _gpg.Decrypt(bytes, out byte[] output);
            if (result.Status == GpgStatus.Success)
            {
                _context.Log.Notice("Inbound: Mail decrypted");
                subjectPrefix += GpgTags.SubjectTagEncrypted;
                if (TryLoadMime(output, out MimeEntity entity))
                {
                    if (!string.IsNullOrEmpty(result.Signer))
                    {
                        var trust = GetTrust(result.Signer, input.Message.From.First() as MailboxAddress);
                        subjectPrefix += GetSignedTrustTag(trust);
                    }

                    return Process(entity, subjectPrefix, input, errorBox);
                }
                else
                {
                    var text = new TextPart("plain");
                    text.Content = new MimeContent(new MemoryStream(output));
                    input.Message.Body = text;
                    input.Message.Subject = subjectPrefix + input.Message.Subject;
                    return input;
                }
            }
            _context.Log.Warning("Inbound: Cannot decrypt mail");
            input.Message.Subject = subjectPrefix + input.Message.Subject;
            return input;
        }

        private Envelope ProcessGpgAttachment(Envelope input, string subjectPrefix, IMailbox errorBox)
        {
            var gpgAttachements = input.Message.Attachments.Where(IsGpgAttachment);
            switch (gpgAttachements.Count())
            {
                case 0:
                    _context.Log.Warning("Inbound: GPG attachment not found.");
                    return input;
                case 1:
                    return ProcessGpgAttachment(input, gpgAttachements.Single(), subjectPrefix, errorBox);
                default:
                    _context.Log.Warning("Inbound: Multiple GPG attachments found.");
                    return input;
            }
        }

        private bool IsGpgAttachment(MimeEntity mimeEntity)
        {
            return mimeEntity?.ContentDisposition?.FileName == "encrypted.asc";
        }

        private bool HasGpgAttachment(MimeMessage message)
        {
            return message.Attachments.Any(IsGpgAttachment);
        }

        private bool HasGpgAttachment(Multipart message)
        {
            return message.Any(IsGpgAttachment);
        }

        private Envelope Process(MimePart body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            if (body.Content == null)
            {
                _context.Log.Verbose("Inbound: Empty data returned");
                return input;
            }

            AutoKeyImport(input.Message.Body);
            var bytes = GetContentBytes(body.Content);
            if (IsGpgData(bytes))
            {
                _context.Log.Verbose("Inbound: Processing OpenPGP inline data");
                var result = _gpg.Decrypt(bytes, out byte[] output);
                if (result.Status == GpgStatus.Success)
                {
                    _context.Log.Notice("Inbound: Inline data verified or decrypted");
                    subjectPrefix += GpgTags.SubjectTagEncrypted;
                    if (TryLoadMime(output, out MimeEntity entity))
                    {
                        if (!string.IsNullOrEmpty(result.Signer))
                        {
                            var trust = GetTrust(result.Signer, input.Message.From.First() as MailboxAddress);
                            subjectPrefix += GetSignedTrustTag(trust);
                        }

                        return ProcessSecuredBody(entity, subjectPrefix, input, errorBox);
                    }
                    else
                    {
                        var text = new TextPart("plain");
                        text.Content = new MimeContent(new MemoryStream(output));
                        input.Message.Body = text;
                        input.Message.Subject = subjectPrefix + input.Message.Subject;
                        _context.Log.Verbose("Inbound: Plain data returned");
                        return input;
                    }
                }
            }
            else if (HasGpgAttachment(input.Message))
            {
                _context.Log.Verbose("Inbound: Processing OpenPGP attachment");
                ProcessGpgAttachment(input, subjectPrefix, errorBox);
            }

            input.Message.Subject = subjectPrefix + input.Message.Subject;
            AutoKeyImport(input.Message.Body);
            _context.Log.Verbose("Inbound: Unsecured data returned");
            return input;
        }

        private bool TryLoadMime(byte[] data, out MimeEntity entity)
        {
            try
            {
                using (var stream = new MemoryStream(data))
                {
                    entity = MimeEntity.Load(stream);
                    return true;
                }
            }
            catch
            {
                entity = null;
                return false;
            }
        }

        private bool IsGpgData(byte[] bytes)
        {
            var text = Encoding.UTF8.GetString(bytes).Trim();
            return
                text.StartsWith("-----BEGIN PGP SIGNED MESSAGE-----", StringComparison.Ordinal) ||
                text.StartsWith("-----BEGIN PGP MESSAGE-----", StringComparison.Ordinal);
        }

        private byte[] GetContentBytes(IMimeContent content)
        {
            using (var stream = new MemoryStream())
            {
                content.DecodeTo(stream);
                return stream.ToArray();
            }
        }

        private byte[] GetAttachementBytes(MimeEntity attachment)
        {
            using (var stream = new MemoryStream())
            {
                attachment.WriteTo(stream);
                return stream.ToArray();
            }
        }

        private byte[] GetPartBytes(MimeEntity part)
        {
            using (var stream = new MemoryStream())
            {
                part.WriteTo(stream);
                return stream.ToArray();
            }
        }

        private Envelope Process(MimeEntity body, string subjectPrefix, Envelope input, IMailbox errorBox)
        {
            _context.Log.Info("Inbound: Processing mail");
            if (body is Multipart multipart)
            {
                return Process(multipart, subjectPrefix, input, errorBox);
            }
            if (body is MimePart mimepart)
            {
                return Process(mimepart, subjectPrefix, input, errorBox);
            }
            else
            {
                _context.Log.Warning("Inbound: Unkonwn data returned");
                return ProcessSecuredBody(body, subjectPrefix, input, errorBox);
            }
        }

        public Envelope Process(Envelope input, IMailbox errorBox)
        {
            return Process(input.Message.Body, string.Empty, input, errorBox);
        }
    }
}
