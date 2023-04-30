using System;
namespace OpenPgpMailProxy
{
    public static class GpgTags
    {
        public const string SubjectTagEncrypt = "[encrypt]";
        public const string SubjectTagEncrypted = "[encrypted]";
        public const string SubjectTagUntrusted = "[untrusted]";
        public const string SubjectTagUnverified = "[unverified]";
        public const string SubjectTagVerified = "[verified]";
        public const string SubjectTagBad = "[bad]";
        public static string[] SubjectTags = new string[] {
            SubjectTagEncrypt,
            SubjectTagEncrypted,
            SubjectTagUntrusted,
            SubjectTagUnverified,
            SubjectTagVerified,
            SubjectTagBad,
        };
    }
}
