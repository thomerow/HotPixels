using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace HotPixels.Printing;

/// <summary>
/// Sends raw ESC/POS byte data through the Windows print spooler.
/// </summary>
/// <remarks>
/// ESC/POS is a raw byte protocol, not a page description language, so the job is submitted with the
/// "RAW" data type to bypass the printer driver's rendering entirely.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class WindowsSpoolerPrinter {

   #region P/Invoke Declarations

   [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
   static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool ClosePrinter(IntPtr hPrinter);

   [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
   class DOC_INFO_1 {
      public string pDocName;
      public string pOutputFile;
      public string pDatatype;
   }

   [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
   static extern bool StartDocPrinter(IntPtr hPrinter, int level, [In] DOC_INFO_1 pDocInfo);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool EndDocPrinter(IntPtr hPrinter);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool StartPagePrinter(IntPtr hPrinter);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool EndPagePrinter(IntPtr hPrinter);

   [DllImport("winspool.drv", SetLastError = true)]
   static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

   [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "EnumPrintersW")]
   static extern bool EnumPrinters(int flags, string name, int level, IntPtr pPrinterEnum, int cbBuf,
                                   out int pcbNeeded, out int pcReturned);

   /// <summary>
   /// PRINTER_INFO_4, the cheapest level that carries the printer name. Declared as a struct rather
   /// than sized by hand so that the marshaller applies the platform's alignment padding — on x64 the
   /// two pointers and the DWORD occupy 24 bytes, not the 20 they add up to.
   /// </summary>
   [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
   struct PRINTER_INFO_4 {
      [MarshalAs(UnmanagedType.LPWStr)] public string pPrinterName;
      [MarshalAs(UnmanagedType.LPWStr)] public string pServerName;
      public uint Attributes;
   }

   private const int PRINTER_ENUM_LOCAL = 0x00000002;
   private const int PRINTER_ENUM_CONNECTIONS = 0x00000004;

   #endregion P/Invoke Declarations

   /// <summary>
   /// Sends a byte array as raw data to the given printer queue.
   /// </summary>
   public static void SendBytes(string printerName, byte[] bytes) {
      if (!OpenPrinter(printerName, out IntPtr hPrinter, IntPtr.Zero)) {
         throw new InvalidOperationException($"OpenPrinter failed with printer name '{printerName}'.");
      }

      try {
         var docInfo = new DOC_INFO_1 {
            pDocName = "ESC/POS Raw Job",
            pDatatype = "RAW"
         };

         if (!StartDocPrinter(hPrinter, 1, docInfo)) {
            throw new InvalidOperationException("StartDocPrinter failed.");
         }
         try {
            if (!StartPagePrinter(hPrinter)) {
               throw new InvalidOperationException("StartPagePrinter failed.");
            }
            try {
               IntPtr pUnmanagedBytes = Marshal.AllocHGlobal(bytes.Length);
               try {
                  Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);
                  if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out int _)) {
                     throw new InvalidOperationException("WritePrinter failed.");
                  }
               }
               finally {
                  Marshal.FreeHGlobal(pUnmanagedBytes);
               }
            }
            finally {
               EndPagePrinter(hPrinter);
            }
         }
         finally {
            EndDocPrinter(hPrinter);
         }
      }
      finally {
         ClosePrinter(hPrinter);
      }
   }

   /// <summary>
   /// Returns the names of the printers installed on this machine.
   /// </summary>
   /// <remarks>
   /// EnumPrinters is called twice, as the API requires: once with a zero-sized buffer to learn how many
   /// bytes it needs, then again with a buffer of that size.
   /// </remarks>
   public static IEnumerable<string> ListPrinters() {
      const int level = 4;
      int flags = PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS;

      // First call: ask for the required buffer size. This one is expected to fail.
      EnumPrinters(flags, null, level, IntPtr.Zero, 0, out int needed, out int _);
      if (needed <= 0) return [];

      List<string> names = [];
      IntPtr buffer = Marshal.AllocHGlobal(needed);
      try {
         if (EnumPrinters(flags, null, level, buffer, needed, out int _, out int count)) {
            int entrySize = Marshal.SizeOf<PRINTER_INFO_4>();
            for (int i = 0; i < count; ++i) {
               var info = Marshal.PtrToStructure<PRINTER_INFO_4>(buffer + (i * entrySize));
               if (!string.IsNullOrEmpty(info.pPrinterName)) names.Add(info.pPrinterName);
            }
         }
      }
      finally {
         Marshal.FreeHGlobal(buffer);
      }

      return names;
   }
}
