/*
  Printervention
  Authenticode validation and approved publisher matching for vendor executables.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Printervention
{
    internal static class AuthenticodeVerifier
    {
        private static readonly Guid GenericVerifyV2Action = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

        private static readonly IDictionary<string, string[]> ApprovedSignerTerms =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "Brother", new[] { "Brother Industries", "Brother International" } },
                { "Canon", new[] { "Canon Inc", "Canon U.S.A" } },
                { "Epson", new[] { "Seiko Epson", "Epson America" } },
                { "Fujifilm", new[] { "Fujifilm Business Innovation", "Fuji Xerox" } },
                { "Fujitsu", new[] { "Fujitsu Limited", "PFU Limited" } },
                { "HP", new[] { "HP Inc", "Hewlett-Packard" } },
                { "Konica Minolta", new[] { "Konica Minolta" } },
                { "Kyocera", new[] { "Kyocera Document Solutions", "Kyocera Corporation" } },
                { "Lexmark", new[] { "Lexmark International" } },
                { "OKI", new[] { "Oki Electric", "Oki Data" } },
                { "Panasonic", new[] { "Panasonic Connect", "Panasonic Corporation" } },
                { "Pantum", new[] { "Pantum", "Zhuhai Pantum" } },
                { "Ricoh", new[] { "Ricoh Company", "Ricoh USA" } },
                { "Riso", new[] { "Riso Kagaku" } },
                { "Savin", new[] { "Ricoh Company", "Ricoh USA" } },
                { "Sharp", new[] { "Sharp Corporation", "Sharp Electronics" } },
                { "Toshiba", new[] { "Toshiba Tec", "Toshiba America Business Solutions" } },
                { "Xerox", new[] { "Xerox Corporation" } }
            };

        public static string VerifyApprovedVendorExecutable(string filePath, string vendor)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A downloaded executable path is required.", "filePath");
            }

            string[] approvedTerms;
            if (string.IsNullOrWhiteSpace(vendor) || !ApprovedSignerTerms.TryGetValue(vendor, out approvedTerms))
            {
                throw new InvalidOperationException("No approved executable publishers are configured for " + (vendor ?? "this vendor") + ".");
            }

            VerifyEmbeddedSignature(filePath);

            string subject;
            using (var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath)))
            {
                subject = certificate.Subject ?? string.Empty;
            }

            foreach (var approvedTerm in approvedTerms)
            {
                if (subject.IndexOf(approvedTerm, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return subject;
                }
            }

            throw new InvalidOperationException(
                "Windows validated the package signature, but its publisher does not match " + vendor + ". Publisher: " + subject);
        }

        private static void VerifyEmbeddedSignature(string filePath)
        {
            var fileInformation = new WinTrustFileInfo
            {
                StructureSize = (uint)Marshal.SizeOf(typeof(WinTrustFileInfo)),
                FilePath = filePath
            };

            var fileInformationPointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WinTrustFileInfo)));
            try
            {
                Marshal.StructureToPtr(fileInformation, fileInformationPointer, false);
                var trustData = new WinTrustData
                {
                    StructureSize = (uint)Marshal.SizeOf(typeof(WinTrustData)),
                    UiChoice = 2,
                    RevocationChecks = 1,
                    UnionChoice = 1,
                    FileInformation = fileInformationPointer,
                    ProviderFlags = 0x80 | 0x2000 | 0x4000
                };

                var actionIdentifier = GenericVerifyV2Action;
                var result = WinVerifyTrust(new IntPtr(-1), ref actionIdentifier, ref trustData);
                if (result != 0)
                {
                    throw new Win32Exception(result,
                        "Windows rejected the downloaded vendor executable's Authenticode signature (0x" + result.ToString("X8") + ").");
                }
            }
            finally
            {
                Marshal.DestroyStructure(fileInformationPointer, typeof(WinTrustFileInfo));
                Marshal.FreeHGlobal(fileInformationPointer);
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustFileInfo
        {
            public uint StructureSize;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string FilePath;
            public IntPtr FileHandle;
            public IntPtr KnownSubject;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WinTrustData
        {
            public uint StructureSize;
            public IntPtr PolicyCallbackData;
            public IntPtr SipClientData;
            public uint UiChoice;
            public uint RevocationChecks;
            public uint UnionChoice;
            public IntPtr FileInformation;
            public uint StateAction;
            public IntPtr StateData;
            public IntPtr UrlReference;
            public uint ProviderFlags;
            public uint UiContext;
        }

        [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true, SetLastError = false)]
        private static extern int WinVerifyTrust(
            IntPtr windowHandle,
            [In] ref Guid actionIdentifier,
            ref WinTrustData trustData);
    }
}
