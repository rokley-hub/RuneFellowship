using System.Runtime.InteropServices;

namespace Rune.Voice;

// Windows PCM playback routed to an explicitly selected output device.
internal sealed class OutputAudio : IDisposable
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Caps { public ushort manufacturer, product; public uint version; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name; public uint formats; public ushort channels, reserved; public uint support; }
    [StructLayout(LayoutKind.Sequential, Pack = 2)]
    private struct Format { public ushort tag, channels; public uint rate, bytesPerSecond; public ushort align, bits, extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Header { public IntPtr data; public uint length, recorded; public IntPtr user; public uint flags, loops; public IntPtr next, reserved; }
    [DllImport("winmm.dll")] private static extern uint waveOutGetNumDevs();
    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern uint waveOutGetDevCapsW(UIntPtr id, out Caps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutOpen(out IntPtr handle, uint device, ref Format format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] private static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] private static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] private static extern uint waveOutClose(IntPtr handle);
    private IntPtr handle, header, data;
    private bool prepared;
    private static uint HeaderSize => (uint)Marshal.SizeOf<Header>();
    public bool IsPlaying => header != IntPtr.Zero && (Marshal.PtrToStructure<Header>(header).flags & 1) == 0;
    public static string[] Devices() => Enumerable.Range(0, (int)waveOutGetNumDevs()).Select(i => waveOutGetDevCapsW((UIntPtr)i, out var caps, (uint)Marshal.SizeOf<Caps>()) == 0 ? caps.name : "Unavailable output").ToArray();
    public OutputAudio(byte[] wav, string outputName, int volume = 100)
    {
        try {
            using var reader = new BinaryReader(new MemoryStream(wav));
            if (new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Invalid voice audio.");
            reader.ReadUInt32(); if (new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Invalid voice audio.");
            Format format = default; byte[]? samples = null;
            while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length) {
                string chunk = new(reader.ReadChars(4)); int count = reader.ReadInt32(); long end = reader.BaseStream.Position + count;
                if (count < 0 || end > wav.Length) throw new InvalidDataException("Incomplete voice audio.");
                if (chunk == "fmt ") { format.tag = reader.ReadUInt16(); format.channels = reader.ReadUInt16(); format.rate = reader.ReadUInt32(); format.bytesPerSecond = reader.ReadUInt32(); format.align = reader.ReadUInt16(); format.bits = reader.ReadUInt16(); }
                else if (chunk == "data") samples = reader.ReadBytes(count);
                reader.BaseStream.Position = end + (count & 1);
            }
            if (format.tag != 1 || samples == null || samples.Length == 0) throw new InvalidDataException("Voice audio must be PCM.");
            ScalePcm(samples, format.bits, volume);
            int device = string.IsNullOrEmpty(outputName) ? -1 : Array.IndexOf(Devices(), outputName);
            if (device < 0 && !string.IsNullOrEmpty(outputName)) throw new InvalidOperationException("Your selected voice output is unavailable. Choose it again in Audio setup.");
            Check(waveOutOpen(out handle, unchecked((uint)device), ref format, IntPtr.Zero, IntPtr.Zero, 0));
            data = Marshal.AllocHGlobal(samples.Length); Marshal.Copy(samples, 0, data, samples.Length);
            header = Marshal.AllocHGlobal((int)HeaderSize); Marshal.StructureToPtr(new Header { data = data, length = (uint)samples.Length }, header, false);
            Check(waveOutPrepareHeader(handle, header, HeaderSize)); prepared = true; Check(waveOutWrite(handle, header, HeaderSize));
        } catch { Dispose(); throw; }
    }
    internal static void ScalePcm(byte[] samples, int bits, int volume)
    {
        double gain = Math.Clamp(volume, 0, 100) / 100.0;
        if (gain == 1) return;
        if (bits == 16) for (int i = 0; i + 1 < samples.Length; i += 2) {
            short value = (short)Math.Round(BitConverter.ToInt16(samples, i) * gain);
            samples[i] = (byte)value; samples[i + 1] = (byte)(value >> 8);
        }
        else if (bits == 8) for (int i = 0; i < samples.Length; i++) samples[i] = (byte)(128 + Math.Round((samples[i] - 128) * gain));
        else throw new InvalidDataException("Voice volume requires 8-bit or 16-bit PCM.");
    }
    private static void Check(uint result) { if (result != 0) throw new InvalidOperationException("Voice output could not start (audio code " + result + "). Check Audio setup."); }
    public void Dispose()
    {
        if (handle != IntPtr.Zero) { waveOutReset(handle); if (prepared) waveOutUnprepareHeader(handle, header, HeaderSize); waveOutClose(handle); handle = IntPtr.Zero; }
        if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
        if (data != IntPtr.Zero) { Marshal.FreeHGlobal(data); data = IntPtr.Zero; }
    }
}
