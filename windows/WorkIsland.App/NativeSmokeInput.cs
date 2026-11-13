using System.Runtime.InteropServices;

namespace WorkIsland.App;

// Used exclusively with explicit --native-smoke and isolated test data.
// F24 carries no typed content; refuse injection if another window has focus.
internal static class NativeSmokeInput
{
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort VirtualKey, Scan; public uint Flags, Time; public nint Extra; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion
    { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input
    { public uint Type; public InputUnion Union; }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint SendInput(uint count, Input[] inputs, int size);
    public static void SendF24ToOwnWindow(nint hwnd)
    {
        if (hwnd == 0 || GetForegroundWindow() != hwnd) throw new InvalidOperationException("Native smoke requires focus on its own window; no input was sent.");
        var inputs = new[] { new Input { Type = 1, Union = new() { Keyboard = new() { VirtualKey = 0x87 } } },
            new Input { Type = 1, Union = new() { Keyboard = new() { VirtualKey = 0x87, Flags = 2 } } } };
        if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2) throw new InvalidOperationException("Native test input was rejected by Windows.");
    }
}
